import { Component, Input, Output, EventEmitter, HostListener, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface ContextMenuItem {
  id: string;
  label: string;
  icon?: string;
  danger?: boolean;
  disabled?: boolean;
  dividerBefore?: boolean;
}

@Component({
  selector: 'app-context-menu',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (isOpen) {
      <div
        class="context-menu"
        [style.left.px]="adjustedX"
        [style.top.px]="adjustedY"
        (click)="$event.stopPropagation()"
      >
        @for (item of items; track item.id) {
          @if (item.dividerBefore) {
            <div class="menu-divider"></div>
          }
          <button
            class="menu-item"
            [class.menu-item--danger]="item.danger"
            [disabled]="item.disabled"
            (click)="onSelect(item)"
          >
            @if (item.icon) {
              <span class="menu-icon" [innerHTML]="item.icon"></span>
            }
            <span class="menu-label">{{ item.label }}</span>
          </button>
        }
      </div>
    }
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .context-menu {
      @include glass-elevated;
      position: fixed;
      z-index: 10000;
      min-width: 190px;
      padding: 6px;
      border-radius: var(--omega-radius-md);
      box-shadow: var(--omega-shadow-dropdown);
      animation: appleScaleIn 0.18s var(--omega-ease-spring);
      display: flex;
      flex-direction: column;
    }

    .menu-item {
      display: flex;
      align-items: center;
      gap: 10px;
      width: 100%;
      padding: 8px 12px;
      border-radius: var(--omega-radius-sm);
      font-size: 13px;
      color: var(--omega-text-primary);
      text-align: left;
      transition: background 0.15s, color 0.15s;

      &:hover:not(:disabled) {
        background: var(--omega-accent);
        color: #ffffff;

        .menu-icon {
          color: #ffffff;
        }
      }

      &--danger {
        color: var(--omega-danger);

        &:hover:not(:disabled) {
          background: var(--omega-danger) !important;
          color: #ffffff !important;

          .menu-icon {
            color: #ffffff !important;
          }
        }
      }

      &:disabled {
        opacity: 0.4;
        cursor: not-allowed;
      }
    }

    .menu-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 16px;
      height: 16px;
      color: var(--omega-text-secondary);
      flex-shrink: 0;

      svg {
        width: 16px;
        height: 16px;
      }
    }

    .menu-label {
      flex: 1;
    }

    .menu-divider {
      height: 1px;
      margin: 4px 6px;
      background: var(--omega-glass-border-subtle);
    }

    @media (max-width: 768px), (pointer: coarse) {
      .context-menu {
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
        box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
      }
    }
  `]
})
export class ContextMenuComponent {
  private readonly elementRef = inject(ElementRef);

  @Input() isOpen: boolean = false;
  @Input() x: number = 0;
  @Input() y: number = 0;
  @Input() items: ContextMenuItem[] = [];

  @Output() itemClick = new EventEmitter<string>();
  @Output() close = new EventEmitter<void>();

  get adjustedX(): number {
    const menuWidth = 200;
    const maxX = typeof window !== 'undefined' ? window.innerWidth - menuWidth - 10 : this.x;
    return Math.min(this.x, Math.max(10, maxX));
  }

  get adjustedY(): number {
    const menuHeight = this.items.length * 34 + 20;
    const maxY = typeof window !== 'undefined' ? window.innerHeight - menuHeight - 10 : this.y;
    return Math.min(this.y, Math.max(10, maxY));
  }

  onSelect(item: ContextMenuItem): void {
    if (!item.disabled) {
      this.itemClick.emit(item.id);
      this.close.emit();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen && !this.elementRef.nativeElement.contains(event.target)) {
      this.close.emit();
    }
  }

  @HostListener('document:contextmenu', ['$event'])
  onDocumentContextMenu(event: MouseEvent): void {
    if (this.isOpen && !this.elementRef.nativeElement.contains(event.target)) {
      this.close.emit();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen) {
      this.close.emit();
    }
  }
}
