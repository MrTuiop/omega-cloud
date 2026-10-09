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
});
