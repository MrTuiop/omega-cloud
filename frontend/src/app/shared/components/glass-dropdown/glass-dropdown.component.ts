import {
  Component,
  Input,
  Output,
  EventEmitter,
  forwardRef,
  ElementRef,
  HostListener,
  inject,
  signal,
  computed,
  ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface DropdownOption<T = any> {
  value: T;
  label: string;
  icon?: string;
  badge?: string;
  disabled?: boolean;
}

export type DropdownSize = 'sm' | 'md' | 'lg';
export type DropdownVariant = 'default' | 'subtle';

@Component({
  selector: 'app-glass-dropdown',
  standalone: true,
  imports: [CommonModule],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => GlassDropdownComponent),
      multi: true
    }
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="glass-dropdown"
      [class.glass-dropdown--open]="isOpen()"
      [class.glass-dropdown--disabled]="disabled"
      [class]="'glass-dropdown--' + size + ' glass-dropdown--' + variant"
      tabindex="0"
      role="combobox"
      [attr.aria-expanded]="isOpen()"
      [attr.aria-disabled]="disabled"
      (keydown)="onKeydown($event)"
    >
      <!-- Trigger -->
      <button
        type="button"
        class="dropdown-trigger"
        [disabled]="disabled"
        (click)="toggleOpen($event)"
      >
        <span class="trigger-label" [class.trigger-label--placeholder]="!selectedOption()">
          {{ selectedLabel() }}
        </span>
        <span class="trigger-icon" [class.trigger-icon--rotated]="isOpen()">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </span>
      </button>

      <!-- Dropdown Menu -->
      @if (isOpen()) {
        <div class="dropdown-menu" role="listbox">
          @for (opt of normalizedOptions(); track opt.value; let idx = $index) {
            <div
              class="dropdown-item"
              role="option"
              [attr.aria-selected]="opt.value === currentValue()"
              [class.dropdown-item--selected]="opt.value === currentValue()"
              [class.dropdown-item--focused]="highlightedIndex() === idx"
              [class.dropdown-item--disabled]="opt.disabled"
              (click)="selectOption(opt, $event)"
              (mouseenter)="highlightedIndex.set(idx)"
            >
              @if (opt.icon) {
                <span class="item-icon" [innerHTML]="opt.icon"></span>
              }
              <span class="item-label">{{ opt.label }}</span>
              @if (opt.badge) {
                <span class="item-badge">{{ opt.badge }}</span>
              }
              @if (opt.value === currentValue()) {
                <span class="item-check">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                    <polyline points="20 6 9 17 4 12"></polyline>
                  </svg>
                </span>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    :host {
      display: inline-block;
      vertical-align: middle;
      outline: none;
    }

    .glass-dropdown {
      position: relative;
      display: inline-flex;
      width: 100%;
      min-width: 120px;
      user-select: none;
      outline: none;

      &--disabled {
        opacity: 0.5;
        pointer-events: none;
      }
    }

    .dropdown-trigger {
      display: inline-flex;
      align-items: center;
      justify-content: space-between;
      width: 100%;
      gap: 8px;
      font-weight: 500;
      color: var(--omega-text-primary);
      text-align: left;
      border-radius: var(--omega-radius-md);
      transition: all 0.2s var(--omega-ease-smooth);
      cursor: pointer;
      outline: none;

      // Sizes
      .glass-dropdown--sm & {
        height: 32px;
        padding: 0 10px;
        font-size: 12px;
      }

      .glass-dropdown--md & {
        height: 38px;
        padding: 0 14px;
        font-size: 13px;
      }

      .glass-dropdown--lg & {
        height: 44px;
        padding: 0 16px;
        font-size: 14px;
      }

      // Variants
      .glass-dropdown--default & {
        @include glass-surface;
        border: 1px solid var(--omega-glass-border);

        &:hover:not(:disabled) {
          background: var(--omega-glass-bg-hover);
          border-color: rgba(255, 255, 255, 0.35);
        }
      }

      .glass-dropdown--subtle & {
        background: var(--omega-glass-bg-subtle);
        border: 1px solid var(--omega-glass-border-subtle);

        &:hover:not(:disabled) {
          background: var(--omega-glass-bg-hover);
        }
      }

      &:focus,
      .glass-dropdown--open & {
        border-color: var(--omega-accent);
        box-shadow: 0 0 0 3px var(--omega-accent-subtle);
      }
    }

    .trigger-label {
      flex: 1;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;

      &--placeholder {
        color: var(--omega-text-muted);
      }
    }

    .trigger-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-secondary);
      transition: transform 0.2s var(--omega-ease-smooth);
      flex-shrink: 0;

      &--rotated {
        transform: rotate(180deg);
      }
    }

    .dropdown-menu {
      @include glass-elevated;
      position: absolute;
      top: calc(100% + 4px);
      left: 0;
      right: 0;
      z-index: 1000;
      min-width: 140px;
      max-height: 260px;
      overflow-y: auto;
      padding: 6px;
      border-radius: var(--omega-radius-md);
      box-shadow: var(--omega-shadow-dropdown);
      animation: appleScaleIn 0.16s var(--omega-ease-spring);
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .dropdown-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 12px;
      border-radius: var(--omega-radius-sm);
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);
      cursor: pointer;
      transition: background 0.15s, color 0.15s;

      &:hover,
      &--focused {
        background: var(--omega-accent);
        color: #ffffff;

        .item-icon,
        .item-check {
          color: #ffffff;
        }

        .item-badge {
          background: rgba(255, 255, 255, 0.2);
          color: #ffffff;
        }
      }

      &--selected {
        background: var(--omega-accent-subtle);
        color: var(--omega-accent);
        font-weight: 600;
      }

      &--disabled {
        opacity: 0.4;
        cursor: not-allowed;
        pointer-events: none;
      }
    }

    .item-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 16px;
      height: 16px;
      color: var(--omega-text-secondary);
      flex-shrink: 0;
    }

    .item-label {
      flex: 1;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .item-badge {
      font-size: 11px;
      padding: 2px 6px;
      border-radius: 4px;
      background: var(--omega-glass-bg-sunken);
      color: var(--omega-text-muted);
    }

    .item-check {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-accent);
      flex-shrink: 0;
    }
  `]
})
export class GlassDropdownComponent implements ControlValueAccessor {
  private readonly elementRef = inject(ElementRef);

  @Input() set options(val: Array<DropdownOption | string | number>) {
    if (!val) {
      this._rawOptions.set([]);
      return;
    }
    const normalized: DropdownOption[] = val.map(item => {
      if (typeof item === 'object' && item !== null && 'value' in item) {
        return item as DropdownOption;
      }
      return {
        value: item,
        label: String(item)
      };
    });
    this._rawOptions.set(normalized);
  }

  @Input() placeholder: string = 'Select...';
  @Input() size: DropdownSize = 'md';
  @Input() variant: DropdownVariant = 'default';
  @Input() disabled: boolean = false;

  @Input() set value(val: any) {
    if (this._currentValue() !== val) {
      this._currentValue.set(val);
    }
  }

  @Output() valueChange = new EventEmitter<any>();

  private readonly _rawOptions = signal<DropdownOption[]>([]);
  readonly normalizedOptions = computed(() => this._rawOptions());

  private readonly _currentValue = signal<any>(null);
  readonly currentValue = computed(() => this._currentValue());

  readonly isOpen = signal<boolean>(false);
  readonly highlightedIndex = signal<number>(-1);

  readonly selectedOption = computed(() => {
    const val = this._currentValue();
    return this.normalizedOptions().find(o => o.value === val) || null;
  });

  readonly selectedLabel = computed(() => {
    const sel = this.selectedOption();
    if (sel) return sel.label;
    return this.placeholder;
  });

  private onChange: (value: any) => void = () => {};
  private onTouched: () => void = () => {};

  toggleOpen(event?: MouseEvent): void {
    if (event) {
      event.stopPropagation();
    }
    if (this.disabled) return;
    const nextState = !this.isOpen();
    this.isOpen.set(nextState);
    if (nextState) {
      // Find current selected index to highlight
      const curVal = this._currentValue();
      const idx = this.normalizedOptions().findIndex(o => o.value === curVal);
      this.highlightedIndex.set(idx >= 0 ? idx : 0);
    } else {
      this.onTouched();
    }
  }

  selectOption(opt: DropdownOption, event?: MouseEvent): void {
    if (event) {
      event.stopPropagation();
    }
    if (opt.disabled || this.disabled) return;
    this._currentValue.set(opt.value);
    this.onChange(opt.value);
    this.valueChange.emit(opt.value);
    this.isOpen.set(false);
    this.onTouched();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.elementRef.nativeElement.contains(event.target)) {
      this.isOpen.set(false);
      this.onTouched();
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (this.disabled) return;

    if (event.key === 'Escape') {
      if (this.isOpen()) {
        this.isOpen.set(false);
        this.onTouched();
        event.preventDefault();
      }
      return;
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      if (!this.isOpen()) {
        this.toggleOpen();
        return;
      }
      const opts = this.normalizedOptions();
      if (opts.length === 0) return;
      let nextIdx = this.highlightedIndex() + 1;
      while (nextIdx < opts.length && opts[nextIdx].disabled) {
        nextIdx++;
      }
      if (nextIdx < opts.length) {
        this.highlightedIndex.set(nextIdx);
      }
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      if (!this.isOpen()) {
        this.toggleOpen();
        return;
      }
      const opts = this.normalizedOptions();
      if (opts.length === 0) return;
      let prevIdx = this.highlightedIndex() - 1;
      while (prevIdx >= 0 && opts[prevIdx].disabled) {
        prevIdx--;
      }
      if (prevIdx >= 0) {
        this.highlightedIndex.set(prevIdx);
      }
      return;
    }

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (!this.isOpen()) {
        this.toggleOpen();
      } else {
        const idx = this.highlightedIndex();
        const opts = this.normalizedOptions();
        if (idx >= 0 && idx < opts.length && !opts[idx].disabled) {
          this.selectOption(opts[idx]);
        }
      }
    }
  }

  // ControlValueAccessor methods
  writeValue(value: any): void {
    this._currentValue.set(value);
  }

  registerOnChange(fn: (value: any) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }
}
