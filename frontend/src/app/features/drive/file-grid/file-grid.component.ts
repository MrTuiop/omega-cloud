import { Component, Input, Output, EventEmitter, ElementRef, ViewChild, HostListener, inject, NgZone, ChangeDetectorRef, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FolderItem, FileItem } from '../../../core/models/file.models';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { getFileTypeInfo } from '../../../shared/utils/file-icons';
import { DragStateService } from '../../../core/services/drag-state.service';

@Component({
  selector: 'app-file-grid',
  standalone: true,
  imports: [CommonModule, FileSizePipe, RelativeTimePipe],
  template: `
    <div
      #gridContainer
      class="grid-container"
      [class.grid-container--dragging]="dragState.isDragging()"
      (mousedown)="onContainerMouseDown($event)"
      (contextmenu)="onContainerContextMenu($event)"
    >
      <!-- Marquee Selection Rectangle -->
      @if (isMarqueeActive && (marqueeBox.width > 3 || marqueeBox.height > 3)) {
        <div
          class="marquee-box"
          [style.left.px]="marqueeBox.left"
          [style.top.px]="marqueeBox.top"
          [style.width.px]="marqueeBox.width"
          [style.height.px]="marqueeBox.height"
        ></div>
      }

      <!-- Folders Section -->
      @if (folders.length > 0) {
        <div class="section-title">Folders</div>
        <div class="folders-grid">
          @for (folder of folders; track folder.id) {
            <div
              class="folder-card"
              [attr.data-id]="folder.id"
              [attr.data-type]="'folder'"
              [class.folder-card--selected]="isFolderSelected(folder.id)"
              [class.folder-card--dragover]="dragOverFolderId === folder.id"
              [draggable]="!isTouchDevice"
              (click)="onFolderClick($event, folder)"
              (dblclick)="openFolder.emit(folder)"
              (contextmenu)="onFolderContextMenu($event, folder)"
              (dragstart)="onFolderDragStart($event, folder)"
              (dragover)="onFolderDragOver($event, folder)"
              (dragleave)="onFolderDragLeave($event, folder)"
              (drop)="onFolderDrop($event, folder)"
            >
              <!-- Checkbox for multi-selection -->
              <div
                class="item-checkbox"
                [class.item-checkbox--checked]="isFolderSelected(folder.id)"
                (click)="onCheckboxClick($event, 'folder', folder.id)"
                title="Select folder"
              >
                @if (isFolderSelected(folder.id)) {
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                    <polyline points="20 6 9 17 4 12"></polyline>
                  </svg>
                }
              </div>

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
              [attr.data-id]="file.id"
              [attr.data-type]="'file'"
              [class.file-card--selected]="isFileSelected(file.id)"
              [draggable]="!isTouchDevice"
              (click)="onFileClick($event, file)"
              (dblclick)="onFileDoubleClick(file)"
              (contextmenu)="onFileContextMenu($event, file)"
              (dragstart)="onFileDragStart($event, file)"
            >
              <!-- Checkbox for multi-selection -->
              <div
                class="item-checkbox"
                [class.item-checkbox--checked]="isFileSelected(file.id)"
                (click)="onCheckboxClick($event, 'file', file.id)"
                title="Select file"
              >
                @if (isFileSelected(file.id)) {
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                    <polyline points="20 6 9 17 4 12"></polyline>
                  </svg>
                }
              </div>

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
      position: relative;
      user-select: none;
      -webkit-user-select: none;
      min-height: 100%;

      &--dragging {
        .folder-card *,
        .file-card * {
          pointer-events: none !important;
        }
      }
    }

    .marquee-box {
      position: absolute;
      border: 1px solid var(--omega-accent);
      background: rgba(0, 113, 227, 0.16);
      border-radius: 4px;
      pointer-events: none;
      z-index: 50;
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
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

    .item-checkbox {
      position: absolute;
      top: 8px;
      left: 8px;
      width: 20px;
      height: 20px;
      border-radius: 50%;
      border: 1.5px solid var(--omega-glass-border, rgba(255, 255, 255, 0.4));
      background: rgba(255, 255, 255, 0.15);
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      z-index: 10;
      opacity: 0;
      transition: all 0.2s var(--omega-ease-smooth);
      pointer-events: auto;

      &:hover {
        opacity: 1 !important;
        transform: scale(1.1);
        border-color: var(--omega-accent);
      }

      &--checked {
        opacity: 1 !important;
        background: var(--omega-accent);
        border-color: var(--omega-accent);
        color: #ffffff;
      }
    }

    .folder-card {
      @include glass-surface;
      content-visibility: auto;
      contain-intrinsic-size: auto 54px;
      border-radius: var(--omega-radius-md);
      padding: 12px 14px 12px 34px;
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

        .item-checkbox {
          opacity: 0.6;
        }
      }

      &--selected {
        border-color: var(--omega-accent) !important;
        box-shadow: 0 0 0 2px var(--omega-accent), var(--omega-shadow-sm) !important;
        background: var(--omega-accent-subtle) !important;

        .item-checkbox {
          opacity: 1 !important;
        }
      }

      &--dragover {
        border-color: var(--omega-accent) !important;
        background: var(--omega-accent-subtle) !important;
        transform: scale(1.03) !important;
        box-shadow: 0 0 24px rgba(0, 113, 227, 0.55), 0 0 0 2px var(--omega-accent) !important;

        .folder-icon,
        .folder-info {
          pointer-events: none !important;
        }
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
      font-size: 12px;
      color: var(--omega-text-muted);
    }

    .files-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 16px;
    }

    .file-card {
      @include glass-surface;
      content-visibility: auto;
      contain-intrinsic-size: auto 120px;
      border-radius: var(--omega-radius-lg);
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 12px;
      cursor: pointer;
      transition: all 0.2s var(--omega-ease-spring);
      position: relative;
      background: var(--omega-glass-bg);

      &:hover {
        background: var(--omega-glass-bg-hover);
        transform: translateY(-2px);
        box-shadow: var(--omega-shadow-md), var(--omega-glass-highlight);

        .star-btn {
          opacity: 1;
        }

        .item-checkbox {
          opacity: 0.6;
        }
      }

      &--selected {
        border-color: var(--omega-accent) !important;
        box-shadow: 0 0 0 2px var(--omega-accent), var(--omega-shadow-sm) !important;
        background: var(--omega-accent-subtle) !important;

        .item-checkbox {
          opacity: 1 !important;
        }
      }
    }

    .star-btn {
      position: absolute;
      top: 8px;
      right: 8px;
      background: transparent;
      border: none;
      color: var(--omega-text-muted);
      cursor: pointer;
      padding: 4px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      opacity: 0;
      transition: all 0.2s;
      z-index: 10;
      pointer-events: auto;

      &:hover {
        color: #ffcc00;
        transform: scale(1.15);
      }

      &--active {
        opacity: 1;
        color: #ffcc00;
      }
    }

    .file-preview {
      height: 110px;
      border-radius: var(--omega-radius-md);
      background: rgba(0, 0, 0, 0.03);
      display: flex;
      align-items: center;
      justify-content: center;
      position: relative;
      overflow: hidden;
      border: 1px solid var(--omega-glass-border-subtle);
    }

    .file-badge {
      padding: 6px 12px;
      border-radius: 6px;
      color: #ffffff;
      font-size: 11px;
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

    @media (max-width: 768px), (pointer: coarse) {
      .folders-grid {
        grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
        gap: 8px;
      }

      .files-grid {
        grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
        gap: 10px;
      }

      .folder-card,
      .file-card,
      .item-checkbox {
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .folder-card {
        padding: 10px 10px 10px 32px;
      }

      .file-card {
        padding: 12px 10px;
        gap: 8px;
      }
    }
  `]
})
export class FileGridComponent implements OnDestroy {
  @ViewChild('gridContainer') gridContainerRef!: ElementRef<HTMLDivElement>;

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
  private readonly ngZone = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  dragOverFolderId: string | null = null;
  isTouchDevice = typeof window !== 'undefined' && (
    (typeof window.matchMedia === 'function' && window.matchMedia('(pointer: coarse)').matches) ||
    ('ontouchstart' in window && !(typeof window.matchMedia === 'function' && window.matchMedia('(pointer: fine)').matches))
  );

