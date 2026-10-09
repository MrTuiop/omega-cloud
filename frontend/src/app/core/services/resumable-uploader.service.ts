import { Injectable, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpEventType } from '@angular/common/http';
import { firstValueFrom, Subscription } from 'rxjs';
import { ApiService } from './api.service';
import { SignalRService } from './signalr.service';
import { UploadSession, UploadTask } from '../models/upload.models';

const CHUNK_SIZE = 20 * 1024 * 1024; // 20 MB chunks as per master plan
const DIRECT_STREAM_LIMIT = 50 * 1024 * 1024; // 50 MB threshold

@Injectable({
  providedIn: 'root'
})
export class ResumableUploaderService implements OnDestroy {
  private readonly apiService = inject(ApiService);
  private readonly signalRService = inject(SignalRService);

  private readonly activeSubscriptions = new Map<string, Subscription>();
  private fileProgressSub: Subscription | null = null;

  readonly tasks = signal<UploadTask[]>([]);
  readonly isTrayOpen = signal<boolean>(false);

  readonly activeCount = computed(() =>
    this.tasks().filter(t => ['queued', 'hashing', 'uploading', 'assembling'].includes(t.status)).length
  );

  readonly completedCount = computed(() =>
    this.tasks().filter(t => t.status === 'completed').length
  );

  readonly hasActiveUploads = computed(() => this.activeCount() > 0);

  readonly overallProgress = computed(() => {
    const list = this.tasks();
    if (list.length === 0) return 0;
    const totalBytes = list.reduce((sum, t) => sum + t.totalBytes, 0);
    if (totalBytes === 0) return 0;
    const uploadedBytes = list.reduce((sum, t) => sum + t.bytesUploaded, 0);
    return Math.min(100, Math.round((uploadedBytes / totalBytes) * 100));
  });

  readonly overallSpeedBytesPerSec = computed(() => {
    return this.tasks()
      .filter(t => t.status === 'uploading')
      .reduce((sum, t) => sum + t.speedBytesPerSec, 0);
  });

  constructor() {
    // Listen for server-side FileProgress events (e.g. during assembly of large chunked files)
    this.fileProgressSub = this.signalRService.fileProgress$.subscribe(event => {
      const task = this.tasks().find(t => t.sessionId === event.sessionId);
      if (task && (task.status === 'assembling' || task.status === 'uploading')) {
        this.updateTask(task.id, t => ({
          ...t,
          progress: Math.max(t.progress, Math.min(99, Math.round(event.percent))),
          speedBytesPerSec: event.speedBytesPerSec
        }));
      }
    });
  }

  ngOnDestroy(): void {
    if (this.fileProgressSub) {
      this.fileProgressSub.unsubscribe();
    }
    this.activeSubscriptions.forEach(sub => sub.unsubscribe());
    this.activeSubscriptions.clear();
  }

