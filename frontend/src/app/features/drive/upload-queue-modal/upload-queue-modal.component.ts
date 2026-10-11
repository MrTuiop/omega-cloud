import { Component, Input, Output, EventEmitter, inject, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { TranslatePipe } from '../../../shared/pipes/translate.pipe';
import { ApiService } from '../../../core/services/api.service';
import { ToastService } from '../../../core/services/toast.service';

export interface FileQueueItem {
  file: File;
  relativePath: string;
  name: string;
  size: number;
}

@Component({
  selector: 'app-upload-queue-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, GlassModalComponent, GlassButtonComponent, FileSizePipe, TranslatePipe],
  template: `
    <app-glass-modal
      [isOpen]="isOpen"
      [title]="step() === 'queue' ? ('upload.queueTitle' | translate) : ('conflicts.title' | translate)"
      maxWidth="680px"
      (close)="onClose()"
    >
      @if (step() === 'queue') {
        <div class="queue-container">
          <div class="destination-bar">
            <span class="dest-label">{{ 'upload.queueDescription' | translate }}</span>
            <span class="dest-pill">{{ destinationName || 'Root' }}</span>
          </div>

          <!-- Summary Stats -->
          <div class="summary-cards">
            <div class="summary-card">
              <span class="card-label">{{ 'upload.totalFiles' | translate }}</span>
              <span class="card-val">{{ filteredItems().length }} / {{ items().length }}</span>
            </div>
            <div class="summary-card">
              <span class="card-label">{{ 'upload.totalSize' | translate }}</span>
              <span class="card-val">{{ totalSizeBytes() | fileSize }}</span>
            </div>
          </div>

          <!-- Search / Filter -->
          <div class="filter-wrapper">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"></circle>
              <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
            </svg>
            <input
              type="text"
              [placeholder]="'upload.filterPlaceholder' | translate"
              [(ngModel)]="filterQuery"
            />
            @if (filterQuery) {
              <button class="clear-btn" (click)="filterQuery = ''">✕</button>
            }
          </div>

          <!-- Queue List -->
          <div class="queue-list-scroll">
            @if (filteredItems().length === 0) {
              <div class="empty-list">
                {{ 'upload.emptyQueue' | translate }}
              </div>
            } @else {
              @for (item of filteredItems(); track item.file.name + item.file.size + $index) {
                <div class="queue-item">
                  <div class="item-icon">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                      <polyline points="14 2 14 8 20 8"></polyline>
                    </svg>
                  </div>
                  <div class="item-info">
                    <div class="item-name" [title]="item.relativePath || item.name">{{ item.name }}</div>
                    @if (item.relativePath && item.relativePath !== item.name) {
                      <div class="item-subpath">{{ item.relativePath }}</div>
                    }
                  </div>
                  <div class="item-size">{{ item.size | fileSize }}</div>
                  <button class="remove-btn" (click)="removeItem(item)" [title]="'upload.remove' | translate">
                    ✕
                  </button>
                </div>
              }
            }
          </div>
        </div>
      } @else {
        <!-- Conflicts Step -->
        <div class="conflict-container">
          <p class="conflict-subtitle">{{ 'conflicts.subtitle' | translate }}</p>

          <div class="global-action-bar">
            <label class="apply-all-checkbox">
              <input type="checkbox" [(ngModel)]="applyToAll" (change)="onApplyToAllChange()" />
              <span>{{ 'conflicts.applyToAll' | translate }}</span>
            </label>

            @if (applyToAll) {
              <div class="global-strategy-selector">
                <button
                  type="button"
                  class="strat-btn"
                  [class.strat-btn--active]="globalStrategy === 'Replace'"
                  (click)="setGlobalStrategy('Replace')"
                >
                  {{ 'conflicts.replace' | translate }}
                </button>
                <button
                  type="button"
                  class="strat-btn"
                  [class.strat-btn--active]="globalStrategy === 'Rename'"
                  (click)="setGlobalStrategy('Rename')"
                >
                  {{ 'conflicts.rename' | translate }}
                </button>
                <button
                  type="button"
                  class="strat-btn"
                  [class.strat-btn--active]="globalStrategy === 'Skip'"
                  (click)="setGlobalStrategy('Skip')"
                >
                  {{ 'conflicts.skip' | translate }}
                </button>
              </div>
            }
          </div>

          <div class="conflict-list-scroll">
            @for (cName of conflictNames(); track cName) {
              <div class="conflict-card">
                <div class="conflict-file">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"></circle>
                    <line x1="12" y1="8" x2="12" y2="12"></line>
                    <line x1="12" y1="16" x2="12.01" y2="16"></line>
                  </svg>
                  <span class="conflict-name">{{ cName }}</span>
                </div>

                <div class="conflict-strategies">
                  <button
                    type="button"
                    class="strat-pill"
                    [class.strat-pill--active]="resolutions[cName] === 'Replace'"
                    (click)="setFileResolution(cName, 'Replace')"
                  >
                    {{ 'conflicts.replace' | translate }}
                  </button>
                  <button
                    type="button"
                    class="strat-pill"
                    [class.strat-pill--active]="resolutions[cName] === 'Rename'"
                    (click)="setFileResolution(cName, 'Rename')"
                  >
                    {{ 'conflicts.rename' | translate }}
                  </button>
                  <button
                    type="button"
                    class="strat-pill"
                    [class.strat-pill--active]="resolutions[cName] === 'Skip'"
                    (click)="setFileResolution(cName, 'Skip')"
                  >
                    {{ 'conflicts.skip' | translate }}
                  </button>
                </div>
              </div>
            }
          </div>
        </div>
      }

      <div footer class="modal-actions">
        @if (step() === 'queue') {
          <app-glass-button variant="ghost" (click)="onClose()">
            {{ 'common.cancel' | translate }}
          </app-glass-button>
          <app-glass-button
            variant="primary"
            [disabled]="items().length === 0 || checkingConflicts()"
            [loading]="checkingConflicts()"
            (click)="proceedToUpload()"
          >
            {{ 'upload.startUpload' | translate }} ({{ items().length }})
          </app-glass-button>
        } @else {
          <app-glass-button variant="ghost" (click)="step.set('queue')">
            {{ 'common.cancel' | translate }}
          </app-glass-button>
          <app-glass-button variant="primary" (click)="finishUploadWithConflicts()">
            {{ 'conflicts.confirm' | translate }}
          </app-glass-button>
        }
      </div>
    </app-glass-modal>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .queue-container, .conflict-container {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .destination-bar {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--omega-text-secondary);
    }

    .dest-pill {
      background: var(--omega-accent-subtle);
      color: var(--omega-accent);
      padding: 3px 8px;
      border-radius: 6px;
      font-weight: 600;
      font-size: 12px;
    }

    .summary-cards {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .summary-card {
      @include glass-surface;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .card-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--omega-text-muted);
    }

    .card-val {
      font-size: 16px;
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .filter-wrapper {
      position: relative;
      display: flex;
      align-items: center;

      svg {
        position: absolute;
        left: 12px;
        color: var(--omega-text-muted);
        pointer-events: none;
      }

      input {
        width: 100%;
        padding: 8px 32px 8px 34px;
        font-size: 13px;
        border-radius: var(--omega-radius-sm);
        border: 1px solid var(--omega-glass-border-subtle);
        background: var(--omega-glass-bg-sunken);
        color: var(--omega-text-primary);
        outline: none;

        &:focus {
          border-color: var(--omega-accent);
        }
      }

      .clear-btn {
        position: absolute;
        right: 10px;
        background: none;
        border: none;
        color: var(--omega-text-muted);
        cursor: pointer;
      }
    }

    .queue-list-scroll, .conflict-list-scroll {
      max-height: 260px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 6px;
      padding-right: 4px;
    }

    .queue-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 12px;
      border-radius: 8px;
      background: var(--omega-glass-bg-subtle);
      border: 1px solid var(--omega-glass-border-subtle);
      font-size: 13px;
    }

    .item-icon {
      color: var(--omega-accent);
      display: flex;
      align-items: center;
    }

    .item-info {
      flex: 1;
      min-width: 0;
    }

    .item-name {
      font-weight: 500;
      color: var(--omega-text-primary);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .item-subpath {
      font-size: 11px;
      color: var(--omega-text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .item-size {
      font-size: 12px;
      color: var(--omega-text-secondary);
      white-space: nowrap;
    }

    .remove-btn {
      background: none;
      border: none;
      color: var(--omega-text-muted);
      cursor: pointer;
      padding: 4px;
      border-radius: 4px;
      min-width: 24px;
      min-height: 24px;
      display: flex;
      align-items: center;
      justify-content: center;

      &:hover {
        color: var(--omega-danger);
        background: var(--omega-danger-subtle);
      }
    }

    .empty-list {
      text-align: center;
      padding: 32px 16px;
      color: var(--omega-text-muted);
      font-size: 13px;
    }

    .modal-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      width: 100%;
    }

    .conflict-subtitle {
      font-size: 13px;
      color: var(--omega-text-secondary);
      margin: 0;
    }

    .global-action-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 10px;
      padding: 10px 12px;
      background: var(--omega-glass-bg-sunken);
      border-radius: var(--omega-radius-md);
    }

    .apply-all-checkbox {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);
      cursor: pointer;

      input {
        width: 16px;
        height: 16px;
        cursor: pointer;
      }
    }

    .global-strategy-selector, .conflict-strategies {
      display: flex;
      gap: 6px;
    }

    .strat-btn, .strat-pill {
      background: var(--omega-glass-bg-subtle);
      border: 1px solid var(--omega-glass-border-subtle);
      border-radius: 6px;
      padding: 5px 10px;
      font-size: 12px;
      font-weight: 500;
      color: var(--omega-text-secondary);
      cursor: pointer;
      transition: all 0.15s;

      &:hover {
        color: var(--omega-text-primary);
        background: var(--omega-glass-bg-hover);
      }

      &--active {
        background: var(--omega-accent);
        border-color: var(--omega-accent);
        color: #ffffff !important;
      }
    }

    .conflict-card {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 10px 14px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-glass-bg-subtle);
      border: 1px solid var(--omega-glass-border-subtle);
    }

    .conflict-file {
      display: flex;
      align-items: center;
      gap: 8px;
      min-width: 0;
      flex: 1;
      color: var(--omega-warning);

      .conflict-name {
        color: var(--omega-text-primary);
        font-weight: 500;
        font-size: 13px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
    }
  `]
})
export class UploadQueueModalComponent {
  private readonly apiService = inject(ApiService);
  private readonly toastService = inject(ToastService);

