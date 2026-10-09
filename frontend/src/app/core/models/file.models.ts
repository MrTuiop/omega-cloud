export interface FolderItem {
  id: string;
  userId: string;
  parentFolderId?: string | null;
  name: string;
  color?: string | null;
  isDeleted: boolean;
  deletedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface FileItem {
  id: string;
  userId: string;
  folderId?: string | null;
  storageBlobId: string;
  name: string;
  extension: string;
  mimeType: string;
  sizeBytes: number;
  isDeleted: boolean;
  deletedAt?: string | null;
  isFavorite: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Breadcrumb {
  id?: string | null;
  name: string;
}

export interface UserQuota {
  allocatedBytes: number;
  usedBytes: number;
  availableBytes: number;
}

export interface FolderContent {
  currentFolder?: FolderItem | null;
  breadcrumbs: Breadcrumb[];
  folders: FolderItem[];
  files: FileItem[];
  quota: UserQuota;
}

export interface FileMetadata {
  id: string;
  userId: string;
  folderId?: string | null;
  folderName?: string | null;
  storageBlobId: string;
  sha256Hash: string;
  storageNodeId: string;
  storageNodeName: string;
  storageNodeType: string;
  name: string;
  extension: string;
  mimeType: string;
  sizeBytes: number;
  isFavorite: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateFolderRequest {
  name: string;
  parentFolderId?: string | null;
  color?: string | null;
}

export interface UpdateFolderRequest {
  name?: string;
  parentFolderId?: string | null;
  color?: string | null;
}

export interface UpdateFileRequest {
  name?: string;
  folderId?: string | null;
  isFavorite?: boolean;
}
