import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpEvent, HttpEventType, HttpResponse } from '@angular/common/http';
import { of, throwError, Subject } from 'rxjs';
import { ResumableUploaderService } from './resumable-uploader.service';
import { ApiService } from './api.service';
import { SignalRService } from './signalr.service';
import { FileItem } from '../models/file.models';
import { UploadSession } from '../models/upload.models';

describe('ResumableUploaderService', () => {
  let service: ResumableUploaderService;
  let apiService: jasmine.SpyObj<ApiService>;
  let signalRService: jasmine.SpyObj<SignalRService>;

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj('ApiService', [
      'directStreamUpload',
      'initUploadSession',
      'uploadChunk',
      'completeUploadSession',
      'getUploadSession',
      'abortUploadSession'
    ]);

    const signalRSpy = jasmine.createSpyObj('SignalRService', [
      'subscribeToUploadSession',
      'unsubscribeFromUploadSession'
    ], {
      fileProgress$: new Subject()
    });

    signalRSpy.subscribeToUploadSession.and.returnValue(Promise.resolve());
    signalRSpy.unsubscribeFromUploadSession.and.returnValue(Promise.resolve());

    TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: apiSpy },
        { provide: SignalRService, useValue: signalRSpy },
        ResumableUploaderService
      ]
    });

    service = TestBed.inject(ResumableUploaderService);
    apiService = TestBed.inject(ApiService) as jasmine.SpyObj<ApiService>;
    signalRService = TestBed.inject(SignalRService) as jasmine.SpyObj<SignalRService>;
  });

  afterEach(() => {
    service.ngOnDestroy();
  });

  it('should initialize with empty queue', () => {
    expect(service.tasks().length).toBe(0);
    expect(service.activeCount()).toBe(0);
    expect(service.completedCount()).toBe(0);
    expect(service.hasActiveUploads()).toBeFalse();
    expect(service.overallProgress()).toBe(0);
    expect(service.overallSpeedBytesPerSec()).toBe(0);
  });

  it('should enqueue files and open tray', () => {
    apiService.directStreamUpload.and.returnValue(new Subject());
    const file1 = new File(['hello'], 'hello.txt', { type: 'text/plain' });
    const file2 = new File(['world!'], 'world.txt', { type: 'text/plain' });

    service.enqueue([file1, file2], 'folder-1', 'Documents');

    expect(service.tasks().length).toBe(2);
    expect(service.isTrayOpen()).toBeTrue();
    expect(service.tasks()[0].file.name).toBe('hello.txt');
    expect(service.tasks()[0].targetFolderId).toBe('folder-1');
  });

  it('should immediately flag empty 0-byte files with an error and not upload them', () => {
    const emptyFile = new File([], 'empty.txt', { type: 'text/plain' });

    service.enqueue([emptyFile]);

    expect(service.tasks().length).toBe(1);
    const task = service.tasks()[0];
    expect(task.status).toBe('error');
    expect(task.errorMessage).toContain('Empty files (0 bytes) cannot be uploaded');
    expect(apiService.directStreamUpload).not.toHaveBeenCalled();
    expect(apiService.initUploadSession).not.toHaveBeenCalled();
  });

  it('should toggle and set tray visibility', () => {
    service.setTrayOpen(false);
    expect(service.isTrayOpen()).toBeFalse();

    service.toggleTray();
    expect(service.isTrayOpen()).toBeTrue();

    service.toggleTray();
    expect(service.isTrayOpen()).toBeFalse();
  });

  it('should handle direct stream upload progress and completion for small file', fakeAsync(() => {
    const streamSubject = new Subject<HttpEvent<FileItem>>();
    apiService.directStreamUpload.and.returnValue(streamSubject.asObservable());

    const smallFile = new File(['small content'], 'small.txt', { type: 'text/plain' });
    service.enqueue([smallFile]);

    const task = service.tasks()[0];
    expect(task.status).toBe('uploading');

    // Emit progress
    streamSubject.next({
      type: HttpEventType.UploadProgress,
      loaded: 5,
      total: 12
    } as any);

    tick();
    const progressedTask = service.tasks()[0];
    expect(progressedTask.bytesUploaded).toBe(5);
    expect(progressedTask.progress).toBe(42);

    // Emit completion response
    streamSubject.next(new HttpResponse({
      body: {
        id: 'file-1',
        userId: 'u1',
        storageBlobId: 'b1',
        name: 'small.txt',
        extension: 'txt',
        mimeType: 'text/plain',
        sizeBytes: 12,
        isDeleted: false,
        isFavorite: false,
        isShared: false,
        createdAt: '',
        updatedAt: ''
      }
    }));

    tick();
    const completedTask = service.tasks()[0];
    expect(completedTask.status).toBe('completed');
    expect(completedTask.progress).toBe(100);
  }));

  it('should abort in-flight direct stream upload when paused or cancelled', fakeAsync(() => {
    const streamSubject = new Subject<HttpEvent<FileItem>>();
    apiService.directStreamUpload.and.returnValue(streamSubject.asObservable());

    const smallFile = new File(['small content'], 'small.txt', { type: 'text/plain' });
    service.enqueue([smallFile]);

    const task = service.tasks()[0];
    expect(task.status).toBe('uploading');

    service.pauseTask(task.id);
    tick();

    expect(service.tasks()[0].status).toBe('paused');
    expect(streamSubject.observed).toBeFalse(); // Unsubscribed!
  }));

  it('should resume chunked upload by reusing existing session and skipping uploaded chunks', async () => {
    // 60 MB file -> 3 chunks of 20MB (chunk 0, 1, 2)
    const largeSize = 60 * 1024 * 1024;
    const largeBlob = new Blob([new Uint8Array(100)], { type: 'application/octet-stream' });
    const largeFile = new File([largeBlob], 'movie.mp4', { type: 'video/mp4' });
    Object.defineProperty(largeFile, 'size', { value: largeSize });

    const mockSession: UploadSession = {
      sessionId: 'sess-existing-123',
      fileName: 'movie.mp4',
      totalSizeBytes: largeSize,
      chunkSizeBytes: 20 * 1024 * 1024,
      totalChunks: 3,
      uploadMode: 'Proxy',
      status: 'Uploading',
      uploadedChunks: [0], // Chunk 0 was already uploaded!
      missingChunks: [1, 2],
      progressPercentage: 33.3,
      createdAt: '',
      expiresAt: ''
    };

    apiService.getUploadSession.and.returnValue(of(mockSession));
    apiService.uploadChunk.and.returnValue(of({
      sessionId: 'sess-existing-123',
      chunkIndex: 1,
      sizeBytes: 20 * 1024 * 1024,
      sha256Hash: '',
      totalChunks: 3,
      uploadedChunksCount: 2,
      progressPercentage: 66,
      isCompleted: false
    }));
    apiService.completeUploadSession.and.returnValue(of({} as FileItem));

    const taskId = 'resumed-task-1';
    (service.tasks as any).set([
      {
        id: taskId,
        file: largeFile,
        targetFolderId: null,
        targetFolderName: 'Root Drive',
        status: 'paused',
        sessionId: 'sess-existing-123',
        progress: 33,
        bytesUploaded: 20 * 1024 * 1024,
        totalBytes: largeSize,
        speedBytesPerSec: 0,
        currentChunkIndex: 0,
        totalChunks: 3,
        startTime: Date.now()
      }
    ]);

    // Resume task
    service.resumeTask(taskId);

    // Wait for async processing of chunks
    for (let i = 0; i < 20; i++) {
      if (service.tasks()[0].status === 'completed') break;
      await new Promise(r => setTimeout(r, 20));
    }

    // Verify it fetched the session instead of calling initUploadSession!
    expect(apiService.getUploadSession).toHaveBeenCalledWith('sess-existing-123');
    expect(apiService.initUploadSession).not.toHaveBeenCalled();

    // Verify chunks 1 and 2 were uploaded, but chunk 0 was skipped!
    expect(apiService.uploadChunk).toHaveBeenCalledWith('sess-existing-123', 1, jasmine.any(Blob), jasmine.any(String));
    expect(apiService.uploadChunk).toHaveBeenCalledWith('sess-existing-123', 2, jasmine.any(Blob), jasmine.any(String));
    expect(apiService.uploadChunk).not.toHaveBeenCalledWith('sess-existing-123', 0, jasmine.any(Blob), jasmine.any(String));

    // Verify completion
    expect(apiService.completeUploadSession).toHaveBeenCalledWith('sess-existing-123', jasmine.any(Object));
    expect(service.tasks()[0].status).toBe('completed');
  });

  it('should cancel active task and abort server upload session', fakeAsync(() => {
    apiService.abortUploadSession.and.returnValue(of(undefined as any));

    const file = new File(['content'], 'test.txt', { type: 'text/plain' });
    apiService.directStreamUpload.and.returnValue(new Subject());
    service.enqueue([file]);

    const task = service.tasks()[0];
    (service.tasks as any).update((list: any[]) =>
      list.map(t => (t.id === task.id ? { ...t, sessionId: 's-to-abort' } : t))
    );

    service.cancelTask(task.id);
    tick();

    expect(service.tasks()[0].status).toBe('cancelled');
    expect(apiService.abortUploadSession).toHaveBeenCalledWith('s-to-abort');
  }));

  it('should clear completed and cancelled tasks', () => {
    (service.tasks as any).set([
      { id: '1', status: 'completed' },
      { id: '2', status: 'uploading' },
      { id: '3', status: 'cancelled' },
      { id: '4', status: 'error' }
    ]);

    service.clearCompleted();

    expect(service.tasks().map(t => t.id)).toEqual(['2', '4']);
  });
});
