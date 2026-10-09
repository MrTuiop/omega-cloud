import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-glass-card',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div
      class="glass-card"
      [class.glass-card--interactive]="interactive"
      [class.glass-card--elevated]="elevated"
      [class.glass-card--active]="active"
      [style.padding]="padding"
    >
      <ng-content></ng-content>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .glass-card {
      @include glass-surface;
      border-radius: var(--omega-radius-lg);
      transition: all 0.25s var(--omega-ease-spring);
      position: relative;
      overflow: hidden;

      &--elevated {
        @include glass-elevated;
      }

      &--interactive {
        cursor: pointer;

        &:hover {
          background: var(--omega-glass-bg-hover);
          transform: translateY(-2px);
          box-shadow: var(--omega-shadow-lg), var(--omega-glass-highlight);
          border-color: rgba(255, 255, 255, 0.45);
        }

        &:active {
          transform: translateY(0) scale(0.99);
        }
      }

      &--active {
        border-color: var(--omega-accent);
        box-shadow: 0 0 0 2px var(--omega-accent), var(--omega-shadow-md);
      }
    }
  `]
})
export class GlassCardComponent {
  @Input() interactive: boolean = false;
  @Input() elevated: boolean = false;
  @Input() active: boolean = false;
  @Input() padding: string = '20px';
}
