import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { TrashContent, TrashFileItem, TrashFolderItem } from '../../../core/models/trash.models';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { getFileTypeInfo } from '../../../shared/utils/file-icons';

@Component({
  selector: 'app-trash-bin',
  standalone: true,
  imports: [CommonModule, FileSizePipe, RelativeTimePipe, GlassButtonComponent, GlassModalComponent],
  template: `
    <div class="trash-container">
      <!-- Top Bar / Banner -->
      <div class="trash-header">
        <div class="header-left">
          <h2 class="page-title">Trash Bin</h2>
          <span class="trash-summary">
            {{ content()?.totalItemCount || 0 }} items ({{ content()?.totalTrashSizeBytes || 0 | fileSize }})
          </span>
        </div>

        <div class="header-actions">
          <app-glass-button
            variant="danger"
            size="md"
            [disabled]="(content()?.totalItemCount || 0) === 0"
            (btnClick)="confirmEmptyOpen.set(true)"
          >
            Empty Trash
          </app-glass-button>
        </div>
      </div>

      <div class="auto-clean-notice">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="12" cy="12" r="10"></circle>
          <polyline points="12 6 12 12 16 14"></polyline>
        </svg>
        <span>Items in the trash will be automatically cleaned up after 30 days to free storage quota.</span>
      </div>

      <!-- Content Area -->
      @if (loading()) {
        <div class="loading-state">Loading trash contents...</div>
      } @else if ((content()?.folders?.length || 0) === 0 && (content()?.files?.length || 0) === 0) {
        <div class="empty-state">
          <div class="empty-icon">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
          </div>
          <h3>Trash is empty</h3>
          <p>Files and folders that you delete will appear here before being permanently removed.</p>
        </div>
      } @else {
        <div class="table-wrapper">
          <table class="glass-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Kind</th>
                <th>Size</th>
                <th>Deleted</th>
                <th class="col-actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              <!-- Folders -->
              @for (folder of content()?.folders; track folder.id) {
                <tr class="table-row">
                  <td>
                    <div class="item-name-cell">
                      <span class="folder-icon" [style.color]="folder.color || 'var(--omega-accent)'">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
                          <path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/>
                        </svg>
                      </span>
                      <span class="item-name">{{ folder.name }}</span>
                    </div>
                  </td>
                  <td>Folder</td>
                  <td>—</td>
                  <td>{{ folder.deletedAt | relativeTime }}</td>
                  <td class="col-actions">
                    <button class="action-btn action-btn--restore" (click)="restoreItem(folder.id)" title="Restore">
                      Restore
                    </button>
                    <button class="action-btn action-btn--delete" (click)="deletePermanent(folder.id)" title="Delete Permanently">
                      Delete
                    </button>
                  </td>
                </tr>
              }

              <!-- Files -->
              @for (file of content()?.files; track file.id) {
                <tr class="table-row">
                  <td>
                    <div class="item-name-cell">
                      <span class="badge" [style.background-color]="getFileBadgeColor(file)">
                        {{ getFileBadge(file) }}
                      </span>
                      <span class="item-name">{{ file.name }}</span>
                    </div>
                  </td>
                  <td>{{ file.extension ? file.extension.toUpperCase() : 'File' }}</td>
                  <td>{{ file.sizeBytes | fileSize }}</td>
                  <td>{{ file.deletedAt | relativeTime }}</td>
                  <td class="col-actions">
                    <button class="action-btn action-btn--restore" (click)="restoreItem(file.id)" title="Restore">
                      Restore
                    </button>
                    <button class="action-btn action-btn--delete" (click)="deletePermanent(file.id)" title="Delete Permanently">
                      Delete
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    <!-- Confirm Empty Trash Modal -->
    <app-glass-modal
      [isOpen]="confirmEmptyOpen()"
      title="Empty Trash Bin"
      (close)="confirmEmptyOpen.set(false)"
    >
      <p>Are you sure you want to empty the trash bin? All items will be permanently erased from storage and cannot be restored.</p>

      <div footer>
        <app-glass-button variant="secondary" (btnClick)="confirmEmptyOpen.set(false)">Cancel</app-glass-button>
        <app-glass-button variant="danger" (btnClick)="emptyTrash()">Empty Permanently</app-glass-button>
      </div>
    </app-glass-modal>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .trash-container {
      display: flex;
      flex-direction: column;
      gap: 16px;
      width: 100%;
    }

    .trash-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding-bottom: 8px;
    }

    .page-title {
      font-size: 22px;
      font-weight: 700;
      color: var(--omega-text-primary);
    }

    .trash-summary {
      font-size: 13px;
      color: var(--omega-text-muted);
    }

    .auto-clean-notice {
      @include glass-sunken;
      border-radius: var(--omega-radius-md);
      padding: 10px 14px;
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 13px;
      color: var(--omega-text-secondary);

      svg {
        color: var(--omega-warning);
        flex-shrink: 0;
      }
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
        border-bottom: 1px solid var(--omega-glass-border);
        background: var(--omega-glass-bg-sunken);
      }

      td {
        padding: 12px 16px;
        border-bottom: 1px solid var(--omega-glass-border-subtle);
        vertical-align: middle;
      }
    }

    .item-name-cell {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 2px 6px;
      border-radius: 4px;
      color: #ffffff;
      font-size: 10px;
      font-weight: 700;
      text-transform: uppercase;
    }

    .item-name {
      font-weight: 500;
      color: var(--omega-text-primary);
    }

    .col-actions {
      text-align: right;
    }

    .action-btn {
      font-size: 12px;
      font-weight: 500;
      padding: 4px 10px;
      border-radius: var(--omega-radius-sm);
      margin-left: 6px;
      transition: all 0.15s;

      &--restore {
        color: var(--omega-accent);
        background: var(--omega-accent-subtle);

        &:hover {
          background: var(--omega-accent);
          color: #ffffff;
        }
      }

      &--delete {
        color: var(--omega-danger);
        background: var(--omega-danger-subtle);

        &:hover {
          background: var(--omega-danger);
          color: #ffffff;
        }
      }
    }

    .empty-state {
      padding: 60px 20px;
      display: flex;
      flex-direction: column;
      align-items: center;
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

    .loading-state {
      padding: 40px;
      text-align: center;
      color: var(--omega-text-muted);
    }
  `]
})
export class TrashBinComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  content = signal<TrashContent | null>(null);
  loading = signal<boolean>(false);
  confirmEmptyOpen = signal<boolean>(false);

  ngOnInit(): void {
    this.loadTrash();
  }

  loadTrash(): void {
    this.loading.set(true);
    this.apiService.getTrash().subscribe({
      next: res => {
        this.content.set(res);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  restoreItem(id: string): void {
    this.apiService.restoreTrashItem(id).subscribe({
      next: () => this.loadTrash()
    });
  }

  deletePermanent(id: string): void {
    this.apiService.permanentlyDeleteTrashItem(id).subscribe({
      next: () => this.loadTrash()
    });
  }

  emptyTrash(): void {
    this.apiService.emptyTrash().subscribe({
      next: () => {
        this.confirmEmptyOpen.set(false);
        this.loadTrash();
      }
    });
  }

  getFileBadge(file: TrashFileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).badge;
  }

  getFileBadgeColor(file: TrashFileItem): string {
    return getFileTypeInfo(file.extension, file.mimeType).color;
  }
}
