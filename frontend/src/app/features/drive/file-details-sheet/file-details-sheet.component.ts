import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { FileItem, FileMetadata } from '../../../core/models/file.models';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { getFileTypeInfo } from '../../../shared/utils/file-icons';

@Component({
  selector: 'app-file-details-sheet',
  standalone: true,
  imports: [CommonModule, FileSizePipe, RelativeTimePipe, GlassButtonComponent],
  template: `
    @if (file) {
      <div class="sheet-container anim-slide-in-right">
        <!-- Sheet Header -->
        <div class="sheet-header">
          <div class="header-info">
            <h3 class="sheet-title" [title]="file.name">{{ file.name }}</h3>
            <span class="sheet-subtitle">{{ file.extension ? ('.' + file.extension) : 'File' }}</span>
          </div>
          <button class="sheet-close" (click)="close.emit()" title="Close Inspector">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>

        <!-- Sheet Body -->
        <div class="sheet-body">
          <!-- Visual Hero / Preview -->
          <div class="preview-hero">
            <div class="hero-badge" [style.background-color]="badgeColor">
              {{ badgeText }}
            </div>
          </div>

          <!-- Quick Actions Bar -->
          <div class="action-bar">
            <app-glass-button variant="primary" size="sm" (btnClick)="download.emit(file)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
              Download
            </app-glass-button>

            <app-glass-button variant="secondary" size="sm" (btnClick)="rename.emit(file)">
              Rename
            </app-glass-button>

            <app-glass-button
              variant="secondary"
              size="sm"
              (btnClick)="toggleFavorite.emit(file)"
            >
              {{ file.isFavorite ? '★ Starred' : '☆ Star' }}
            </app-glass-button>

            <app-glass-button variant="danger" size="sm" (btnClick)="delete.emit(file)">
              Delete
            </app-glass-button>
          </div>

          <!-- Metadata Section -->
          <div class="meta-section">
            <h4 class="section-heading">Information</h4>

            <div class="meta-row">
              <span class="meta-label">Size</span>
              <span class="meta-value">{{ file.sizeBytes | fileSize }} ({{ file.sizeBytes.toLocaleString() }} bytes)</span>
            </div>

            <div class="meta-row">
              <span class="meta-label">MIME Type</span>
              <span class="meta-value">{{ file.mimeType || 'application/octet-stream' }}</span>
            </div>

            <div class="meta-row">
              <span class="meta-label">Modified</span>
              <span class="meta-value">{{ file.updatedAt | relativeTime }}</span>
            </div>

            <div class="meta-row">
              <span class="meta-label">Created</span>
              <span class="meta-value">{{ file.createdAt | relativeTime }}</span>
            </div>

            @if (metadata(); as meta) {
              <div class="meta-row">
                <span class="meta-label">Storage Node</span>
                <span class="meta-value node-badge">
                  {{ meta.storageNodeName || 'Local Node' }}
                  <small>({{ meta.storageNodeType || 'Local' }})</small>
                </span>
              </div>

              <div class="meta-row">
                <span class="meta-label">SHA-256</span>
                <div class="hash-box">
                  <span class="hash-text" [title]="meta.sha256Hash">{{ meta.sha256Hash || '—' }}</span>
                  @if (meta.sha256Hash) {
                    <button class="copy-btn" (click)="copyHash(meta.sha256Hash)" [title]="copied() ? 'Copied!' : 'Copy SHA-256'">
                      {{ copied() ? '✓' : 'Copy' }}
                    </button>
                  }
                </div>
              </div>
            } @else if (loadingMetadata()) {
              <div class="meta-row meta-row--loading">
                <span>Loading node details...</span>
              </div>
            }
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .sheet-container {
      @include glass-elevated;
      width: 340px;
      height: 100%;
      border-radius: var(--omega-radius-xl) 0 0 var(--omega-radius-xl);
      border-right: none;
      display: flex;
      flex-direction: column;
      z-index: 100;
      box-shadow: -8px 0 32px rgba(0, 0, 0, 0.15);
      overflow: hidden;
    }

    .sheet-header {
      padding: 18px 20px 14px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
    }

    .header-info {
      min-width: 0;
      flex: 1;
    }

    .sheet-title {
      font-size: 15px;
      font-weight: 600;
      color: var(--omega-text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .sheet-subtitle {
      font-size: 12px;
      color: var(--omega-text-muted);
      text-transform: uppercase;
    }

    .sheet-close {
      width: 30px;
      height: 30px;
      border-radius: 50%;
      background: var(--omega-glass-bg-sunken);
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);
      transition: all 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }
    }

    .sheet-body {
      padding: 20px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 20px;
      flex: 1;
    }

    .preview-hero {
      height: 120px;
      border-radius: var(--omega-radius-lg);
      @include glass-sunken;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .hero-badge {
      padding: 8px 18px;
      border-radius: var(--omega-radius-md);
      color: #ffffff;
      font-size: 15px;
      font-weight: 700;
      letter-spacing: 0.05em;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
    }

    .action-bar {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .meta-section {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .section-heading {
      font-size: 12px;
      font-weight: 600;
      color: var(--omega-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .meta-row {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 13px;

      &--loading {
        color: var(--omega-text-muted);
        font-style: italic;
      }
    }

    .meta-label {
      color: var(--omega-text-muted);
      font-size: 11px;
    }

    .meta-value {
      color: var(--omega-text-primary);
      font-weight: 500;
      word-break: break-word;
    }

    .node-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;

      small {
        color: var(--omega-text-muted);
      }
    }

    .hash-box {
      display: flex;
      align-items: center;
      gap: 8px;
      background: var(--omega-glass-bg-sunken);
      padding: 6px 10px;
      border-radius: var(--omega-radius-sm);
    }

    .hash-text {
      font-family: var(--omega-font-mono);
      font-size: 11px;
      color: var(--omega-text-secondary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      flex: 1;
    }

    .copy-btn {
      font-size: 11px;
      font-weight: 600;
      color: var(--omega-accent);
      padding: 2px 6px;
      border-radius: 4px;

      &:hover {
        background: var(--omega-accent-subtle);
      }
    }

    @media (max-width: 768px), (pointer: coarse) {
      .sheet-container {
        position: fixed;
        top: 0;
        right: 0;
        bottom: 0;
        left: 0;
        width: 100% !important;
        max-width: 100vw;
        border-radius: 0;
        z-index: 150;
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
        box-shadow: none;
      }

      .sheet-body {
        -webkit-overflow-scrolling: touch;
        overscroll-behavior-y: contain;
      }
    }
  `]
})
export class FileDetailsSheetComponent implements OnChanges {
  private readonly apiService = inject(ApiService);

