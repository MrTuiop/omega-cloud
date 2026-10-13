import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { GlassCardComponent } from '../../../shared/components/glass-card/glass-card.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { ThemeService } from '../../../core/services/theme.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, GlassCardComponent, GlassButtonComponent],
  template: `
    <div class="login-wrapper">
      <div class="ambient-glow ambient-glow--1"></div>
      <div class="ambient-glow ambient-glow--2"></div>

      <div class="theme-toggle-top">
        <button class="theme-btn" (click)="themeService.toggleTheme()" [title]="'Switch to ' + (themeService.isDark() ? 'Light' : 'Dark') + ' mode'">
          @if (themeService.isDark()) {
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="5"></circle>
              <line x1="12" y1="1" x2="12" y2="3"></line>
              <line x1="12" y1="21" x2="12" y2="23"></line>
              <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
              <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
              <line x1="1" y1="12" x2="3" y2="12"></line>
              <line x1="21" y1="12" x2="23" y2="12"></line>
              <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
              <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
            </svg>
          } @else {
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
            </svg>
          }
        </button>
      </div>

      <div class="login-container">
        <app-glass-card [elevated]="true" padding="36px 32px">
          <div class="brand-header">
            <div class="brand-logo">
              <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2">
                <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
              </svg>
            </div>
            <h1 class="brand-title">Omega Cloud</h1>
            <p class="brand-subtitle">High-performance private cloud storage</p>
          </div>

          @if (errorMessage()) {
            <div class="error-banner">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"></circle>
                <line x1="12" y1="8" x2="12" y2="12"></line>
                <line x1="12" y1="16" x2="12.01" y2="16"></line>
              </svg>
              <span>{{ errorMessage() }}</span>
            </div>
          }

          <form (ngSubmit)="onSubmit()" class="login-form">
            <div class="form-group">
              <label for="username">Username or Email</label>
              <input
                id="username"
                type="text"
                name="username"
                [(ngModel)]="username"
                placeholder="admin@omegacloud.local"
                required
                autocomplete="username"
              />
            </div>

            <div class="form-group">
              <label for="password">Password</label>
              <input
                id="password"
                type="password"
                name="password"
                [(ngModel)]="password"
                placeholder="••••••••••••"
                required
                autocomplete="current-password"
              />
            </div>

            <div class="form-actions">
              <app-glass-button
                type="submit"
                variant="primary"
                size="lg"
                [loading]="loading()"
                [disabled]="!username || !password"
              >
                Sign In
              </app-glass-button>
            </div>
          </form>

          <div class="shared-space-divider">
            <span>или</span>
          </div>

          <div class="public-actions">
            <app-glass-button
              type="button"
              variant="secondary"
              size="md"
              (btnClick)="goToShared()"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="18" cy="5" r="3"></circle>
                <circle cx="6" cy="12" r="3"></circle>
                <circle cx="18" cy="19" r="3"></circle>
                <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
              </svg>
              <span>Перейти в Общий раздел (без входа)</span>
            </app-glass-button>
          </div>
        </app-glass-card>
      </div>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .login-wrapper {
      position: relative;
      width: 100vw;
      height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
      background: var(--omega-bg-canvas);
      background-image: var(--omega-bg-gradient);
    }

    .theme-toggle-top {
      position: absolute;
      top: 24px;
      right: 28px;
      z-index: 10;
    }

    .theme-btn {
      width: 40px;
      height: 40px;
      border-radius: var(--omega-radius-pill);
      @include glass-surface;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-secondary);
      transition: all 0.2s var(--omega-ease-smooth);

      &:hover {
        color: var(--omega-text-primary);
        background: var(--omega-glass-bg-hover);
        transform: scale(1.05);
      }
    }

    .ambient-glow {
      position: absolute;
      border-radius: 50%;
      filter: blur(90px);
      pointer-events: none;
      opacity: 0.5;

      &--1 {
        width: 480px;
        height: 480px;
        background: radial-gradient(circle, rgba(0, 113, 227, 0.45) 0%, transparent 70%);
        top: 15%;
        left: 20%;
        animation: pulseGlow 10s ease-in-out infinite alternate;
      }

      &--2 {
        width: 420px;
        height: 420px;
        background: radial-gradient(circle, rgba(191, 90, 242, 0.35) 0%, transparent 70%);
        bottom: 15%;
        right: 20%;
        animation: pulseGlow 8s ease-in-out infinite alternate;
      }
    }

    .login-container {
      width: 100%;
      max-width: 430px;
      padding: 20px;
      position: relative;
      z-index: 2;
      animation: appleScaleIn 0.4s var(--omega-ease-spring);
    }

    .brand-header {
      text-align: center;
      margin-bottom: 28px;
    }

    .brand-logo {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 64px;
      height: 64px;
      border-radius: var(--omega-radius-lg);
      background: var(--omega-accent-gradient);
      color: #ffffff;
      box-shadow: 0 8px 24px 0 rgba(0, 113, 227, 0.4);
      margin-bottom: 16px;
    }

    .brand-title {
      font-size: 24px;
      font-weight: 700;
      letter-spacing: -0.02em;
      color: var(--omega-text-primary);
      margin-bottom: 6px;
    }

    .brand-subtitle {
      font-size: 14px;
      color: var(--omega-text-secondary);
    }

    .error-banner {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-danger-subtle);
      border: 1px solid rgba(255, 69, 58, 0.3);
      color: var(--omega-danger);
      font-size: 13px;
      font-weight: 500;
      margin-bottom: 20px;
      animation: appleScaleIn 0.2s var(--omega-ease-spring);
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;

      label {
        font-size: 13px;
        font-weight: 500;
        color: var(--omega-text-secondary);
      }

      input {
        width: 100%;
        height: 44px;
      }
    }

    .form-actions {
      margin-top: 10px;

      app-glass-button, button {
        width: 100%;
      }
    }

    .shared-space-divider {
      margin: 18px 0 14px;
      display: flex;
      align-items: center;
      text-align: center;
      color: var(--omega-text-muted);
      font-size: 12px;

      &::before, &::after {
        content: '';
        flex: 1;
        border-bottom: 1px solid var(--omega-glass-border-subtle);
      }

      span {
        padding: 0 10px;
      }
    }

    .public-actions {
      app-glass-button, button {
        width: 100%;
      }
    }

    .default-hint {
      margin-top: 24px;
      padding-top: 18px;
      border-top: 1px solid var(--omega-glass-border-subtle);
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      font-size: 12px;
      color: var(--omega-text-muted);

      code {
        font-family: var(--omega-font-mono);
        background: var(--omega-glass-bg-sunken);
        padding: 2px 6px;
        border-radius: 4px;
        color: var(--omega-text-primary);
      }

      .quick-fill-btn {
        color: var(--omega-accent);
        font-weight: 600;
        padding: 2px 6px;
        border-radius: 4px;
        transition: background 0.15s;

        &:hover {
          background: var(--omega-accent-subtle);
        }
      }
    }
  `]
})
export class LoginComponent {
  private readonly apiService = inject(ApiService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly themeService = inject(ThemeService);

  username = '';
  password = '';
  loading = signal<boolean>(false);
  errorMessage = signal<string>('');

  fillDefaults(): void {
    this.username = 'admin';
    this.password = 'OmegaAdmin2026!';
  }

  goToShared(): void {
    this.router.navigate(['/shared']);
  }

  onSubmit(): void {
    if (!this.username || !this.password) return;

    this.loading.set(true);
    this.errorMessage.set('');

    const deviceName = typeof navigator !== 'undefined' ? `${navigator.platform || 'Device'} (${navigator.userAgent.slice(0, 30)})` : 'Web Browser';

    this.apiService.login({
      usernameOrEmail: this.username,
      password: this.password,
      deviceName
    }).subscribe({
      next: res => {
        this.loading.set(false);
        this.authStore.setLoginResponse(res);
        const returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/drive';
        this.router.navigateByUrl(returnUrl);
      },
      error: err => {
        this.loading.set(false);
        this.errorMessage.set(err?.error?.message || 'Invalid username or password.');
      }
    });
  }
}