  enqueue(files: FileList | File[], targetFolderId?: string | null, targetFolderName?: string): void {
    const newTasks: UploadTask[] = [];

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const taskId = `upload_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;

      if (file.size === 0) {
        const task: UploadTask = {
          id: taskId,
          file,
          targetFolderId,
          targetFolderName: targetFolderName ?? (targetFolderId ? 'Folder' : 'Root Drive'),
          status: 'error',
          errorMessage: 'Empty files (0 bytes) cannot be uploaded.',
          progress: 0,
          bytesUploaded: 0,
          totalBytes: 0,
          speedBytesPerSec: 0,
          currentChunkIndex: 0,
          totalChunks: 1,
          startTime: Date.now()
        };
        newTasks.push(task);
        continue;
      }

      const totalChunks = file.size > DIRECT_STREAM_LIMIT ? Math.ceil(file.size / CHUNK_SIZE) : 1;

      const task: UploadTask = {
        id: taskId,
        file,
        targetFolderId,
        targetFolderName: targetFolderName ?? (targetFolderId ? 'Folder' : 'Root Drive'),
        status: 'queued',
        progress: 0,
        bytesUploaded: 0,
        totalBytes: file.size,
        speedBytesPerSec: 0,
        currentChunkIndex: 0,
        totalChunks,
        startTime: Date.now()
      };

      newTasks.push(task);
    }

    this.tasks.update(curr => [...curr, ...newTasks]);
    this.isTrayOpen.set(true);

    // Start processing queued tasks
    this.processQueue();
  }

  toggleTray(): void {
    this.isTrayOpen.update(v => !v);
  }

  setTrayOpen(open: boolean): void {
    this.isTrayOpen.set(open);
  }

  pauseTask(taskId: string): void {
    const sub = this.activeSubscriptions.get(taskId);
    if (sub) {
      sub.unsubscribe();
      this.activeSubscriptions.delete(taskId);
    }

    this.updateTask(taskId, task => {
      if (task.abortController) {
        task.abortController.abort();
      }
      return { ...task, status: 'paused', speedBytesPerSec: 0 };
    });
  }

  resumeTask(taskId: string): void {
    const task = this.tasks().find(t => t.id === taskId);
    if (task && task.status === 'paused') {
      this.updateTask(taskId, t => ({ ...t, status: 'queued' }));
      this.processQueue();
    }
  }

  async cancelTask(taskId: string): Promise<void> {
    const task = this.tasks().find(t => t.id === taskId);
    if (!task) return;

    const sub = this.activeSubscriptions.get(taskId);
    if (sub) {
      sub.unsubscribe();
      this.activeSubscriptions.delete(taskId);
    }

    if (task.abortController) {
      task.abortController.abort();
    }

    if (task.sessionId) {
      try {
        await this.signalRService.unsubscribeFromUploadSession(task.sessionId);
        await firstValueFrom(this.apiService.abortUploadSession(task.sessionId));
      } catch {
        // Ignore session abort error
      }
    }

    this.updateTask(taskId, t => ({ ...t, status: 'cancelled', speedBytesPerSec: 0 }));
  }

  removeTask(taskId: string): void {
    this.cancelTask(taskId);
    this.tasks.update(list => list.filter(t => t.id !== taskId));
  }

  clearCompleted(): void {
    this.tasks.update(list => list.filter(t => t.status !== 'completed' && t.status !== 'cancelled'));
  }

  private async processQueue(): Promise<void> {
    const queuedTasks = this.tasks().filter(t => t.status === 'queued');
    for (const task of queuedTasks) {
      const runningCount = this.tasks().filter(t => t.status === 'uploading' || t.status === 'hashing').length;
      if (runningCount >= 2) {
        break;
      }
      this.startUpload(task.id);
    }
  }

  private async startUpload(taskId: string): Promise<void> {
    const task = this.tasks().find(t => t.id === taskId);
    if (!task || task.status !== 'queued') return;

    const abortController = new AbortController();
    this.updateTask(taskId, t => ({ ...t, status: 'uploading', abortController }));

    try {
      if (task.file.size <= DIRECT_STREAM_LIMIT) {
        await this.uploadDirect(taskId);
      } else {
        await this.uploadResumableChunks(taskId);
      }
    } catch (err: any) {
      const currentTask = this.tasks().find(t => t.id === taskId);
      if (abortController.signal.aborted || currentTask?.status === 'paused' || currentTask?.status === 'cancelled') {
        return;
      }
      this.updateTask(taskId, t => ({
        ...t,
        status: 'error',
        errorMessage: err?.error?.message ?? err?.message ?? 'Upload failed.',
        speedBytesPerSec: 0
      }));
    } finally {
      this.activeSubscriptions.delete(taskId);
      this.processQueue();
    }
  }

  private async uploadDirect(taskId: string): Promise<void> {
    const task = this.tasks().find(t => t.id === taskId);
    if (!task) return;

    let lastBytes = 0;
    let lastTime = Date.now();

    await new Promise<void>((resolve, reject) => {
      const sub = this.apiService.directStreamUpload(task.file, task.targetFolderId).subscribe({
        next: event => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            const now = Date.now();
            const timeDiff = (now - lastTime) / 1000;
            let speed = 0;
            if (timeDiff >= 0.5) {
              speed = (event.loaded - lastBytes) / timeDiff;
              lastBytes = event.loaded;
              lastTime = now;
            }

            const pct = Math.round((event.loaded / event.total) * 100);
            this.updateTask(taskId, t => ({
              ...t,
              bytesUploaded: event.loaded,
              progress: pct,
              speedBytesPerSec: speed > 0 ? speed : t.speedBytesPerSec
            }));
          } else if (event.type === HttpEventType.Response) {
            this.updateTask(taskId, t => ({
              ...t,
              bytesUploaded: t.totalBytes,
              progress: 100,
              status: 'completed',
              speedBytesPerSec: 0
            }));
            resolve();
          }
        },
        error: err => reject(err)
      });

      this.activeSubscriptions.set(taskId, sub);
    });
  }

  private async uploadResumableChunks(taskId: string): Promise<void> {
    let currentTask = this.tasks().find(t => t.id === taskId);
    if (!currentTask) return;

    const file = currentTask.file;
    const totalChunks = currentTask.totalChunks;

    // Check if task already has an existing session that can be resumed
    let session: UploadSession | null = null;
    if (currentTask.sessionId) {
      try {
        session = await firstValueFrom(this.apiService.getUploadSession(currentTask.sessionId));
        if (session.status === 'Completed' || session.status === 'Aborted' || session.status === 'Expired') {
          session = null;
        }
      } catch {
        session = null;
      }
    }

    let fileSha256: string | undefined;

    if (!session) {
      // 1. Optional fast WebCrypto SHA-256 for files <= 100MB
      if (file.size <= 100 * 1024 * 1024) {
        this.updateTask(taskId, t => ({ ...t, status: 'hashing' }));
        try {
          fileSha256 = await this.computeSha256(file);
        } catch {
          // Skip hash on memory constraint
        }
      }

      currentTask = this.tasks().find(t => t.id === taskId);
      if (!currentTask || currentTask.status === 'paused' || currentTask.status === 'cancelled') {
        return;
      }

      this.updateTask(taskId, t => ({ ...t, status: 'uploading' }));

      // 2. Initialize new Upload Session
      session = await firstValueFrom(
        this.apiService.initUploadSession({
          fileName: file.name,
          totalSizeBytes: file.size,
          chunkSizeBytes: CHUNK_SIZE,
          totalChunks,
          expectedSha256: fileSha256,
          targetFolderId: currentTask.targetFolderId ?? undefined
        })
      );

      this.updateTask(taskId, t => ({ ...t, sessionId: session!.sessionId }));
      await this.signalRService.subscribeToUploadSession(session.sessionId);
    } else {
      this.updateTask(taskId, t => ({ ...t, status: 'uploading' }));
      await this.signalRService.subscribeToUploadSession(session.sessionId);
    }

    currentTask = this.tasks().find(t => t.id === taskId);
    if (!currentTask || currentTask.status === 'paused' || currentTask.status === 'cancelled') {
      return;
    }

    // 3. Upload missing chunks
    const uploadedSet = new Set(session.uploadedChunks || []);
    let uploadedBytesTotal = 0;

    // Calculate bytes of already uploaded chunks
    for (let i = 0; i < totalChunks; i++) {
      if (uploadedSet.has(i)) {
        const start = i * CHUNK_SIZE;
        const end = Math.min(start + CHUNK_SIZE, file.size);
        uploadedBytesTotal += (end - start);
      }
    }

    if (uploadedBytesTotal > 0) {
      const initialProgress = Math.min(99, Math.round((uploadedBytesTotal / file.size) * 100));
      this.updateTask(taskId, t => ({
        ...t,
        bytesUploaded: uploadedBytesTotal,
        progress: initialProgress
      }));
    }

    let lastTime = Date.now();
    let lastBytes = uploadedBytesTotal;

    for (let chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++) {
      currentTask = this.tasks().find(t => t.id === taskId);
      if (!currentTask || currentTask.status === 'paused' || currentTask.status === 'cancelled') {
        return;
      }

      const start = chunkIndex * CHUNK_SIZE;
      const end = Math.min(start + CHUNK_SIZE, file.size);
      const chunkSize = end - start;

      if (uploadedSet.has(chunkIndex)) {
        continue;
      }

      const chunkBlob = file.slice(start, end);
      const chunkSha = await this.computeSha256(chunkBlob);

      this.updateTask(taskId, t => ({ ...t, currentChunkIndex: chunkIndex }));

      // Perform chunk upload with cancellable subscription
      await new Promise<void>((resolve, reject) => {
        const sub = this.apiService.uploadChunk(session!.sessionId, chunkIndex, chunkBlob, chunkSha).subscribe({
          next: () => resolve(),
          error: err => reject(err)
        });
        this.activeSubscriptions.set(taskId, sub);
      });
      this.activeSubscriptions.delete(taskId);

      uploadedSet.add(chunkIndex);
      uploadedBytesTotal += chunkSize;

      const now = Date.now();
      const elapsed = (now - lastTime) / 1000;
      let speed = elapsed > 0 ? (uploadedBytesTotal - lastBytes) / elapsed : 0;
      lastBytes = uploadedBytesTotal;
      lastTime = now;

      const progress = Math.min(99, Math.round((uploadedBytesTotal / file.size) * 100));
      this.updateTask(taskId, t => ({
        ...t,
        bytesUploaded: uploadedBytesTotal,
        progress,
        speedBytesPerSec: speed
      }));
    }

    // 4. Complete Session
    currentTask = this.tasks().find(t => t.id === taskId);
    if (!currentTask || currentTask.status === 'paused' || currentTask.status === 'cancelled') {
      return;
    }

    this.updateTask(taskId, t => ({ ...t, status: 'assembling', speedBytesPerSec: 0 }));

    await firstValueFrom(
      this.apiService.completeUploadSession(session.sessionId, { expectedSha256: fileSha256 })
    );

    await this.signalRService.unsubscribeFromUploadSession(session.sessionId);

    this.updateTask(taskId, t => ({
      ...t,
      status: 'completed',
      progress: 100,
      bytesUploaded: t.totalBytes,
      speedBytesPerSec: 0
    }));
  }

  private async computeSha256(blob: Blob): Promise<string> {
    if (typeof crypto === 'undefined' || !crypto.subtle) {
      return '';
    }
    try {
      const buffer = await blob.arrayBuffer();
      const hashBuffer = await crypto.subtle.digest('SHA-256', buffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    } catch {
      return '';
    }
  }

  private updateTask(taskId: string, updater: (task: UploadTask) => UploadTask): void {
    this.tasks.update(list => list.map(t => (t.id === taskId ? updater(t) : t)));
  }
}