  @Input() isOpen = false;
  @Input() destinationName = '';
  @Input() destinationFolderId: string | null = null;
  @Input() isShared = false;

  @Input() set inputFiles(files: File[]) {
    if (files) {
      this.items.set(
        files.map(f => ({
          file: f,
          relativePath: (f as any).webkitRelativePath || f.name,
          name: f.name,
          size: f.size
        }))
      );
      this.step.set('queue');
      this.conflictNames.set([]);
      this.resolutions = {};
    }
  }

  @Output() close = new EventEmitter<void>();
  @Output() startUpload = new EventEmitter<{
    files: File[];
    resolutions?: Record<string, 'Replace' | 'Rename' | 'Skip'>;
  }>();

  readonly items = signal<FileQueueItem[]>([]);
  readonly step = signal<'queue' | 'conflicts'>('queue');
  readonly conflictNames = signal<string[]>([]);
  readonly checkingConflicts = signal<boolean>(false);

  filterQuery = '';
  applyToAll = true;
  globalStrategy: 'Replace' | 'Rename' | 'Skip' = 'Rename';
  resolutions: Record<string, 'Replace' | 'Rename' | 'Skip'> = {};

  readonly filteredItems = computed(() => {
    const q = this.filterQuery.trim().toLowerCase();
    if (!q) return this.items();
    return this.items().filter(item =>
      item.name.toLowerCase().includes(q) || item.relativePath.toLowerCase().includes(q)
    );
  });

