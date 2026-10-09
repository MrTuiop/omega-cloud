import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Breadcrumb } from '../../../core/models/file.models';

@Component({
  selector: 'app-breadcrumbs',
  standalone: true,
  imports: [CommonModule],
  template: `
    <nav class="breadcrumbs-nav" aria-label="Breadcrumb">
      <ol class="breadcrumbs-list">
        <li class="breadcrumb-item">
          <button class="breadcrumb-btn breadcrumb-root" (click)="navigate.emit(null)" title="Root Drive">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
              <polyline points="9 22 9 12 15 12 15 22"></polyline>
            </svg>
            <span>Drive</span>
          </button>
        </li>

        @for (crumb of breadcrumbs; track crumb.id; let last = $last) {
          <li class="breadcrumb-separator">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <polyline points="9 18 15 12 9 6"></polyline>
            </svg>
          </li>
          <li class="breadcrumb-item" [class.breadcrumb-item--active]="last">
            @if (last) {
              <span class="breadcrumb-current">{{ crumb.name }}</span>
            } @else {
              <button class="breadcrumb-btn" (click)="navigate.emit(crumb.id)">
                {{ crumb.name }}
              </button>
            }
          </li>
        }
      </ol>
    </nav>
  `,
  styles: [`
    .breadcrumbs-nav {
      display: flex;
      align-items: center;
      overflow-x: auto;
      white-space: nowrap;
      scrollbar-width: none;

      &::-webkit-scrollbar {
        display: none;
      }
    }

    .breadcrumbs-list {
      display: flex;
      align-items: center;
      list-style: none;
      gap: 4px;
      padding: 0;
      margin: 0;
    }

    .breadcrumb-item {
      display: flex;
      align-items: center;
    }

    .breadcrumb-btn {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 8px;
      border-radius: var(--omega-radius-sm);
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-secondary);
      transition: all 0.15s ease;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }
    }

    .breadcrumb-separator {
      display: flex;
      align-items: center;
      color: var(--omega-text-muted);
      opacity: 0.6;
    }

    .breadcrumb-current {
      font-size: 13px;
      font-weight: 600;
      color: var(--omega-text-primary);
      padding: 4px 8px;
    }
  `]
})
export class BreadcrumbsComponent {
  @Input() breadcrumbs: Breadcrumb[] = [];
  @Output() navigate = new EventEmitter<string | null>();
}
