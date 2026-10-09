import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost' | 'icon';
export type ButtonSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-glass-button',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button
      [type]="type"
      [disabled]="disabled || loading"
      [class]="'glass-btn glass-btn--' + variant + ' glass-btn--' + size"
      [class.glass-btn--loading]="loading"
      (click)="onClick($event)"
    >
      @if (loading) {
        <span class="spinner"></span>
      }
      <span class="btn-content" [class.btn-content--hidden]="loading">
        <ng-content></ng-content>
      </span>
    </button>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .glass-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      font-weight: 500;
      border-radius: var(--omega-radius-md);
      transition: all 0.2s var(--omega-ease-smooth);
      position: relative;
      white-space: nowrap;
      text-decoration: none;

      &--sm {
        height: 32px;
        padding: 0 12px;
        font-size: 13px;
      }

      &--md {
        height: 40px;
        padding: 0 18px;
        font-size: 14px;
      }

      &--lg {
        height: 48px;
        padding: 0 24px;
        font-size: 16px;
      }

      // Variants
      &--primary {
        background: var(--omega-accent-gradient);
        color: #ffffff;
        border: 1px solid rgba(255, 255, 255, 0.25);
        box-shadow: 0 4px 14px 0 rgba(0, 113, 227, 0.35);

        &:hover:not(:disabled) {
          box-shadow: 0 6px 20px 0 rgba(0, 113, 227, 0.5);
          transform: translateY(-1px);
        }

        &:active:not(:disabled) {
          transform: translateY(0) scale(0.98);
        }
      }

      &--secondary {
        @include glass-surface;
        color: var(--omega-text-primary);

        &:hover:not(:disabled) {
          background: var(--omega-glass-bg-hover);
          border-color: rgba(255, 255, 255, 0.45);
          transform: translateY(-1px);
        }

        &:active:not(:disabled) {
          transform: translateY(0) scale(0.98);
        }
      }

      &--danger {
        background: var(--omega-danger);
        color: #ffffff;
        border: 1px solid rgba(255, 255, 255, 0.2);
        box-shadow: 0 4px 14px 0 rgba(255, 69, 58, 0.35);

        &:hover:not(:disabled) {
          box-shadow: 0 6px 20px 0 rgba(255, 69, 58, 0.5);
          transform: translateY(-1px);
        }

        &:active:not(:disabled) {
          transform: translateY(0) scale(0.98);
        }
      }

      &--ghost {
        background: transparent;
        color: var(--omega-text-secondary);
        border: 1px solid transparent;

        &:hover:not(:disabled) {
          background: var(--omega-glass-bg-hover);
          color: var(--omega-text-primary);
        }
      }

      &--icon {
        width: 38px;
        height: 38px;
        padding: 0;
        border-radius: var(--omega-radius-pill);
        background: var(--omega-glass-bg);
        border: 1px solid var(--omega-glass-border);
        color: var(--omega-text-primary);

        &:hover:not(:disabled) {
          background: var(--omega-glass-bg-hover);
          transform: translateY(-1px);
        }

        &:active:not(:disabled) {
          transform: scale(0.95);
        }
      }

      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    }

    .btn-content {
      display: inline-flex;
      align-items: center;
      gap: 8px;

      &--hidden {
        visibility: hidden;
      }
    }

    .spinner {
      position: absolute;
      width: 16px;
      height: 16px;
      border: 2px solid rgba(255, 255, 255, 0.3);
      border-top-color: currentColor;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `]
})
export class GlassButtonComponent {
  @Input() variant: ButtonVariant = 'primary';
  @Input() size: ButtonSize = 'md';
  @Input() type: 'button' | 'submit' | 'reset' = 'button';
  @Input() disabled: boolean = false;
  @Input() loading: boolean = false;

  @Output() btnClick = new EventEmitter<MouseEvent>();

  onClick(event: MouseEvent): void {
    if (!this.disabled && !this.loading) {
      this.btnClick.emit(event);
    }
  }
}
