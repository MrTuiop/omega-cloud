import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEvent, HttpParams, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  LoginRequest,
  LoginResponse,
  RefreshTokenRequest,
  RefreshTokenResponse,
  UserProfile,
  UserSession
} from '../models/auth.models';
import {
  FolderContent,
  FolderItem,
  FileItem,
  FileMetadata,
  CreateFolderRequest,
  UpdateFolderRequest,
  UpdateFileRequest,
  UserQuota
} from '../models/file.models';
import {
  InitUploadSessionRequest,
  UploadSession,
  UploadChunkResult,
  CompleteUploadSessionRequest
} from '../models/upload.models';
import { TrashContent, RestoreItemResult } from '../models/trash.models';
import {
  AdminUser,
  AdminUserDetail,
  CreateUserRequest,
  UpdateUserRequest,
  AdminResetPasswordRequest,
  StorageNode,
  UpdateStorageNodeRequest,
  GenerateNodeTokenRequest,
  NodeRegistrationToken,
  NodePingResult,
  AuditLog,
  SystemHealth
} from '../models/admin.models';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  // ================= Auth =================
  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.baseUrl}/auth/login`, request);
  }

  refreshToken(request?: RefreshTokenRequest): Observable<RefreshTokenResponse> {
    return this.http.post<RefreshTokenResponse>(`${this.baseUrl}/auth/refresh`, request ?? {});
  }

  logout(request?: RefreshTokenRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/auth/logout`, request ?? {});
  }

  getUserProfile(): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${this.baseUrl}/auth/me`);
  }

  getUserSessions(): Observable<UserSession[]> {
    return this.http.get<UserSession[]>(`${this.baseUrl}/auth/sessions`);
  }

  revokeSession(sessionId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.baseUrl}/auth/sessions/${sessionId}`);
  }

  // ================= Folders =================
  getRootFolderContent(): Observable<FolderContent> {
    return this.http.get<FolderContent>(`${this.baseUrl}/folders`);
  }

  getFolderContent(folderId: string): Observable<FolderContent> {
    return this.http.get<FolderContent>(`${this.baseUrl}/folders/${folderId}`);
  }

  createFolder(request: CreateFolderRequest): Observable<FolderItem> {
    return this.http.post<FolderItem>(`${this.baseUrl}/folders`, request);
  }

  updateFolder(folderId: string, request: UpdateFolderRequest): Observable<FolderItem> {
    return this.http.patch<FolderItem>(`${this.baseUrl}/folders/${folderId}`, request);
  }

  deleteFolder(folderId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/folders/${folderId}`);
  }

  // ================= Files =================
  getFileMetadata(fileId: string): Observable<FileMetadata> {
    return this.http.get<FileMetadata>(`${this.baseUrl}/files/${fileId}/metadata`);
  }

  updateFile(fileId: string, request: UpdateFileRequest): Observable<FileItem> {
    return this.http.patch<FileItem>(`${this.baseUrl}/files/${fileId}`, request);
  }

  deleteFile(fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/files/${fileId}`);
  }

  downloadFileBlob(fileId: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/files/${fileId}/download`, {
      responseType: 'blob'
    });
  }

  getDownloadUrl(fileId: string): string {
    return `${this.baseUrl}/files/${fileId}/download`;
  }

  // ================= Direct Stream Upload (< 50MB) =================
  directStreamUpload(file: File, folderId?: string | null): Observable<HttpEvent<FileItem>> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    if (folderId) {
      formData.append('folderId', folderId);
    }

    const req = new HttpRequest('POST', `${this.baseUrl}/upload/direct-stream`, formData, {
      reportProgress: true
    });
    return this.http.request<FileItem>(req);
  }

  // ================= Resumable Chunk Sessions (> 50MB) =================
  initUploadSession(request: InitUploadSessionRequest): Observable<UploadSession> {
    return this.http.post<UploadSession>(`${this.baseUrl}/upload/sessions/init`, request);
  }

  uploadChunk(
    sessionId: string,
    chunkIndex: number,
    chunkBlob: Blob,
    sha256?: string
  ): Observable<UploadChunkResult> {
    const formData = new FormData();
    formData.append('chunk', chunkBlob);
    if (sha256) {
      formData.append('sha256', sha256);
    }

    return this.http.put<UploadChunkResult>(
      `${this.baseUrl}/upload/sessions/${sessionId}/chunks/${chunkIndex}`,
      formData
    );
  }

  completeUploadSession(
    sessionId: string,
    request?: CompleteUploadSessionRequest
  ): Observable<FileItem> {
    return this.http.post<FileItem>(
      `${this.baseUrl}/upload/sessions/${sessionId}/complete`,
      request ?? {}
    );
  }

  getUploadSession(sessionId: string, chunkIndex?: number): Observable<UploadSession> {
    let params = new HttpParams();
    if (chunkIndex !== undefined) {
      params = params.set('chunkIndex', chunkIndex.toString());
    }
    return this.http.get<UploadSession>(`${this.baseUrl}/upload/sessions/${sessionId}`, { params });
  }

  abortUploadSession(sessionId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/upload/sessions/${sessionId}`);
  }

  getUserQuota(): Observable<UserQuota> {
    return this.http.get<UserQuota>(`${this.baseUrl}/upload/quota`);
  }

  // ================= Trash =================
  getTrash(topLevelOnly: boolean = false): Observable<TrashContent> {
    const params = new HttpParams().set('topLevelOnly', topLevelOnly.toString());
    return this.http.get<TrashContent>(`${this.baseUrl}/trash`, { params });
  }

  restoreTrashItem(id: string): Observable<RestoreItemResult> {
    return this.http.post<RestoreItemResult>(`${this.baseUrl}/trash/restore/${id}`, {});
  }

  permanentlyDeleteTrashItem(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/trash/permanent/${id}`);
  }

  emptyTrash(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/trash/empty`);
  }

  // ================= Admin Users =================
  getAdminUsers(): Observable<AdminUser[]> {
    return this.http.get<AdminUser[]>(`${this.baseUrl}/admin/users`);
  }

  getAdminUser(id: string): Observable<AdminUserDetail> {
    return this.http.get<AdminUserDetail>(`${this.baseUrl}/admin/users/${id}`);
  }

  createAdminUser(request: CreateUserRequest): Observable<AdminUser> {
    return this.http.post<AdminUser>(`${this.baseUrl}/admin/users`, request);
  }

  updateAdminUser(id: string, request: UpdateUserRequest): Observable<AdminUser> {
    return this.http.patch<AdminUser>(`${this.baseUrl}/admin/users/${id}`, request);
  }

  resetAdminUserPassword(id: string, request: AdminResetPasswordRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/admin/users/${id}/reset-password`, request);
  }

  revokeAdminUserSessions(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.baseUrl}/admin/users/${id}/sessions`);
  }

  revokeAdminUserSession(userId: string, sessionId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.baseUrl}/admin/users/${userId}/sessions/${sessionId}`);
  }

  deleteAdminUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/admin/users/${id}`);
  }

  // ================= Admin Nodes =================
  getAdminNodes(): Observable<StorageNode[]> {
    return this.http.get<StorageNode[]>(`${this.baseUrl}/admin/nodes`);
  }

  getAdminNode(id: string): Observable<StorageNode> {
    return this.http.get<StorageNode>(`${this.baseUrl}/admin/nodes/${id}`);
  }

  generateNodeToken(request?: GenerateNodeTokenRequest): Observable<NodeRegistrationToken> {
    return this.http.post<NodeRegistrationToken>(`${this.baseUrl}/admin/nodes/generate-token`, request ?? {});
  }

  deleteAdminNode(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/admin/nodes/${id}`);
  }

  updateAdminNode(id: string, request: UpdateStorageNodeRequest): Observable<StorageNode> {
    return this.http.patch<StorageNode>(`${this.baseUrl}/admin/nodes/${id}`, request);
  }

  pingAdminNode(id: string): Observable<NodePingResult> {
    return this.http.post<NodePingResult>(`${this.baseUrl}/admin/nodes/${id}/ping`, {});
  }

  // ================= Admin Audit =================
  getAuditLogs(params?: { userId?: string; action?: string; search?: string; limit?: number; offset?: number }): Observable<AuditLog[]> {
    let httpParams = new HttpParams();
    if (params?.userId) httpParams = httpParams.set('userId', params.userId);
    if (params?.action) httpParams = httpParams.set('action', params.action);
    if (params?.search) httpParams = httpParams.set('search', params.search);
    if (params?.limit !== undefined) httpParams = httpParams.set('limit', params.limit.toString());
    if (params?.offset !== undefined) httpParams = httpParams.set('offset', params.offset.toString());
    return this.http.get<AuditLog[]>(`${this.baseUrl}/admin/audit`, { params: httpParams });
  }

  // ================= Admin System Health =================
  getSystemHealth(): Observable<SystemHealth> {
    return this.http.get<SystemHealth>(`${this.baseUrl}/admin/health`);
  }
}
