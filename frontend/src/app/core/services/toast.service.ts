import { Injectable, signal } from '@angular/core';
import { ToastMessage, ToastType } from '../models/toast.models';

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  readonly toasts = signal<ToastMessage[]>([]);
  private readonly timerMap = new Map<string, any>();

  show(message: string, type: ToastType = 'info', durationMs: number = 4000): string {
    const id = 'toast-' + Math.random().toString(36).substring(2, 9) + '-' + Date.now();
    const item: ToastMessage = {
      id,
      message,
      type,
      durationMs,
      timestamp: Date.now()
    };

    this.toasts.update(list => [...list, item]);

    if (durationMs > 0) {
      const timer = setTimeout(() => {
        this.dismiss(id);
      }, durationMs);
      this.timerMap.set(id, timer);
    }

    return id;
  }

  success(message: string, durationMs: number = 3500): string {
    return this.show(message, 'success', durationMs);
  }

  info(message: string, durationMs: number = 3500): string {
    return this.show(message, 'info', durationMs);
  }

  warning(message: string, durationMs: number = 4500): string {
    return this.show(message, 'warning', durationMs);
  }

  error(message: string, durationMs: number = 5500): string {
    return this.show(message, 'error', durationMs);
  }

  dismiss(id: string): void {
    const timer = this.timerMap.get(id);
    if (timer) {
      clearTimeout(timer);
      this.timerMap.delete(id);
    }
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  /**
   * Guaranteed removal of ALL active toast messages and cancellation of active timers
   */
  clearAll(): void {
    this.timerMap.forEach(timer => clearTimeout(timer));
    this.timerMap.clear();
    this.toasts.set([]);
  }
}
