import { Component, Input, computed } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-progress-ring',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="progress-ring-container" [style.width.px]="size" [style.height.px]="size">
      <svg [attr.width]="size" [attr.height]="size" class="progress-ring-svg">
        <circle
          class="progress-ring-track"
          [attr.stroke-width]="strokeWidth"
          [attr.r]="radius"
          [attr.cx]="center"
          [attr.cy]="center"
        />
        <circle
          class="progress-ring-indicator"
          [attr.stroke-width]="strokeWidth"
          [attr.stroke-dasharray]="circumference"
          [attr.stroke-dashoffset]="dashOffset"
          [attr.stroke]="color"
          [attr.r]="radius"
          [attr.cx]="center"
          [attr.cy]="center"
        />
      </svg>
      @if (showText) {
        <span class="progress-ring-text" [style.font-size.px]="fontSize">
          {{ progress }}%
        </span>
      }
    </div>
  `,
  styles: [`
    .progress-ring-container {
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
    }

    .progress-ring-svg {
      transform: rotate(-90deg);
      overflow: visible;
    }

    .progress-ring-track {
      fill: transparent;
      stroke: rgba(120, 120, 128, 0.2);
    }

    .progress-ring-indicator {
      fill: transparent;
      stroke-linecap: round;
      transition: stroke-dashoffset 0.35s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .progress-ring-text {
      position: absolute;
      font-weight: 600;
      color: var(--omega-text-primary);
      user-select: none;
    }
  `]
})
export class ProgressRingComponent {
  @Input() size: number = 44;
  @Input() strokeWidth: number = 4;
  @Input() progress: number = 0; // 0 - 100
  @Input() color: string = 'var(--omega-accent)';
  @Input() showText: boolean = false;
  @Input() fontSize: number = 11;

  get center(): number {
    return this.size / 2;
  }

  get radius(): number {
    return (this.size - this.strokeWidth) / 2;
  }

  get circumference(): number {
    return 2 * Math.PI * this.radius;
  }

  get dashOffset(): number {
    const p = Math.max(0, Math.min(100, this.progress));
    return this.circumference - (p / 100) * this.circumference;
  }
}
