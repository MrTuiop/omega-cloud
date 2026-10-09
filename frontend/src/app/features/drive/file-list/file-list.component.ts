import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FolderItem, FileItem } from '../../../core/models/file.models';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { getFileTypeInfo } from '../../../shared/utils/file-icons';

type SortColumn = 'name' | 'size' | 'updatedAt';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-file-list',
  standalone: true,
  imports: [CommonModule, FileSizePipe, RelativeTimePipe],
  template: `
    <div class="list-container">
      <div class="table-wrapper">
        <table class="glass-table">
          <thead>
            <tr>
              <th class="col-name" (click)="setSort('name')">
                <div class="th-content">
                  <span>Name</span>
                  @if (sortCol() === 'name') {
                    <span class="sort-arrow">{{ sortDir() === 'asc' ? '▲' : '▼' }}</span>
                  }
                </div>
              </th>
              <th class="col-kind">Kind</th>
              <th class="col-size" (click)="setSort('size')">
                <div class="th-content">
                  <span>Size</span>
                  @if (sortCol() === 'size') {
                    <span class="sort-arrow">{{ sortDir() === 'asc' ? '▲' : '▼' }}</span>
                  }
                </div>
              </th>
              <th class="col-date" (click)="setSort('updatedAt')">
                <div class="th-content">
                  <span>Modified</span>
                  @if (sortCol() === 'updatedAt') {
                    <span class="sort-arrow">{{ sortDir() === 'asc' ? '▲' : '▼' }}</span>
                  }
                </div>
              </th>
              <th class="col-star"></th>
              <th class="col-actions"></th>
            </tr>
          </thead>
          <tbody>
            <!-- Folders -->
            @for (folder of sortedFolders; track folder.id) {
              <tr
                class="table-row folder-row"
                [class.table-row--selected]="selectedFolderId === folder.id"
                [class.table-row--dragover]="dragOverFolderId === folder.id"
                (click)="selectFolder.emit(folder)"
                (dblclick)="openFolder.emit(folder)"
                (contextmenu)="onFolderContextMenu($event, folder)"
                (dragover)="onFolderDragOver($event, folder)"
                (dragleave)="onFolderDragLeave(folder)"
                (drop)="onFolderDrop($event, folder)"
              >
                <td class="col-name">
                  <div class="row-title">
                    <span class="row-icon folder-icon" [style.color]="folder.color || 'var(--omega-accent)'">
                      <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
                        <path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/>
                      </svg>
                    </span>
                    <span class="item-name" [title]="folder.name">{{ folder.name }}</span>
                  </div>
                </td>
                <td class="col-kind"><span class="kind-tag">Folder</span></td>
                <td class="col-size">—</td>
                <td class="col-date">{{ folder.updatedAt | relativeTime }}</td>
                <td class="col-star"></td>
                <td class="col-actions">
                  <button class="row-action-btn" (click)="onFolderContextMenu($event, folder)" title="More options">
                    •••
                  </button>
                </td>
              </tr>
            }

            <!-- Files -->
            @for (file of sortedFiles; track file.id) {
              <tr
                class="table-row file-row"
                [class.table-row--selected]="selectedFileId === file.id"
                (click)="selectFile.emit(file)"
                (dblclick)="downloadFile.emit(file)"
                (contextmenu)="onFileContextMenu($event, file)"
                draggable="true"
                (dragstart)="onFileDragStart($event, file)"
              >
                <td class="col-name">
                  <div class="row-title">
                    <span class="file-mini-badge" [style.background-color]="getFileBadgeColor(file)">
                      {{ getFileBadge(file) }}
                    </span>
                    <span class="item-name" [title]="file.name">{{ file.name }}</span>
                  </div>
                </td>
                <td class="col-kind">
                  <span class="kind-tag">{{ file.extension ? file.extension.toUpperCase() : 'File' }}</span>
                </td>
                <td class="col-size">{{ file.sizeBytes | fileSize }}</td>
                <td class="col-date">{{ file.updatedAt | relativeTime }}</td>
                <td class="col-star">
                  <button
                    class="table-star-btn"
                    [class.table-star-btn--active]="file.isFavorite"
                    (click)="onToggleFavorite($event, file)"
                    title="Toggle Favorite"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" [attr.fill]="file.isFavorite ? 'currentColor' : 'none'" stroke="currentColor" stroke-width="2">
                      <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
                    </svg>
                  </button>
                </td>
                <td class="col-actions">
                  <button class="row-action-btn" (click)="onFileContextMenu($event, file)" title="More options">
                    •••
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>

        @if (folders.length === 0 && files.length === 0) {
          <div class="empty-list">No items in this folder</div>
        }
      </div>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .list-container {
      width: 100%;
    }

    .table-wrapper {
      @include glass-surface;
      border-radius: var(--omega-radius-lg);
      overflow: hidden;
    }

    .glass-table {
      width: 100%;
      border-collapse: collapse;
      text-align: left;
      font-size: 13px;

      th {
        padding: 12px 16px;
        font-weight: 600;
        font-size: 12px;
        color: var(--omega-text-muted);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        border-bottom: 1px solid var(--omega-glass-border);
        background: var(--omega-glass-bg-sunken);
        user-select: none;
      }

      td {
        padding: 11px 16px;
        border-bottom: 1px solid var(--omega-glass-border-subtle);
        color: var(--omega-text-primary);
        vertical-align: middle;
      }
    }

    .th-content {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      cursor: pointer;

      &:hover {
        color: var(--omega-text-primary);
      }
    }

    .sort-arrow {
      font-size: 10px;
      color: var(--omega-accent);
    }

    .table-row {
      cursor: pointer;
      transition: background 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
      }

      &--selected {
        background: var(--omega-accent-subtle) !important;
      }

      &--dragover {
        background: var(--omega-accent-subtle) !important;
        outline: 2px dashed var(--omega-accent);
      }
    }

    .row-title {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .item-name {
      font-weight: 500;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 320px;
    }

    .file-mini-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 2px 6px;
      border-radius: 4px;
      color: #ffffff;
      font-size: 10px;
      font-weight: 700;
      text-transform: uppercase;
      flex-shrink: 0;
    }

    .kind-tag {
      font-size: 11px;
      color: var(--omega-text-secondary);
    }

    .table-star-btn {
      color: var(--omega-text-muted);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 4px;
      border-radius: 4px;
      transition: all 0.15s;

      &--active {
        color: #ffcc00;
      }

      &:hover {
        color: #ffcc00;
      }
    }

    .row-action-btn {
      color: var(--omega-text-muted);
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 12px;
      letter-spacing: 1px;

      &:hover {
        background: var(--omega-glass-bg-sunken);
        color: var(--omega-text-primary);
      }
    }

    .col-name { width: 45%; }
    .col-kind { width: 12%; }
    .col-size { width: 12%; }
    .col-date { width: 18%; }
    .col-star { width: 5%; text-align: center; }
    .col-actions { width: 8%; text-align: right; }

    .empty-list {
      padding: 48px;
      text-align: center;
      color: var(--omega-text-muted);
      font-size: 14px;
    }
  `]
})
export class FileListComponent {
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

