import { Component, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthStore } from '../../../core/auth/auth.store';
import { ThemeService } from '../../../core/services/theme.service';
import { ApiService } from '../../../core/services/api.service';
import { I18nService } from '../../../core/services/i18n.service';
import { TranslatePipe } from '../../../shared/pipes/translate.pipe';
import { GlassDropdownComponent } from '../../../shared/components/glass-dropdown/glass-dropdown.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    TranslatePipe,
    GlassDropdownComponent
  ],
  template: `
    <div class="admin-shell">
      <!-- Admin Sidebar -->
      <aside class="admin-sidebar">
        <!-- Brand Header -->
        <div class="sidebar-brand">
          <div class="brand-icon">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2">
              <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
            </svg>
          </div>
          <div class="brand-info">
            <span class="brand-title">Omega Cloud</span>
            <span class="brand-subtitle">{{ 'admin.nav.console' | translate }}</span>
          </div>
        </div>

        <!-- Navigation Menu -->
        <nav class="sidebar-nav">
          <a
            routerLink="/admin/users"
            routerLinkActive="nav-item--active"
            class="nav-item"
            [title]="'admin.nav.users' | translate"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
              <circle cx="9" cy="7" r="4"></circle>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"></path>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
            </svg>
            <span>{{ 'admin.nav.users' | translate }}</span>
          </a>

          <a
            routerLink="/admin/nodes"
            routerLinkActive="nav-item--active"
            class="nav-item"
            [title]="'admin.nav.nodes' | translate"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
              <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
              <line x1="6" y1="6" x2="6.01" y2="6"></line>
              <line x1="6" y1="18" x2="6.01" y2="18"></line>
            </svg>
            <span>{{ 'admin.nav.nodes' | translate }}</span>
          </a>

          <a
            routerLink="/admin/audit"
            routerLinkActive="nav-item--active"
            class="nav-item"
            [title]="'admin.nav.audit' | translate"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
              <polyline points="14 2 14 8 20 8"></polyline>
              <line x1="16" y1="13" x2="8" y2="13"></line>
              <line x1="16" y1="17" x2="8" y2="17"></line>
              <polyline points="10 9 9 9 8 9"></polyline>
            </svg>
            <span>{{ 'admin.nav.audit' | translate }}</span>
          </a>

          <a
            routerLink="/admin/health"
            routerLinkActive="nav-item--active"
            class="nav-item"
            [title]="'admin.nav.health' | translate"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>
            </svg>
            <span>{{ 'admin.nav.health' | translate }}</span>
          </a>
        </nav>

        <!-- Sidebar Footer -->
        <div class="sidebar-footer">
          <!-- Language selector -->
          <div class="lang-selector">
            <app-glass-dropdown
              size="sm"
              variant="subtle"
              [options]="languageOptions"
              [value]="i18n.currentLang()"
              (valueChange)="i18n.setLanguage($event)"
            ></app-glass-dropdown>
          </div>

          <!-- Back to Drive Link -->
          <a routerLink="/drive" class="drive-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            <span>{{ 'admin.nav.backToDrive' | translate }}</span>
          </a>

          <!-- User profile strip -->
          <div class="user-strip">
            <div class="user-capsule">
              <div class="user-avatar">
                {{ userInitials() }}
              </div>
              <div class="user-info">
                <span class="username">{{ authStore.currentUser()?.username || 'Admin' }}</span>
                <span class="role-badge">{{ 'admin.nav.superAdmin' | translate }}</span>
              </div>
            </div>

            <div class="strip-actions">
              <button class="strip-btn" (click)="themeService.toggleTheme()" [title]="'nav.switchTheme' | translate">
                @if (themeService.isDark()) {
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="5"></circle>
                    <line x1="12" y1="1" x2="12" y2="3"></line>
                    <line x1="12" y1="21" x2="12" y2="23"></line>
                  </svg>
                } @else {
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
                  </svg>
                }
              </button>

              <button class="strip-btn" (click)="logout()" [title]="'nav.logout' | translate">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path>
                  <polyline points="16 17 21 12 16 7"></polyline>
                  <line x1="21" y1="12" x2="9" y2="12"></line>
                </svg>
              </button>
            </div>
          </div>
        </div>
      </aside>

      <!-- Main Stage -->
      <main class="admin-stage">
        <router-outlet></router-outlet>
      </main>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

    .admin-shell {
      position: relative;
      width: 100vw;
      height: 100vh;
      display: flex;
      overflow: hidden;
      background: var(--omega-bg-canvas);
      background-image: var(--omega-bg-gradient);
    }

    .admin-sidebar {
      width: 260px;
      height: 100%;
      @include glass-surface;
      border-left: none;
      border-top: none;
      border-bottom: none;
      display: flex;
      flex-direction: column;
      flex-shrink: 0;
      z-index: 10;
    }

    .sidebar-brand {
      height: 64px;
      padding: 0 20px;
      display: flex;
      align-items: center;
      gap: 12px;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
      box-sizing: border-box;
    }

    .brand-icon {
      width: 36px;
      height: 36px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-accent-gradient);
      color: #ffffff;
      display: flex;
      align-items: center;
      justify-content: center;
      box-shadow: 0 4px 12px 0 rgba(0, 113, 227, 0.4);
      flex-shrink: 0;
    }

    .brand-info {
      display: flex;
      flex-direction: column;
      gap: 1px;
    }

    .brand-title {
      font-size: 16px;
      font-weight: 700;
      letter-spacing: -0.01em;
      color: var(--omega-text-primary);
    }

    .brand-subtitle {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--omega-accent);
    }

    .sidebar-nav {
      padding: 12px 14px;
      display: flex;
      flex-direction: column;
      gap: 5px;
      flex: 1;
      overflow-y: auto;
    }

    .nav-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 11px 14px;
      border-radius: var(--omega-radius-md);
      font-size: 14px;
      font-weight: 500;
      color: var(--omega-text-secondary);
      text-decoration: none;
      transition: all 0.2s var(--omega-ease-smooth);

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }

      &--active {
        background: var(--omega-accent-subtle);
        color: var(--omega-accent);
        font-weight: 600;
      }
    }

    .sidebar-footer {
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 14px;
      border-top: 1px solid var(--omega-glass-border-subtle);
    }

    .drive-link {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 9px 12px;
      border-radius: var(--omega-radius-md);
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-muted);
      text-decoration: none;
      @include glass-sunken;
      transition: all 0.2s;

      &:hover {
        color: var(--omega-accent);
        background: var(--omega-glass-bg-hover);
      }
    }

    .user-strip {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .user-capsule {
      display: flex;
      align-items: center;
      gap: 10px;
      min-width: 0;
    }

    .user-avatar {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: linear-gradient(135deg, #bf5af2, #5e5ce6);
      color: #ffffff;
      font-size: 12px;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .user-info {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .username {
      font-size: 13px;
      font-weight: 600;
      color: var(--omega-text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .role-badge {
      font-size: 10px;
      color: #bf5af2;
      font-weight: 600;
      text-transform: uppercase;
    }

    .strip-actions {
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .strip-btn {
      width: 28px;
      height: 28px;
      border-radius: var(--omega-radius-sm);
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);
      transition: all 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }
    }

    .lang-selector {
      margin-bottom: 12px;
    }

    .admin-stage {
      flex: 1;
      height: 100%;
      display: flex;
      flex-direction: column;
      min-width: 0;
      overflow-y: auto;
    }
  `]
})
export class AdminLayoutComponent {
  readonly authStore = inject(AuthStore);
  readonly themeService = inject(ThemeService);
  readonly i18n = inject(I18nService);
  private readonly apiService = inject(ApiService);
  private readonly router = inject(Router);

  readonly languageOptions = [
    { value: 'ru', label: 'Русский (RU)' },
    { value: 'en', label: 'English (EN)' },
    { value: 'zh', label: '中文 (ZH)' }
  ];

  readonly userInitials = computed(() => {
    const user = this.authStore.currentUser();
    if (!user || !user.username) return 'A';
    return user.username.slice(0, 2).toUpperCase();
  });

  logout(): void {
    this.apiService.logout().subscribe({
      next: () => {
        this.authStore.logout();
        this.router.navigate(['/login']);
      },
      error: () => {
        this.authStore.logout();
        this.router.navigate(['/login']);
      }
    });
  }
}
