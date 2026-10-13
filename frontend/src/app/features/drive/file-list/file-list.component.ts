import { Component, Input, Output, EventEmitter, signal, inject, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FolderItem, FileItem } from '../../../core/models/file.models';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { getFileTypeInfo } from '../../../shared/utils/file-icons';
import { DragStateService } from '../../../core/services/drag-state.service';

type SortColumn = 'name' | 'size' | 'updatedAt';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-file-list',
  standalone: true,
  imports: [CommonModule, FileSizePipe, RelativeTimePipe],
  template: `
    <div
      class="list-container"
      [class.list-container--dragging]="dragState.isDragging()"
      (contextmenu)="onContainerContextMenu($event)"
      (click)="onContainerClick($event)"
    >
      <div class="table-wrapper">
        <table class="glass-table">
          <thead>
            <tr>
              <th class="col-checkbox" (click)="$event.stopPropagation()">
                <input
                  type="checkbox"
                  class="glass-checkbox"
                  [checked]="isAllSelected()"
                  [indeterminate]="isPartiallySelected()"
                  (change)="toggleSelectAll($event)"
                  title="Select all"
                />
              </th>
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
                [attr.data-id]="folder.id"
                [attr.data-type]="'folder'"
                [class.table-row--selected]="isFolderSelected(folder.id)"
                [class.table-row--dragover]="dragOverFolderId === folder.id"
                [draggable]="true"
                (click)="onFolderClick($event, folder)"
                (dblclick)="openFolder.emit(folder)"
                (contextmenu)="onFolderContextMenu($event, folder)"
                (dragstart)="onFolderDragStart($event, folder)"
                (dragover)="onFolderDragOver($event, folder)"
                (dragleave)="onFolderDragLeave($event, folder)"
                (drop)="onFolderDrop($event, folder)"
              >
                <td class="col-checkbox" (click)="$event.stopPropagation()">
                  <input
                    type="checkbox"
                    class="glass-checkbox"
                    [checked]="isFolderSelected(folder.id)"
                    (change)="onRowCheckboxChange($event, 'folder', folder.id)"
                    title="Select folder"
                  />
                </td>
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
                [attr.data-id]="file.id"
                [attr.data-type]="'file'"
                [class.table-row--selected]="isFileSelected(file.id)"
                [draggable]="true"
                (click)="onFileClick($event, file)"
                (dblclick)="onFileDoubleClick(file)"
                (contextmenu)="onFileContextMenu($event, file)"
                (dragstart)="onFileDragStart($event, file)"
              >
                <td class="col-checkbox" (click)="$event.stopPropagation()">
                  <input
                    type="checkbox"
                    class="glass-checkbox"
                    [checked]="isFileSelected(file.id)"
                    (change)="onRowCheckboxChange($event, 'file', file.id)"
                    title="Select file"
                  />
                </td>
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

            <!-- Empty State -->
            @if (folders.length === 0 && files.length === 0) {
              <tr>
                <td colspan="7" class="empty-cell">
                  <div class="empty-state">
                    <div class="empty-icon">
                      <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                        <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                      </svg>
                    </div>
                    <span>This folder is empty</span>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .list-container {
      width: 100%;
      height: 100%;
      user-select: none;
      -webkit-user-select: none;

      &--dragging {
        .table-row * {
          pointer-events: none !important;
        }
      }
    }

    .table-wrapper {
      @include glass-surface;
      border-radius: var(--omega-radius-lg);
      overflow: hidden;
      width: 100%;
    }

    .glass-table {
      width: 100%;
      border-collapse: collapse;
      text-align: left;
      font-size: 13px;

      thead {
        background: var(--omega-glass-bg-sunken);
        border-bottom: 1px solid var(--omega-glass-border-subtle);

        th {
          padding: 10px 16px;
          font-weight: 600;
          color: var(--omega-text-muted);
          font-size: 11px;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          user-select: none;
          cursor: pointer;
          white-space: nowrap;

          &:hover {
            color: var(--omega-text-primary);
          }
        }
      }

      .th-content {
        display: flex;
        align-items: center;
        gap: 6px;
      }

      .sort-arrow {
        font-size: 9px;
        color: var(--omega-accent);
      }
    }

    .col-checkbox {
      width: 44px;
      padding: 10px 12px 10px 16px !important;
      text-align: center;
      vertical-align: middle;
      cursor: default !important;
    }

    .glass-checkbox {
      appearance: none;
      -webkit-appearance: none;
      width: 18px;
      height: 18px;
      border-radius: 4px;
      border: 1.5px solid var(--omega-glass-border, rgba(255, 255, 255, 0.4));
      background: rgba(255, 255, 255, 0.12);
      cursor: pointer;
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s ease;
      vertical-align: middle;

      &:hover {
        border-color: var(--omega-accent);
        background: rgba(0, 113, 227, 0.15);
      }

      &:checked {
        background: var(--omega-accent);
        border-color: var(--omega-accent);

        &::after {
          content: '';
          display: block;
          width: 5px;
          height: 9px;
          border: solid #ffffff;
          border-width: 0 2px 2px 0;
          transform: rotate(45deg);
          margin-bottom: 2px;
        }
      }

      &:indeterminate {
        background: var(--omega-accent);
        border-color: var(--omega-accent);

        &::after {
          content: '';
          display: block;
          width: 8px;
          height: 2px;
          background: #ffffff;
          border-radius: 1px;
        }
      }
    }

    .table-row {
      border-bottom: 1px solid var(--omega-glass-border-subtle);
      transition: background 0.15s ease, box-shadow 0.15s ease;
      cursor: pointer;

      &:last-child {
        border-bottom: none;
      }

      &:hover {
        background: var(--omega-glass-bg-hover);
      }

      &--selected {
        background: var(--omega-accent-subtle) !important;
        td.col-name {
          color: var(--omega-accent);
        }
      }

      &--dragover {
        background: var(--omega-accent-subtle) !important;
        box-shadow: inset 0 0 0 2px var(--omega-accent), 0 0 16px rgba(0, 113, 227, 0.4) !important;

        .row-title,
        .row-icon,
        .item-name,
        .file-mini-badge,
        .kind-tag,
        .col-size,
        .col-date {
          pointer-events: none !important;
        }
      }

      td {
        padding: 10px 16px;
        color: var(--omega-text-secondary);
        white-space: nowrap;
      }
    }

    .row-title {
      display: flex;
      align-items: center;
      gap: 12px;
      min-width: 0;
    }

    .row-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .file-mini-badge {
      padding: 2px 6px;
      border-radius: var(--omega-radius-xs, 4px);
      color: #ffffff;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      flex-shrink: 0;
    }

    .item-name {
      color: var(--omega-text-primary);
      font-weight: 500;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .kind-tag {
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .col-star {
      width: 36px;
      padding-right: 0 !important;
    }

    .col-actions {
      width: 44px;
      text-align: right;
    }

    .table-star-btn {
      background: none;
      border: none;
      color: var(--omega-text-muted);
      cursor: pointer;
      padding: 4px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s ease;

      &:hover {
        color: #ffcc00;
        transform: scale(1.15);
      }

      &--active {
        color: #ffcc00;
      }
    }

    .row-action-btn {
      background: none;
      border: none;
      color: var(--omega-text-muted);
      cursor: pointer;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 12px;
      letter-spacing: 1px;

      &:hover {
        background: var(--omega-glass-bg-sunken);
        color: var(--omega-text-primary);
      }
    }

    .empty-cell {
      padding: 48px 16px !important;
      text-align: center;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      color: var(--omega-text-muted);
      font-size: 14px;

      .empty-icon {
        color: var(--omega-text-secondary);
      }
    }
  `]
})
export class FileListComponent {
  @Input() folders: FolderItem[] = [];
  @Input() files: FileItem[] = [];
  @Input() selectedFolderId: string | null = null;
  @Input() selectedFileId: string | null = null;
  @Input() selectedFolderIds: string[] = [];
  @Input() selectedFileIds: string[] = [];