  sortCol = signal<SortColumn>('name');
  sortDir = signal<SortDirection>('asc');
  dragOverFolderId: string | null = null;

  get sortedFolders(): FolderItem[] {
    const list = [...this.folders];
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return list.sort((a, b) => {
      if (this.sortCol() === 'name') return (a.name || '').localeCompare(b.name || '') * dir;
      if (this.sortCol() === 'updatedAt') return (new Date(a.updatedAt || 0).getTime() - new Date(b.updatedAt || 0).getTime()) * dir;
      return 0;
    });
  }

  get sortedFiles(): FileItem[] {
    const list = [...this.files];
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return list.sort((a, b) => {
      if (this.sortCol() === 'name') return (a.name || '').localeCompare(b.name || '') * dir;
      if (this.sortCol() === 'size') return ((a.sizeBytes || 0) - (b.sizeBytes || 0)) * dir;
      if (this.sortCol() === 'updatedAt') return (new Date(a.updatedAt || 0).getTime() - new Date(b.updatedAt || 0).getTime()) * dir;
      return 0;
    });
  }

  setSort(col: SortColumn): void {
    if (this.sortCol() === col) {
      this.sortDir.update(d => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortCol.set(col);
      this.sortDir.set('asc');
    }
  }

  getFileBadge(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).badge;
  }

  getFileBadgeColor(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).color;
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
