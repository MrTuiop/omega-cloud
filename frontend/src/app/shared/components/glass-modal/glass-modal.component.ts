import { Component, Input, Output, EventEmitter, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-glass-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (isOpen) {
      <div
        class="modal-backdrop"
        (mousedown)="onBackdropMouseDown($event)"
        (mouseup)="onBackdropMouseUp($event)"
      >
        <div
          class="modal-dialog"
          [style.max-width]="maxWidth"
          (click)="$event.stopPropagation()"
        >
          <div class="modal-header">
            <h3 class="modal-title">{{ title }}</h3>
            <button class="modal-close" (click)="close.emit()" title="Close">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <div class="modal-body">
            <ng-content></ng-content>
          </div>

          <div class="modal-footer">
            <ng-content select="[footer]"></ng-content>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .modal-backdrop {
      position: fixed;
      inset: 0;
      z-index: 9999;
      background: rgba(0, 0, 0, 0.45);
      backdrop-filter: blur(12px);
      -webkit-backdrop-filter: blur(12px);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      animation: appleFadeIn 0.2s ease-out;
    }

    .modal-dialog {
      @include glass-elevated;
      width: 100%;
      border-radius: var(--omega-radius-xl);
      animation: appleScaleIn 0.3s var(--omega-ease-spring);
      display: flex;
      flex-direction: column;
      overflow: hidden;
      max-height: 90vh;
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px 14px;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
    }

    .modal-title {
      font-size: 18px;
      font-weight: 600;
      color: var(--omega-text-primary);
      letter-spacing: -0.01em;
    }

    .modal-close {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);
      background: var(--omega-glass-bg-sunken);
      transition: all 0.2s;

      &:hover {
        color: var(--omega-text-primary);
        background: var(--omega-glass-bg-hover);
        transform: scale(1.05);
      }
    }

    .modal-body {
      padding: 20px 24px;
      overflow-y: auto;
      color: var(--omega-text-primary);
      flex: 1;
    }

    .modal-footer {
      padding: 14px 24px 20px;
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      border-top: 1px solid var(--omega-glass-border-subtle);

      &:empty {
        display: none;
      }
    }
  `]
})
export class GlassModalComponent {
  @Input() isOpen: boolean = false;
  @Input() title: string = '';
  @Input() maxWidth: string = '480px';
  @Input() closeOnBackdrop: boolean = true;

  @Output() close = new EventEmitter<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen) {
      this.close.emit();
    }
  }

  private isMouseDownOnBackdrop = false;

  @HostListener('window:mouseup')
  onWindowMouseUp(): void {
    this.isMouseDownOnBackdrop = false;
  }

  onBackdropMouseDown(event: MouseEvent): void {
    this.isMouseDownOnBackdrop = event.target === event.currentTarget;
  }

  onBackdropMouseUp(event: MouseEvent): void {
    if (this.closeOnBackdrop && this.isMouseDownOnBackdrop && event.target === event.currentTarget) {
      this.close.emit();
    }
    this.isMouseDownOnBackdrop = false;
  }
}
