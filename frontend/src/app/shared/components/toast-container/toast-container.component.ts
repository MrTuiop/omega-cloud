import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '../../../core/services/toast.service';
import { ToastMessage } from '../../../core/models/toast.models';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (toastService.toasts().length > 0) {
      <div class="toast-stack anim-fade-in" aria-live="polite">
        @if (toastService.toasts().length > 1) {
          <div class="toast-stack-header anim-slide-in-down">
            <button class="clear-all-btn" (click)="toastService.clearAll()">
              <span>Очистить все</span>
              <span class="count-pill">{{ toastService.toasts().length }}</span>
            </button>
          </div>
        }

        <div class="toast-list">
          @for (toast of toastService.toasts(); track toast.id) {
            <div
              class="toast-card toast-card--{{ toast.type }} anim-slide-in-right"
              role="alert"
            >
              <div class="toast-icon">
                @switch (toast.type) {
                  @case ('success') {
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                      <polyline points="20 6 9 17 4 12"></polyline>
                    </svg>
                  }
                  @case ('error') {
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                      <circle cx="12" cy="12" r="10"></circle>
                      <line x1="12" y1="8" x2="12" y2="12"></line>
                      <line x1="12" y1="16" x2="12.01" y2="16"></line>
                    </svg>
                  }
                  @case ('warning') {
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                      <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                      <line x1="12" y1="9" x2="12" y2="13"></line>
                      <line x1="12" y1="17" x2="12.01" y2="17"></line>
                    </svg>
                  }
                  @default {
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                      <circle cx="12" cy="12" r="10"></circle>
                      <line x1="12" y1="16" x2="12" y2="12"></line>
                      <line x1="12" y1="8" x2="12.01" y2="8"></line>
                    </svg>
                  }
                }
              </div>

              <div class="toast-message">
                {{ toast.message }}
              </div>

              <button
                class="toast-close"
                (click)="toastService.dismiss(toast.id)"
                title="Dismiss"
              >
                ✕
              </button>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .toast-stack {
      position: fixed;
      top: 24px;
      right: 24px;
      z-index: 10001;
      display: flex;
      flex-direction: column;
      gap: 10px;
      max-width: 380px;
      width: calc(100vw - 48px);
      pointer-events: none;
    }

    .toast-stack-header {
      display: flex;
      justify-content: flex-end;
      pointer-events: auto;
    }

    .clear-all-btn {
      @include glass-sunken;
      border: 1px solid var(--omega-glass-border-subtle);
      border-radius: var(--omega-radius-full, 9999px);
      padding: 5px 12px;
      font-size: 12px;
      font-weight: 500;
      color: var(--omega-text-secondary);
      display: inline-flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      backdrop-filter: blur(12px);
      -webkit-backdrop-filter: blur(12px);
      transition: all 0.2s ease;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
        transform: translateY(-1px);
        box-shadow: var(--omega-shadow-sm);
      }

      .count-pill {
        background: var(--omega-accent);
        color: #ffffff;
        font-size: 11px;
        font-weight: 600;
        border-radius: 10px;
        padding: 1px 6px;
      }
    }

    .toast-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .toast-card {
      @include glass-elevated;
      pointer-events: auto;
      border-radius: var(--omega-radius-lg);
      padding: 12px 14px;
      display: flex;
      align-items: center;
      gap: 12px;
      box-shadow: var(--omega-shadow-lg);
      border-left: 4px solid var(--omega-accent);
      transition: all 0.25s var(--omega-ease-spring);

      &--success {
        border-left-color: #30d158;
        .toast-icon { color: #30d158; }
      }

      &--error {
        border-left-color: #ff453a;
        .toast-icon { color: #ff453a; }
      }

      &--warning {
        border-left-color: #ff9f0a;
        .toast-icon { color: #ff9f0a; }
      }

      &--info {
        border-left-color: #0071e3;
        .toast-icon { color: #0071e3; }
      }
    }

    .toast-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .toast-message {
      flex: 1;
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);
      line-height: 1.4;
      word-break: break-word;
    }

    .toast-close {
      width: 22px;
      height: 22px;
      border-radius: 50%;
      background: transparent;
      color: var(--omega-text-muted);
      border: none;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      cursor: pointer;
      flex-shrink: 0;
      transition: all 0.15s ease;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }
    }

    @media (max-width: 768px), (pointer: coarse) {
      .toast-batch-toggle,
      .toast-item {
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }
    }
  `]
})
export class ToastContainerComponent {
  readonly toastService = inject(ToastService);
}
