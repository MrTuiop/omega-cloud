import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FolderItem, FileItem } from '../../../core/models/file.models';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { getFileTypeInfo } from '../../../shared/utils/file-icons';

@Component({
  selector: 'app-file-grid',
  standalone: true,
  imports: [CommonModule, FileSizePipe, RelativeTimePipe],
  template: `
    <div class="grid-container">
      <!-- Folders Section -->
      @if (folders.length > 0) {
        <div class="section-title">Folders</div>
        <div class="folders-grid">
          @for (folder of folders; track folder.id) {
            <div
              class="folder-card"
              [class.folder-card--selected]="selectedFolderId === folder.id"
              [class.folder-card--dragover]="dragOverFolderId === folder.id"
              (click)="onSelectFolder(folder)"
              (dblclick)="openFolder.emit(folder)"
              (contextmenu)="onFolderContextMenu($event, folder)"
              (dragover)="onFolderDragOver($event, folder)"
              (dragleave)="onFolderDragLeave(folder)"
              (drop)="onFolderDrop($event, folder)"
            >
              <div class="folder-icon" [style.color]="folder.color || 'var(--omega-accent)'">
                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor">
                  <path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/>
                </svg>
              </div>
              <div class="folder-info">
                <span class="folder-name" [title]="folder.name">{{ folder.name }}</span>
                <span class="folder-date">{{ folder.updatedAt | relativeTime }}</span>
              </div>
            </div>
          }
        </div>
      }

      <!-- Files Section -->
      @if (files.length > 0) {
        <div class="section-title" [class.section-title--spaced]="folders.length > 0">Files</div>
        <div class="files-grid">
          @for (file of files; track file.id) {
            <div
              class="file-card"
              [class.file-card--selected]="selectedFileId === file.id"
              (click)="onSelectFile(file)"
              (dblclick)="downloadFile.emit(file)"
              (contextmenu)="onFileContextMenu($event, file)"
              draggable="true"
              (dragstart)="onFileDragStart($event, file)"
            >
              <button
                class="star-btn"
                [class.star-btn--active]="file.isFavorite"
                (click)="onToggleFavorite($event, file)"
                title="Toggle Favorite"
              >
                <svg width="15" height="15" viewBox="0 0 24 24" [attr.fill]="file.isFavorite ? 'currentColor' : 'none'" stroke="currentColor" stroke-width="2">
                  <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
                </svg>
              </button>

              <div class="file-preview">
                <div class="file-badge" [style.background-color]="getFileBadgeColor(file)">
                  {{ getFileBadge(file) }}
                </div>
              </div>

              <div class="file-footer">
                <span class="file-name" [title]="file.name">{{ file.name }}</span>
                <div class="file-meta">
                  <span>{{ file.sizeBytes | fileSize }}</span>
                  <span>•</span>
                  <span>{{ file.updatedAt | relativeTime }}</span>
                </div>
              </div>
            </div>
          }
        </div>
      }

      <!-- Empty State -->
      @if (folders.length === 0 && files.length === 0) {
        <div class="empty-state">
          <div class="empty-icon">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
            </svg>
          </div>
          <h3>This folder is empty</h3>
          <p>Drag and drop files here to upload, or use the Upload button above.</p>
        </div>
      }
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .grid-container {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .section-title {
      font-size: 13px;
      font-weight: 600;
      color: var(--omega-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;

      &--spaced {
        margin-top: 12px;
      }
    }

    .folders-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
      gap: 12px;
    }

    .folder-card {
      @include glass-surface;
      border-radius: var(--omega-radius-md);
      padding: 12px 14px;
      display: flex;
      align-items: center;
      gap: 12px;
      cursor: pointer;
      transition: all 0.2s var(--omega-ease-spring);
      position: relative;

      &:hover {
        background: var(--omega-glass-bg-hover);
        transform: translateY(-2px);
        box-shadow: var(--omega-shadow-md), var(--omega-glass-highlight);
      }

      &--selected {
        border-color: var(--omega-accent);
        box-shadow: 0 0 0 2px var(--omega-accent), var(--omega-shadow-sm);
        background: var(--omega-accent-subtle);
      }

      &--dragover {
        border-color: var(--omega-accent);
        background: var(--omega-accent-subtle);
        transform: scale(1.02);
      }
    }

    .folder-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .folder-info {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .folder-name {
      font-size: 14px;
      font-weight: 500;
      color: var(--omega-text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .folder-date {
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .files-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 14px;
    }

    .file-card {
      @include glass-surface;
      border-radius: var(--omega-radius-lg);
      padding: 14px;
      display: flex;
      flex-direction: column;
      height: 170px;
      cursor: pointer;
      position: relative;
      transition: all 0.25s var(--omega-ease-spring);

      &:hover {
        background: var(--omega-glass-bg-hover);
        transform: translateY(-3px);
        box-shadow: var(--omega-shadow-lg), var(--omega-glass-highlight);

        .star-btn {
          opacity: 1;
        }
      }

      &--selected {
        border-color: var(--omega-accent);
        box-shadow: 0 0 0 2px var(--omega-accent), var(--omega-shadow-sm);
        background: var(--omega-accent-subtle);
      }
    }

    .star-btn {
      position: absolute;
      top: 10px;
      right: 10px;
      width: 26px;
      height: 26px;
      border-radius: 50%;
      background: var(--omega-glass-bg-sunken);
      color: var(--omega-text-muted);
      display: flex;
      align-items: center;
      justify-content: center;
      opacity: 0;
      transition: all 0.15s ease;
      z-index: 2;

      &--active {
        opacity: 1;
        color: #ffcc00;
      }

      &:hover {
        transform: scale(1.15);
        color: #ffcc00;
      }
    }

    .file-preview {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .file-badge {
      padding: 6px 14px;
      border-radius: var(--omega-radius-md);
      color: #ffffff;
      font-size: 13px;
      font-weight: 700;
      letter-spacing: 0.05em;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      text-transform: uppercase;
    }

    .file-footer {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .file-name {
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .file-meta {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .empty-state {
      padding: 60px 20px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      text-align: center;
      color: var(--omega-text-muted);

      .empty-icon {
        width: 72px;
        height: 72px;
        border-radius: 50%;
        @include glass-sunken;
        display: flex;
        align-items: center;
        justify-content: center;
        margin-bottom: 16px;
        color: var(--omega-text-secondary);
      }

      h3 {
        font-size: 16px;
        font-weight: 600;
        color: var(--omega-text-primary);
        margin-bottom: 6px;
      }

      p {
        font-size: 13px;
        max-width: 320px;
      }
    }
  `]
})
export class FileGridComponent {
  @Input() folders: FolderItem[] = [];
  @Input() files: FileItem[] = [];
  @Input() selectedFolderId: string | null = null;
  @Input() selectedFileId: string | null = null;

