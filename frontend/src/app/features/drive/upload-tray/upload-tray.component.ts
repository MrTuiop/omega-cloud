import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ResumableUploaderService } from '../../../core/services/resumable-uploader.service';
import { ProgressRingComponent } from '../../../shared/components/progress-ring/progress-ring.component';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { UploadTask } from '../../../core/models/upload.models';

@Component({
  selector: 'app-upload-tray',
  standalone: true,
  imports: [CommonModule, ProgressRingComponent, FileSizePipe],
  template: `
    @if (uploader.tasks().length > 0) {
      <div class="tray-wrapper" [class.tray-wrapper--expanded]="uploader.isTrayOpen()">
        <!-- Collapsed Capsule Bar -->
        @if (!uploader.isTrayOpen()) {
          <button class="capsule-btn anim-scale-in" (click)="uploader.toggleTray()">
            <app-progress-ring
              [size]="28"
              [strokeWidth]="3"
              [progress]="uploader.overallProgress()"
              [color]="uploader.hasActiveUploads() ? 'var(--omega-accent)' : 'var(--omega-success)'"
            ></app-progress-ring>
            <div class="capsule-text">
              @if (uploader.hasActiveUploads()) {
                <span class="capsule-title">Uploading {{ uploader.activeCount() }} item(s)</span>
                <span class="capsule-meta">{{ uploader.overallSpeedBytesPerSec() | fileSize }}/s</span>
              } @else {
                <span class="capsule-title">{{ uploader.completedCount() }} Upload(s) Complete</span>
              }
            </div>
            <svg class="chevron-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="18 15 12 9 6 15"></polyline>
            </svg>
          </button>
        } @else {
          <!-- Expanded Glass Panel -->
          <div class="expanded-panel anim-slide-in-up">
            <div class="panel-header">
              <div class="header-left">
                <span class="panel-title">Uploads</span>
                <span class="panel-badge">{{ uploader.tasks().length }}</span>
              </div>
              <div class="header-actions">
                @if (uploader.completedCount() > 0) {
                  <button class="header-action-btn" (click)="uploader.clearCompleted()">Clear</button>
                }
                <button class="header-close-btn" (click)="uploader.toggleTray()" title="Minimize">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="6 9 12 15 18 9"></polyline>
                  </svg>
                </button>
              </div>
            </div>

            <div class="task-list">
              @for (task of uploader.tasks(); track task.id) {
                <div class="task-item" [class.task-item--error]="task.status === 'error'">
                  <div class="task-main">
                    <div class="task-info">
                      <span class="task-name" [title]="task.file.name">{{ task.file.name }}</span>
                      <div class="task-sub">
                        <span>{{ task.bytesUploaded | fileSize }} / {{ task.totalBytes | fileSize }}</span>
                        @if (task.status === 'uploading' && task.speedBytesPerSec > 0) {
                          <span>•</span>
                          <span>{{ task.speedBytesPerSec | fileSize }}/s</span>
                        }
                        @if (task.totalChunks > 1) {
                          <span>•</span>
                          <span>Chunk {{ task.currentChunkIndex + 1 }}/{{ task.totalChunks }}</span>
                        }
                      </div>
                    </div>

                    <!-- Status / Action Buttons -->
                    <div class="task-actions">
                      @if (task.status === 'uploading') {
                        <button class="task-btn" (click)="uploader.pauseTask(task.id)" title="Pause">
                          ⏸
                        </button>
                      } @else if (task.status === 'paused') {
                        <button class="task-btn" (click)="uploader.resumeTask(task.id)" title="Resume">
                          ▶
                        </button>
                      }

                      @if (task.status === 'completed') {
                        <span class="status-icon status-icon--success" title="Completed">✓</span>
                      } @else if (task.status === 'error') {
                        <span class="status-icon status-icon--error" [title]="task.errorMessage || 'Error'">!</span>
                      } @else {
                        <button class="task-btn" (click)="uploader.removeTask(task.id)" title="Cancel">
                          ✕
                        </button>
                      }
                    </div>
                  </div>

                  <!-- Progress Bar -->
                  @if (task.status !== 'completed') {
                    <div class="task-progress-track">
                      <div
                        class="task-progress-fill"
                        [class.task-progress-fill--error]="task.status === 'error'"
                        [style.width.%]="task.progress"
                      ></div>
                    </div>
                  }
                  @if (task.errorMessage) {
                    <div class="task-error-text">{{ task.errorMessage }}</div>
                  }
                </div>
              }
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .tray-wrapper {
      position: fixed;
      bottom: 24px;
      right: 28px;
      z-index: 1000;
    }

    .capsule-btn {
      @include glass-elevated;
      border-radius: var(--omega-radius-pill);
      padding: 8px 16px 8px 10px;
      display: flex;
      align-items: center;
      gap: 12px;
      cursor: pointer;
      box-shadow: var(--omega-shadow-md);
      transition: all 0.2s var(--omega-ease-spring);

      &:hover {
        background: var(--omega-glass-bg-hover);
        transform: translateY(-2px);
        box-shadow: var(--omega-shadow-lg);
      }
    }

    .capsule-text {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 2px;
    }

    .capsule-title {
      font-size: 13px;
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .capsule-meta {
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .chevron-icon {
      color: var(--omega-text-muted);
      margin-left: 4px;
    }

    .expanded-panel {
      @include glass-elevated;
      width: 360px;
      border-radius: var(--omega-radius-xl);
      box-shadow: var(--omega-shadow-dropdown);
      display: flex;
      flex-direction: column;
      overflow: hidden;
      max-height: 440px;
    }

    .panel-header {
      padding: 14px 18px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .panel-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .panel-badge {
      background: var(--omega-accent-subtle);
      color: var(--omega-accent);
      font-size: 11px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: var(--omega-radius-pill);
    }

    .header-actions {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .header-action-btn {
      font-size: 12px;
      color: var(--omega-accent);
      font-weight: 500;
      padding: 2px 6px;
      border-radius: 4px;

      &:hover {
        background: var(--omega-accent-subtle);
      }
    }

    .header-close-btn {
      width: 24px;
      height: 24px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);

      &:hover {
        background: var(--omega-glass-bg-sunken);
        color: var(--omega-text-primary);
      }
    }

    .task-list {
      padding: 12px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .task-item {
      @include glass-sunken;
      border-radius: var(--omega-radius-md);
      padding: 10px 12px;
      display: flex;
      flex-direction: column;
      gap: 8px;

      &--error {
        border-color: rgba(255, 69, 58, 0.4);
      }
    }

    .task-main {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }

    .task-info {
      min-width: 0;
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .task-name {
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .task-sub {
      font-size: 11px;
      color: var(--omega-text-muted);
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .task-actions {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .task-btn {
      width: 24px;
      height: 24px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      color: var(--omega-text-secondary);
      background: var(--omega-glass-bg);

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }
    }

    .status-icon {
      width: 22px;
      height: 22px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      font-weight: 700;

      &--success {
        background: var(--omega-success-subtle);
        color: var(--omega-success);
      }

      &--error {
        background: var(--omega-danger-subtle);
        color: var(--omega-danger);
      }
    }

    .task-progress-track {
      width: 100%;
      height: 4px;
      background: rgba(120, 120, 128, 0.15);
      border-radius: var(--omega-radius-pill);
      overflow: hidden;
    }

    .task-progress-fill {
      height: 100%;
      background: var(--omega-accent-gradient);
      border-radius: var(--omega-radius-pill);
      transition: width 0.2s linear;

      &--error {
        background: var(--omega-danger);
      }
    }

    .task-error-text {
      font-size: 11px;
      color: var(--omega-danger);
    }

    @media (max-width: 768px), (pointer: coarse) {
      .tray-wrapper {
        bottom: 12px;
        right: 12px;
        left: 12px;
      }

      .expanded-panel {
        width: 100%;
        max-width: 100%;
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .capsule-btn {
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }
    }
  `]
})
export class UploadTrayComponent {
  readonly uploader = inject(ResumableUploaderService);
}