  readonly totalSizeBytes = computed(() => {
    return this.items().reduce((sum, item) => sum + item.size, 0);
  });

  removeItem(item: FileQueueItem): void {
    this.items.update(list => list.filter(i => i !== item));
  }

  onClose(): void {
    this.close.emit();
  }

  proceedToUpload(): void {
    const currentFiles = this.items().map(i => i.file);
    if (currentFiles.length === 0) return;

    this.checkingConflicts.set(true);
    const fileNames = currentFiles.map(f => f.name);

    this.apiService.checkConflicts(fileNames, this.destinationFolderId, this.isShared).subscribe({
      next: conflictingNames => {
        this.checkingConflicts.set(false);
        if (conflictingNames && conflictingNames.length > 0) {
          this.conflictNames.set(conflictingNames);
          this.resolutions = {};
          conflictingNames.forEach(name => {
            this.resolutions[name] = this.globalStrategy;
          });
          this.step.set('conflicts');
        } else {
          // No conflicts
          this.startUpload.emit({ files: currentFiles });
          this.onClose();
        }
      },
      error: () => {
        this.checkingConflicts.set(false);
        // If conflict check fails (e.g. offline/network), proceed directly
        this.startUpload.emit({ files: currentFiles });
        this.onClose();
      }
    });
  }

  onApplyToAllChange(): void {
    if (this.applyToAll) {
      this.conflictNames().forEach(name => {
        this.resolutions[name] = this.globalStrategy;
      });
    }
  }

  setGlobalStrategy(strat: 'Replace' | 'Rename' | 'Skip'): void {
    this.globalStrategy = strat;
    if (this.applyToAll) {
      this.conflictNames().forEach(name => {
        this.resolutions[name] = strat;
      });
    }
  }

  setFileResolution(fileName: string, strat: 'Replace' | 'Rename' | 'Skip'): void {
    this.resolutions[fileName] = strat;
    if (!this.applyToAll) {
      // Individual strategy
    } else {
      // If user changed one, uncheck applyToAll or sync
      this.applyToAll = false;
    }
  }

  finishUploadWithConflicts(): void {
    const currentFiles = this.items().map(i => i.file);
    const activeFiles = currentFiles.filter(f => this.resolutions[f.name] !== 'Skip');

    this.startUpload.emit({
      files: activeFiles,
      resolutions: this.resolutions
    });
    this.onClose();
  }
}
