import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { environment } from '../../../environments/environment';

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;
  const baseUrl = environment.apiUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService
      ]
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should call login endpoint', () => {
    const loginReq = { usernameOrEmail: 'admin', password: 'password' };
    service.login(loginReq).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(loginReq);
    req.flush({ accessToken: 'a', refreshToken: 'r', expiresInSeconds: 900 });
  });

  it('should call getRootFolderContent', () => {
    service.getRootFolderContent().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/folders`);
    expect(req.request.method).toBe('GET');
    req.flush({ folders: [], files: [], breadcrumbs: [], quota: { allocatedBytes: 100, usedBytes: 10, availableBytes: 90 } });
  });

  it('should call createFolder', () => {
    const payload = { name: 'New Folder', color: '#0071e3' };
    service.createFolder(payload).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/folders`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({ id: 'f-1', name: 'New Folder' });
  });

  it('should call updateFile', () => {
    service.updateFile('file-123', { name: 'Renamed.txt' }).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/file-123`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ name: 'Renamed.txt' });
    req.flush({ id: 'file-123', name: 'Renamed.txt' });
  });

  it('should call initUploadSession', () => {
    const initReq = { fileName: 'bigfile.zip', totalSizeBytes: 100000000 };
    service.initUploadSession(initReq).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/upload/sessions/init`);
    expect(req.request.method).toBe('POST');
    req.flush({ sessionId: 's-1', fileName: 'bigfile.zip' });
  });

  it('should call getTrash', () => {
    service.getTrash().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/trash?topLevelOnly=false`);
    expect(req.request.method).toBe('GET');
    req.flush({ folders: [], files: [], totalTrashSizeBytes: 0, totalItemCount: 0 });
  });

  it('should call getAdminUsers', () => {
    service.getAdminUsers().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/admin/users`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('should call revokeAdminUserSession', () => {
    service.revokeAdminUserSession('user-1', 'session-1').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/admin/users/user-1/sessions/session-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush({ message: 'Session revoked' });
  });

  it('should call getAdminNodes', () => {
    service.getAdminNodes().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/admin/nodes`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('should call getAuditLogs', () => {
    service.getAuditLogs({ action: 'UserLogin' }).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/admin/audit?action=UserLogin`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('should call getSystemHealth', () => {
    service.getSystemHealth().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/admin/health`);
    expect(req.request.method).toBe('GET');
    req.flush({ cpuLoadPercentage: 10, databaseStatus: 'Healthy' });
  });

  it('should call downloadZip', () => {
    service.downloadZip(['f-1', 'f-2']).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/download-zip`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ fileIds: ['f-1', 'f-2'], folderIds: undefined });
    req.flush(new Blob());
  });

  it('should call bulkDelete', () => {
    service.bulkDelete(['f-1'], ['folder-1']).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/bulk-delete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ fileIds: ['f-1'], folderIds: ['folder-1'] });
    req.flush(null);
  });

  it('should call bulkMove', () => {
    service.bulkMove(['f-1'], undefined, 'target-folder').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/bulk-move`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ fileIds: ['f-1'], folderIds: undefined, targetFolderId: 'target-folder' });
    req.flush(null);
  });

  it('should call copyToShared', () => {
    service.copyToShared(['f-1'], ['folder-1'], 'target-shared-id').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/copy-to-shared`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      fileIds: ['f-1'],
      folderIds: ['folder-1'],
      targetSharedFolderId: 'target-shared-id'
    });
    req.flush({ message: 'Files copied to shared space successfully.' });
  });

  it('should call saveToPersonal', () => {
    service.saveToPersonal(['f-1'], undefined, 'target-personal-id').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/shared/save-to-personal`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      fileIds: ['f-1'],
      folderIds: [],
      targetFolderId: 'target-personal-id'
    });
    req.flush({ message: 'Files saved to personal space successfully.' });
  });


  it('should call getRecentFiles', () => {
    service.getRecentFiles(5).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/recent?limit=5`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('should call getSharedFolders', () => {
    service.getSharedFolders('folder-abc').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/shared/folders?folderId=folder-abc`);
    expect(req.request.method).toBe('GET');
    req.flush({ folders: [], files: [], breadcrumbs: [] });
  });

  it('should call checkConflicts', () => {
    service.checkConflicts(['test.txt'], 'folder-1', false).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/check-conflicts`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ fileNames: ['test.txt'], folderId: 'folder-1', isShared: false });
    req.flush(['test.txt']);
  });

  it('should call searchFiles with query, folderId, and scope', () => {
    service.searchFiles('report', 'f-1', 'current').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/search?q=report&scope=current&folderId=f-1`);
    expect(req.request.method).toBe('GET');
    req.flush({ folders: [], files: [], breadcrumbs: [] });
  });

  it('should call searchFiles with folderIds array', () => {
    service.searchFiles('test', null, 'custom', ['f-1', 'f-2']).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/files/search?q=test&scope=custom&folderIds=f-1&folderIds=f-2`);
    expect(req.request.method).toBe('GET');
    req.flush({ folders: [], files: [], breadcrumbs: [] });
  });

  it('should call getFolderTree', () => {
    service.getFolderTree().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/folders/tree`);
    expect(req.request.method).toBe('GET');
    req.flush({ personalFolders: [], sharedFolders: [] });
  });
});
