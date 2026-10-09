import { UserQuota } from './file.models';

export interface TrashFolderItem {
  id: string;
  userId: string;
  parentFolderId?: string | null;
  name: string;
  color?: string | null;
  deletedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface TrashFileItem {
  id: string;
  userId: string;
  folderId?: string | null;
  folderName?: string | null;
  storageBlobId: string;
  name: string;
  extension: string;
  mimeType: string;
  sizeBytes: number;
  isFavorite: boolean;
  deletedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface TrashContent {
  folders: TrashFolderItem[];
  files: TrashFileItem[];
  totalTrashSizeBytes: number;
  totalItemCount: number;
  quota: UserQuota;
}

export interface RestoreItemResult {
  id: string;
  itemType: string;
  name: string;
  restoredToFolderId?: string | null;
  message: string;
}
