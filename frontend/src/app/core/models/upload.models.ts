export interface UploadSession {
  sessionId: string;
  fileName: string;
  totalSizeBytes: number;
  chunkSizeBytes: number;
  totalChunks: number;
  expectedSha256?: string | null;
  uploadMode: string; // 'Direct' | 'Proxy'
  targetUrl?: string | null;
  status: string; // 'Created' | 'Uploading' | 'Assembling' | 'Completed' | 'Aborted' | 'Expired'
  targetFolderId?: string | null;
  uploadedChunks: number[];
  missingChunks: number[];
  progressPercentage: number;
  createdAt: string;
  expiresAt: string;
}

export interface InitUploadSessionRequest {
  fileName: string;
  totalSizeBytes?: number;
  size?: number;
  chunkSizeBytes?: number;
  totalChunks?: number;
  expectedSha256?: string;
  sha256?: string;
  folderId?: string;
  targetFolderId?: string;
  storageNodeId?: string;
  conflictResolution?: 'Replace' | 'Rename' | 'Skip';
  isShared?: boolean;
}

export interface CompleteUploadSessionRequest {
  expectedSha256?: string;
}

export interface UploadChunkResult {
  sessionId: string;
  chunkIndex: number;
  sizeBytes: number;
  sha256Hash: string;
  totalChunks: number;
  uploadedChunksCount: number;
  progressPercentage: number;
  isCompleted: boolean;
}

export type UploadTaskStatus =
  | 'queued'
  | 'hashing'
  | 'uploading'
  | 'assembling'
  | 'completed'
  | 'paused'
  | 'error'
  | 'cancelled';

export interface UploadTask {
  id: string; // Unique task tracking ID
  file: File;
  targetFolderId?: string | null;
  targetFolderName?: string;
  status: UploadTaskStatus;
  progress: number; // 0 - 100
  bytesUploaded: number;
  totalBytes: number;
  speedBytesPerSec: number;
  errorMessage?: string;
  sessionId?: string;
  currentChunkIndex: number;
  totalChunks: number;
  startTime: number;
  abortController?: AbortController;
  conflictResolution?: 'Replace' | 'Rename' | 'Skip';
  isShared?: boolean;
}