  @Output() selectFolder = new EventEmitter<FolderItem>();
  @Output() openFolder = new EventEmitter<FolderItem>();
  @Output() folderContextMenu = new EventEmitter<{ event: MouseEvent; folder: FolderItem }>();
  @Output() folderDrop = new EventEmitter<{ targetFolder: FolderItem; fileId: string }>();

  @Output() selectFile = new EventEmitter<FileItem>();
  @Output() downloadFile = new EventEmitter<FileItem>();
  @Output() toggleFavorite = new EventEmitter<FileItem>();
  @Output() fileContextMenu = new EventEmitter<{ event: MouseEvent; file: FileItem }>();

  dragOverFolderId: string | null = null;

  getFileBadge(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).badge;
  }

  getFileBadgeColor(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).color;
  }

  onSelectFolder(folder: FolderItem): void {
    this.selectFolder.emit(folder);
  }

  onSelectFile(file: FileItem): void {
    this.selectFile.emit(file);
  }

  onToggleFavorite(event: MouseEvent, file: FileItem): void {
    event.stopPropagation();
    this.toggleFavorite.emit(file);
  }

  onFolderContextMenu(event: MouseEvent, folder: FolderItem): void {
    event.preventDefault();
    event.stopPropagation();
    this.folderContextMenu.emit({ event, folder });
  }

  onFileContextMenu(event: MouseEvent, file: FileItem): void {
    event.preventDefault();
    event.stopPropagation();
    this.fileContextMenu.emit({ event, file });
  }

  onFileDragStart(event: DragEvent, file: FileItem): void {
    if (event.dataTransfer) {
      event.dataTransfer.setData('application/json', JSON.stringify({ fileId: file.id }));
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  onFolderDragOver(event: DragEvent, folder: FolderItem): void {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    this.dragOverFolderId = folder.id;
  }

  onFolderDragLeave(folder: FolderItem): void {
    if (this.dragOverFolderId === folder.id) {
      this.dragOverFolderId = null;
    }
  }

  onFolderDrop(event: DragEvent, targetFolder: FolderItem): void {
    event.preventDefault();
    this.dragOverFolderId = null;
    if (event.dataTransfer) {
      const dataStr = event.dataTransfer.getData('application/json');
      if (dataStr) {
        try {
          const data = JSON.parse(dataStr);
          if (data.fileId) {
            this.folderDrop.emit({ targetFolder, fileId: data.fileId });
          }
        } catch {
          // Ignore
        }
      }
    }
  }
}