  @Output() selectFolder = new EventEmitter<FolderItem>();
  @Output() openFolder = new EventEmitter<FolderItem>();
  @Output() folderContextMenu = new EventEmitter<{ event: MouseEvent; folder: FolderItem }>();
  @Output() folderDrop = new EventEmitter<{ targetFolder: FolderItem; fileId?: string; fileIds?: string[]; folderIds?: string[] }>();

  @Output() selectFile = new EventEmitter<FileItem>();
  @Output() openFile = new EventEmitter<FileItem>();
  @Output() downloadFile = new EventEmitter<FileItem>();
  @Output() toggleFavorite = new EventEmitter<FileItem>();
  @Output() fileContextMenu = new EventEmitter<{ event: MouseEvent; file: FileItem }>();
  @Output() selectionChange = new EventEmitter<{ folderIds: string[]; fileIds: string[] }>();
  @Output() workspaceContextMenu = new EventEmitter<MouseEvent>();
  @Output() emptySpaceClick = new EventEmitter<MouseEvent>();

  readonly dragState = inject(DragStateService);

  readonly sortCol = signal<SortColumn>('name');
  readonly sortDir = signal<SortDirection>('asc');

  dragOverFolderId: string | null = null;
  isTouchDevice = false;

  private lastPivot: { type: 'folder' | 'file'; id: string } | null = null;