  @Input() file: FileItem | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() download = new EventEmitter<FileItem>();
  @Output() rename = new EventEmitter<FileItem>();
  @Output() toggleFavorite = new EventEmitter<FileItem>();
  @Output() delete = new EventEmitter<FileItem>();

  metadata = signal<FileMetadata | null>(null);
  loadingMetadata = signal<boolean>(false);
  copied = signal<boolean>(false);

  get badgeText(): string {
    if (!this.file) return '';
    return getFileTypeInfo(this.file.extension, this.file.mimeType).badge;
  }

  get badgeColor(): string {
    if (!this.file) return '#8e8e93';
    return getFileTypeInfo(this.file.extension, this.file.mimeType).color;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['file'] && this.file) {
      this.loadMetadata(this.file.id);
    }
  }

  loadMetadata(fileId: string): void {
    this.loadingMetadata.set(true);
    this.metadata.set(null);

    this.apiService.getFileMetadata(fileId).subscribe({
      next: meta => {
        this.metadata.set(meta);
        this.loadingMetadata.set(false);
      },
      error: () => {
        this.loadingMetadata.set(false);
      }
    });
  }

  copyHash(hash: string): void {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(hash).then(() => {
        this.copied.set(true);
        setTimeout(() => this.copied.set(false), 2000);
      });
    }
  }
}
