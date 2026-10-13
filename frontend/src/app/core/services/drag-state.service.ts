import { Injectable, signal } from '@angular/core';

export interface DragData {
  fileIds?: string[];
  folderIds?: string[];
  fileId?: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class DragStateService {
  readonly isDragging = signal<boolean>(false);
  readonly dragData = signal<DragData | null>(null);

  startDrag(data: DragData): void {
    const fileIds = (data.fileIds && data.fileIds.length > 0)
      ? [...data.fileIds]
      : (data.fileId ? [data.fileId] : []);
    const folderIds = data.folderIds ? [...data.folderIds] : [];
    const fileId = data.fileId || (fileIds.length > 0 ? fileIds[0] : null);

    this.dragData.set({
      fileIds,
      folderIds,
      fileId
    });
    this.isDragging.set(true);
  }

  endDrag(): void {
    this.isDragging.set(false);
    this.dragData.set(null);
  }

  getDragData(): DragData | null {
    return this.dragData();
  }
}