  // Marquee state
  isMarqueeActive = false;
  private marqueeStartX = 0;
  private marqueeStartY = 0;
  private marqueeStartScrollTop = 0;
  private marqueeStartScrollLeft = 0;
  marqueeBox = { left: 0, top: 0, width: 0, height: 0 };
  private marqueeInitialFolderIds: string[] = [];
  private marqueeInitialFileIds: string[] = [];
  private marqueeRafId: number | null = null;
  private boundDocumentMouseMove: ((e: MouseEvent) => void) | null = null;
  private boundDocumentMouseUp: (() => void) | null = null;

  // Pivot for shift range selection
  private lastPivot: { type: 'folder' | 'file'; id: string } | null = null;

  isFolderSelected(id: string): boolean {
    return this.selectedFolderIds.includes(id) || this.selectedFolderId === id;
  }

  isFileSelected(id: string): boolean {
    return this.selectedFileIds.includes(id) || this.selectedFileId === id;
  }

  getFileBadge(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).badge;
  }

  getFileBadgeColor(file: FileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).color;
  }

  onCheckboxClick(event: MouseEvent, type: 'folder' | 'file', id: string): void {
    event.stopPropagation();
    let nextFolderIds = this.selectedFolderIds.length > 0
      ? [...this.selectedFolderIds]
      : (this.selectedFolderId ? [this.selectedFolderId] : []);
    let nextFileIds = this.selectedFileIds.length > 0
      ? [...this.selectedFileIds]
      : (this.selectedFileId ? [this.selectedFileId] : []);

    if (type === 'folder') {
      nextFolderIds = nextFolderIds.includes(id)
        ? nextFolderIds.filter(fId => fId !== id)
        : [...nextFolderIds, id];
    } else {
      nextFileIds = nextFileIds.includes(id)
        ? nextFileIds.filter(fId => fId !== id)
        : [...nextFileIds, id];
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
      ...this.folders.map(f => ({ type: 'folder' as const, id: f.id })),
      ...this.files.map(f => ({ type: 'file' as const, id: f.id }))
    ];
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

  onContainerContextMenu(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('.folder-card, .file-card, button, input')) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.workspaceContextMenu.emit(event);
  }

  onContainerMouseDown(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('.folder-card, .file-card, button, input')) {
      return;
    }

    if (event.button !== 0) return; // Only left click for marquee

    const isCtrl = event.ctrlKey || event.metaKey;
    if (!isCtrl && !event.shiftKey) {
      this.selectedFolderIds = [];
      this.selectedFileIds = [];
      this.selectedFolderId = null;
      this.selectedFileId = null;
      this.selectionChange.emit({ folderIds: [], fileIds: [] });
      this.emptySpaceClick.emit(event);
    }

    if (this.isTouchDevice) {
      return;
    }

    this.marqueeInitialFolderIds = isCtrl ? [...this.selectedFolderIds] : [];
    this.marqueeInitialFileIds = isCtrl ? [...this.selectedFileIds] : [];

    this.isMarqueeActive = true;
    this.marqueeStartX = event.clientX;
    this.marqueeStartY = event.clientY;

    if (this.gridContainerRef) {
      const parent = this.gridContainerRef.nativeElement.parentElement;
      this.marqueeStartScrollTop = parent?.scrollTop || 0;
      this.marqueeStartScrollLeft = parent?.scrollLeft || 0;

      const rect = this.gridContainerRef.nativeElement.getBoundingClientRect();
      this.marqueeBox = {
        left: event.clientX - rect.left,
        top: event.clientY - rect.top,
        width: 0,
        height: 0
      };
    }

    // Attach dynamic listeners outside Angular zone to prevent change detection thrashing
    this.cleanupMarqueeListeners();
    this.boundDocumentMouseMove = (e: MouseEvent) => {
      if (!this.isMarqueeActive) return;
      if (this.marqueeRafId !== null) return;
      this.marqueeRafId = requestAnimationFrame(() => {
        this.marqueeRafId = null;
        this.onDocumentMouseMove(e);
        this.cdr.detectChanges();
      });
    };
    this.boundDocumentMouseUp = () => {
      this.cleanupMarqueeListeners();
      this.ngZone.run(() => {
        this.onDocumentMouseUp();
        this.cdr.markForCheck();
      });
    };

    this.ngZone.runOutsideAngular(() => {
      document.addEventListener('mousemove', this.boundDocumentMouseMove!, { passive: true });
      document.addEventListener('mouseup', this.boundDocumentMouseUp!);
    });
  }

  ngOnDestroy(): void {
    this.cleanupMarqueeListeners();
  }

  private cleanupMarqueeListeners(): void {
    if (this.marqueeRafId !== null) {
      cancelAnimationFrame(this.marqueeRafId);
      this.marqueeRafId = null;
    }
    if (this.boundDocumentMouseMove) {
      document.removeEventListener('mousemove', this.boundDocumentMouseMove);
      this.boundDocumentMouseMove = null;
    }
    if (this.boundDocumentMouseUp) {
      document.removeEventListener('mouseup', this.boundDocumentMouseUp);
      this.boundDocumentMouseUp = null;
    }
  }

  onDocumentMouseMove(event: MouseEvent): void {
    if (!this.isMarqueeActive || !this.gridContainerRef) return;

    const containerEl = this.gridContainerRef.nativeElement;
    const parent = containerEl.parentElement;
    const scrollDiffY = (parent?.scrollTop || 0) - this.marqueeStartScrollTop;
    const scrollDiffX = (parent?.scrollLeft || 0) - this.marqueeStartScrollLeft;

    const containerRect = containerEl.getBoundingClientRect();

    const startX = this.marqueeStartX - scrollDiffX;
    const startY = this.marqueeStartY - scrollDiffY;

    const minX = Math.min(startX, event.clientX);
    const maxX = Math.max(startX, event.clientX);
    const minY = Math.min(startY, event.clientY);
    const maxY = Math.max(startY, event.clientY);

    this.marqueeBox = {
      left: minX - containerRect.left,
      top: minY - containerRect.top,
      width: maxX - minX,
      height: maxY - minY
    };

    // Calculate intersecting items
    const selectedFolderSet = new Set<string>(this.marqueeInitialFolderIds);
    const selectedFileSet = new Set<string>(this.marqueeInitialFileIds);

    const cards = containerEl.querySelectorAll<HTMLElement>('.folder-card, .file-card');
    cards.forEach(card => {
      const rect = card.getBoundingClientRect();
      const intersects = rect.left < maxX && rect.right > minX && rect.top < maxY && rect.bottom > minY;
      const id = card.getAttribute('data-id');
      const type = card.getAttribute('data-type');
      if (id) {
        if (intersects) {
          if (type === 'folder') selectedFolderSet.add(id);
          else selectedFileSet.add(id);
        } else if (!this.marqueeInitialFolderIds.includes(id) && !this.marqueeInitialFileIds.includes(id)) {
          if (type === 'folder') selectedFolderSet.delete(id);
          else selectedFileSet.delete(id);
        }
      }
    });

    this.selectedFolderIds = Array.from(selectedFolderSet);
    this.selectedFileIds = Array.from(selectedFileSet);
    this.selectedFolderId = this.selectedFolderIds.length === 1 && this.selectedFileIds.length === 0 ? this.selectedFolderIds[0] : null;
    this.selectedFileId = this.selectedFileIds.length === 1 && this.selectedFolderIds.length === 0 ? this.selectedFileIds[0] : null;
  }

  onDocumentMouseUp(): void {
    if (this.isMarqueeActive) {
      this.isMarqueeActive = false;
      this.selectionChange.emit({
        folderIds: this.selectedFolderIds,
        fileIds: this.selectedFileIds
      });
    }
  }

  @HostListener('document:dragend')
  onDocumentDragEnd(): void {
    this.dragState.endDrag();
    this.dragOverFolderId = null;
  }

  // Drag and Drop
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
