import { Injectable, signal, effect, computed } from '@angular/core';

export type AppTheme = 'light' | 'dark' | 'system';

@Injectable({
  providedIn: 'root'
})
export class ThemeService {
  private readonly storageKey = 'omega_theme_preference';

  readonly theme = signal<AppTheme>(this.getInitialTheme());

  readonly isDark = computed(() => {
    const current = this.theme();
    if (current === 'system') {
      return typeof window !== 'undefined' && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    }
    return current === 'dark';
  });

  constructor() {
    effect(() => {
      const dark = this.isDark();
      if (typeof document !== 'undefined') {
        const root = document.documentElement;
        if (dark) {
          root.setAttribute('data-theme', 'dark');
        } else {
          root.removeAttribute('data-theme');
        }
      }
    });

    if (typeof window !== 'undefined' && window.matchMedia) {
      window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
        if (this.theme() === 'system') {
          // Trigger signal update
          this.theme.set('system');
        }
      });
    }
  }

  setTheme(newTheme: AppTheme): void {
    this.theme.set(newTheme);
    try {
      localStorage.setItem(this.storageKey, newTheme);
    } catch {
      // localStorage might be unavailable in restricted environments
    }
  }

  toggleTheme(): void {
    const next = this.isDark() ? 'light' : 'dark';
    this.setTheme(next);
  }

  private getInitialTheme(): AppTheme {
    try {
      const saved = localStorage.getItem(this.storageKey) as AppTheme | null;
      if (saved && (saved === 'light' || saved === 'dark' || saved === 'system')) {
        return saved;
      }
    } catch {
      // Ignore
    }
    return 'dark'; // Default to sleek dark glass
  }
}