  isFolderSelected(id: string): boolean {
    return this.selectedFolderIds.includes(id) || this.selectedFolderId === id;
  }

  isFileSelected(id: string): boolean {
    return this.selectedFileIds.includes(id) || this.selectedFileId === id;
  }

  isAllSelected(): boolean {
    const total = this.folders.length + this.files.length;
    if (total === 0) return false;
    const selectedCount = this.folders.filter(f => this.isFolderSelected(f.id)).length +
      this.files.filter(f => this.isFileSelected(f.id)).length;
    return selectedCount === total;
  }

  isPartiallySelected(): boolean {
    const total = this.folders.length + this.files.length;
    if (total === 0) return false;
    const selectedCount = this.folders.filter(f => this.isFolderSelected(f.id)).length +
      this.files.filter(f => this.isFileSelected(f.id)).length;
    return selectedCount > 0 && selectedCount < total;
  }

  toggleSelectAll(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.selectedFolderIds = this.folders.map(f => f.id);
      this.selectedFileIds = this.files.map(f => f.id);
    } else {
      this.selectedFolderIds = [];
      this.selectedFileIds = [];
    }
    this.selectedFolderId = null;
    this.selectedFileId = null;
    this.selectionChange.emit({
      folderIds: this.selectedFolderIds,
      fileIds: this.selectedFileIds
    });
  }

  onRowCheckboxChange(event: Event, type: 'folder' | 'file', id: string): void {
    const checked = (event.target as HTMLInputElement).checked;
    let nextFolderIds = this.selectedFolderIds.length > 0
      ? [...this.selectedFolderIds]
      : (this.selectedFolderId ? [this.selectedFolderId] : []);
    let nextFileIds = this.selectedFileIds.length > 0
      ? [...this.selectedFileIds]
      : (this.selectedFileId ? [this.selectedFileId] : []);

    if (type === 'folder') {
      nextFolderIds = checked
        ? (nextFolderIds.includes(id) ? nextFolderIds : [...nextFolderIds, id])
        : nextFolderIds.filter(fId => fId !== id);
    } else {
      nextFileIds = checked
        ? (nextFileIds.includes(id) ? nextFileIds : [...nextFileIds, id])
        : nextFileIds.filter(fId => fId !== id);
    }

    this.selectedFolderIds = nextFolderIds;
    this.selectedFileIds = nextFileIds;
    this.selectedFolderId = nextFolderIds.length === 1 && nextFileIds.length === 0 ? nextFolderIds[0] : null;
    this.selectedFileId = nextFileIds.length === 1 && nextFolderIds.length === 0 ? nextFileIds[0] : null;

    this.selectionChange.emit({
      folderIds: nextFolderIds,
      fileIds: nextFileIds
    });
  }

  getFileBadge(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).badge;
  }

  getFileBadgeColor(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).color;
  }

  setSort(column: SortColumn): void {
    if (this.sortCol() === column) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortCol.set(column);
      this.sortDir.set('asc');
    }
  }

  get sortedFolders(): FolderItem[] {
    const col = this.sortCol();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...this.folders].sort((a, b) => {
      if (col === 'name') return a.name.localeCompare(b.name) * dir;
      if (col === 'updatedAt') return (new Date(a.updatedAt).getTime() - new Date(b.updatedAt).getTime()) * dir;
      return 0;
    });
  }

  get sortedFiles(): FileItem[] {
    const col = this.sortCol();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...this.files].sort((a, b) => {
      if (col === 'name') return a.name.localeCompare(b.name) * dir;
      if (col === 'size') return ((a.sizeBytes || 0) - (b.sizeBytes || 0)) * dir;
      if (col === 'updatedAt') return (new Date(a.updatedAt).getTime() - new Date(b.updatedAt).getTime()) * dir;
      return 0;
    });
  }

  onFolderClick(event: MouseEvent, folder: FolderItem): void {
    event.stopPropagation();
    this.handleItemSelection(event, 'folder', folder.id);
    this.selectFolder.emit(folder);
  }

  onFileClick(event: MouseEvent, file: FileItem): void {
    event.stopPropagation();
    this.handleItemSelection(event, 'file', file.id);
    this.selectFile.emit(file);
  }

  onFileDoubleClick(file: FileItem): void {
    this.openFile.emit(file);
  }

  private handleItemSelection(event: MouseEvent, type: 'folder' | 'file', id: string): void {
    const isCtrl = event.ctrlKey || event.metaKey;
    const isShift = event.shiftKey;

    let nextFolderIds = [...this.selectedFolderIds];
    let nextFileIds = [...this.selectedFileIds];

    if (isCtrl) {
      if (type === 'folder') {
        nextFolderIds = nextFolderIds.includes(id)
          ? nextFolderIds.filter(fId => fId !== id)
          : [...nextFolderIds, id];
      } else {
        nextFileIds = nextFileIds.includes(id)
          ? nextFileIds.filter(fId => fId !== id)
          : [...nextFileIds, id];
      }
      this.lastPivot = { type, id };
    } else if (isShift && this.lastPivot) {
      const allItems = this.getAllOrderedItems();
      const pivotIdx = allItems.findIndex(i => i.type === this.lastPivot!.type && i.id === this.lastPivot!.id);
      const currentIdx = allItems.findIndex(i => i.type === type && i.id === id);

      if (pivotIdx !== -1 && currentIdx !== -1) {
        const start = Math.min(pivotIdx, currentIdx);
        const end = Math.max(pivotIdx, currentIdx);
        const range = allItems.slice(start, end + 1);

        nextFolderIds = range.filter(i => i.type === 'folder').map(i => i.id);
        nextFileIds = range.filter(i => i.type === 'file').map(i => i.id);
      } else {
        nextFolderIds = type === 'folder' ? [id] : [];
        nextFileIds = type === 'file' ? [id] : [];
        this.lastPivot = { type, id };
      }
    } else {
      nextFolderIds = type === 'folder' ? [id] : [];
      nextFileIds = type === 'file' ? [id] : [];
      this.lastPivot = { type, id };
    }

    this.selectedFolderIds = nextFolderIds;
    this.selectedFileIds = nextFileIds;
    this.selectedFolderId = nextFolderIds.length === 1 && nextFileIds.length === 0 ? nextFolderIds[0] : null;
    this.selectedFileId = nextFileIds.length === 1 && nextFolderIds.length === 0 ? nextFileIds[0] : null;

    this.selectionChange.emit({
      folderIds: nextFolderIds,
      fileIds: nextFileIds
    });
  }

  private getAllOrderedItems(): { type: 'folder' | 'file'; id: string }[] {
    return [
      ...this.sortedFolders.map(f => ({ type: 'folder' as const, id: f.id })),
      ...this.sortedFiles.map(f => ({ type: 'file' as const, id: f.id }))
    ];
  }

  onContainerClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('.table-row, button, input')) {
      return;
    }
    this.selectedFolderIds = [];
    this.selectedFileIds = [];
    this.selectedFolderId = null;
    this.selectedFileId = null;
    this.selectionChange.emit({ folderIds: [], fileIds: [] });
    this.emptySpaceClick.emit(event);
  }

  onContainerContextMenu(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('.table-row, button, input')) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.workspaceContextMenu.emit(event);
  }

  onToggleFavorite(event: MouseEvent, file: FileItem): void {
    event.stopPropagation();
    this.toggleFavorite.emit(file);
  }

  onFolderContextMenu(event: MouseEvent, folder: FolderItem): void {
    event.preventDefault();
    event.stopPropagation();
    if (!this.isFolderSelected(folder.id)) {
      this.selectedFolderIds = [folder.id];
      this.selectedFileIds = [];
      this.selectionChange.emit({ folderIds: [folder.id], fileIds: [] });
    }
    this.folderContextMenu.emit({ event, folder });
  }

  onFileContextMenu(event: MouseEvent, file: FileItem): void {
    event.preventDefault();
    event.stopPropagation();
    if (!this.isFileSelected(file.id)) {
      this.selectedFolderIds = [];
      this.selectedFileIds = [file.id];
      this.selectionChange.emit({ folderIds: [], fileIds: [file.id] });
    }
    this.fileContextMenu.emit({ event, file });
  }

  @HostListener('document:dragend')
  onDocumentDragEnd(): void {
    this.dragState.endDrag();
    this.dragOverFolderId = null;
  }

  onFolderDragStart(event: DragEvent, folder: FolderItem): void {
    if (!this.isFolderSelected(folder.id)) {
      this.selectedFolderIds = [folder.id];
      this.selectedFileIds = [];
      this.selectionChange.emit({ folderIds: [folder.id], fileIds: [] });
    }
    this.dragState.startDrag({
      folderIds: this.selectedFolderIds,
      fileIds: this.selectedFileIds
    });
    if (event.dataTransfer) {
      event.dataTransfer.setData('application/json', JSON.stringify({
        folderIds: this.selectedFolderIds,
        fileIds: this.selectedFileIds,
        fileId: null
      }));
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  onFileDragStart(event: DragEvent, file: FileItem): void {
    if (!this.isFileSelected(file.id)) {
      this.selectedFileIds = [file.id];
      this.selectedFolderIds = [];
      this.selectionChange.emit({ folderIds: [], fileIds: [file.id] });
    }
    this.dragState.startDrag({
      folderIds: this.selectedFolderIds,
      fileIds: this.selectedFileIds,
      fileId: file.id
    });
    if (event.dataTransfer) {
      event.dataTransfer.setData('application/json', JSON.stringify({
        folderIds: this.selectedFolderIds,
        fileIds: this.selectedFileIds,
        fileId: file.id
      }));
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  onFolderDragOver(event: DragEvent, folder: FolderItem): void {
    event.preventDefault();
    event.stopPropagation();

    const dragData = this.dragState.getDragData();
    const currentFolderIds = dragData?.folderIds || this.selectedFolderIds;
    if (currentFolderIds.includes(folder.id)) {
      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'none';
      }
      return;
    }
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    this.dragOverFolderId = folder.id;
  }

  onFolderDragLeave(event: DragEvent, folder: FolderItem): void {
    const currentTarget = event.currentTarget as HTMLElement;
    const relatedTarget = event.relatedTarget as HTMLElement;
    if (currentTarget && relatedTarget && currentTarget.contains(relatedTarget)) {
      return;
    }
    if (this.dragOverFolderId === folder.id) {
      this.dragOverFolderId = null;
    }
  }

  onFolderDrop(event: DragEvent, targetFolder: FolderItem): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOverFolderId = null;

    const dragData = this.dragState.getDragData();
    let folderIds: string[] = dragData?.folderIds ? [...dragData.folderIds] : [];
    let fileIds: string[] = dragData?.fileIds ? [...dragData.fileIds] : [];
    let fileId: string | undefined = dragData?.fileId || undefined;

    if (event.dataTransfer && folderIds.length === 0 && fileIds.length === 0 && !fileId) {
      const dataStr = event.dataTransfer.getData('application/json');
      if (dataStr) {
        try {
          const data = JSON.parse(dataStr);
          folderIds = data.folderIds || [];
          fileIds = data.fileIds || (data.fileId ? [data.fileId] : []);
          fileId = data.fileId || (fileIds.length > 0 ? fileIds[0] : undefined);
        } catch {
          // Ignore
        }
      }
    }

    if (folderIds.includes(targetFolder.id)) {
      this.dragState.endDrag();
      return;
    }

    if (folderIds.length > 0 || fileIds.length > 0 || fileId) {
      this.folderDrop.emit({
        targetFolder,
        fileId: fileId || (fileIds.length > 0 ? fileIds[0] : undefined),
        fileIds: fileIds.length > 0 ? fileIds : (fileId ? [fileId] : []),
        folderIds
      });
    }

    this.dragState.endDrag();
  }
}
