import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AuthStore } from '../../core/auth/auth.store';
import { ThemeService } from '../../core/services/theme.service';
import { I18nService, SUPPORTED_LANGUAGES, AppLanguage } from '../../core/services/i18n.service';
import { ToastService } from '../../core/services/toast.service';
import { ResumableUploaderService } from '../../core/services/resumable-uploader.service';
import { FileItem } from '../../core/models/file.models';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../shared/pipes/relative-time.pipe';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { GlassButtonComponent } from '../../shared/components/glass-button/glass-button.component';
import { GlassCardComponent } from '../../shared/components/glass-card/glass-card.component';
import { GlassModalComponent } from '../../shared/components/glass-modal/glass-modal.component';
import { GlassDropdownComponent, DropdownOption } from '../../shared/components/glass-dropdown/glass-dropdown.component';
import { UploadQueueModalComponent } from '../drive/upload-queue-modal/upload-queue-modal.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    FormsModule,
    FileSizePipe,
    RelativeTimePipe,
    TranslatePipe,
    GlassButtonComponent,
    GlassCardComponent,
    GlassModalComponent,
    GlassDropdownComponent,
    UploadQueueModalComponent
  ],
  template: `
    <div class="dashboard-layout">
      <!-- Mobile Backdrop -->
      @if (isMobileMenuOpen()) {
        <div class="mobile-drawer-backdrop" (click)="isMobileMenuOpen.set(false)"></div>
      }

      <!-- Sidebar -->
      <aside class="sidebar" [class.sidebar--open]="isMobileMenuOpen()">
        <div class="sidebar-brand">
          <div class="brand-icon">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2">
              <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
            </svg>
          </div>
          <span class="brand-text">Omega Cloud</span>
          <button class="mobile-close-btn" (click)="isMobileMenuOpen.set(false)">✕</button>
        </div>

        <nav class="sidebar-nav">
          <a routerLink="/dashboard" class="nav-item nav-item--active">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7"></rect>
              <rect x="14" y="3" width="7" height="7"></rect>
              <rect x="14" y="14" width="7" height="7"></rect>
              <rect x="3" y="14" width="7" height="7"></rect>
            </svg>
            <span>{{ 'nav.dashboard' | translate }}</span>
          </a>

          <a routerLink="/drive" class="nav-item">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
            </svg>
            <span>{{ 'nav.drive' | translate }}</span>
          </a>

          <a routerLink="/shared" class="nav-item">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="18" cy="5" r="3"></circle>
              <circle cx="6" cy="12" r="3"></circle>
              <circle cx="18" cy="19" r="3"></circle>
              <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
              <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
            </svg>
            <span>{{ 'nav.shared' | translate }}</span>
          </a>

          @if (authStore.isAdmin()) {
            <a routerLink="/admin" class="nav-item nav-item--admin">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
              </svg>
              <span>{{ 'nav.admin' | translate }}</span>
            </a>
          }
        </nav>

        <div class="sidebar-footer">
          <!-- Quota Indicator -->
          <div class="quota-card">
            <div class="quota-header">
              <span class="quota-title">{{ 'dashboard.storageStatus' | translate }}</span>
              <span class="quota-percent">{{ authStore.quotaPercent() }}%</span>
            </div>
            <div class="quota-bar-track">
              <div
                class="quota-bar-fill"
                [style.width.%]="authStore.quotaPercent()"
                [class.quota-bar-fill--warning]="authStore.quotaPercent() > 80"
                [class.quota-bar-fill--danger]="authStore.quotaPercent() > 95"
              ></div>
            </div>
            <div class="quota-text">
              {{ (authStore.currentUser()?.usedQuotaBytes || 0) | fileSize }} {{ 'dashboard.usedOf' | translate }}
              {{ (authStore.currentUser()?.allocatedQuotaBytes || 0) | fileSize }}
            </div>
          </div>

          <!-- User Strip -->
          <div class="user-strip">
            <div class="user-capsule">
              <div class="user-avatar">{{ userInitials() }}</div>
              <div class="user-info">
                <span class="username">{{ authStore.currentUser()?.username || 'User' }}</span>
                <span class="role-badge">{{ authStore.currentUser()?.role || 'User' }}</span>
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

      <!-- Main Content -->
      <main class="main-content">
        <!-- Top App Bar -->
        <header class="app-bar">
          <div class="bar-left">
            <button class="hamburger-btn" (click)="isMobileMenuOpen.set(true)">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="3" y1="12" x2="21" y2="12"></line>
                <line x1="3" y1="6" x2="21" y2="6"></line>
                <line x1="3" y1="18" x2="21" y2="18"></line>
              </svg>
            </button>
            <div>
              <h1 class="page-title">{{ 'dashboard.title' | translate }}</h1>
              <p class="page-subtitle">{{ 'dashboard.subtitle' | translate }}</p>
            </div>
          </div>

          <div class="bar-right">
            <!-- Language Selector -->
            <div class="lang-selector">
              <app-glass-dropdown
                size="sm"
                variant="subtle"
                [options]="languageOptions"
                [value]="i18n.currentLang()"
                (valueChange)="i18n.setLanguage($event)"
              ></app-glass-dropdown>
            </div>
          </div>
        </header>

        <!-- Dashboard Content Grid -->
        <div class="dashboard-grid">
          <!-- Top Row: Storage Status & Quick Actions -->
          <div class="grid-row-two">
            <!-- Storage Status Widget -->
            <app-glass-card [elevated]="true" padding="24px">
              <div class="widget-header">
                <div class="widget-icon widget-icon--storage">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                    <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                    <line x1="12" y1="22.08" x2="12" y2="12"></line>
                  </svg>
                </div>
                <div>
                  <h3 class="widget-title">{{ 'dashboard.storageStatus' | translate }}</h3>
                  <span class="status-tag" [class.status-tag--warning]="authStore.quotaPercent() > 80" [class.status-tag--danger]="authStore.quotaPercent() > 95">
                    @if (authStore.quotaPercent() > 95) {
                      {{ 'dashboard.usageDanger' | translate }}
                    } @else if (authStore.quotaPercent() > 80) {
                      {{ 'dashboard.usageWarning' | translate }}
                    } @else {
                      {{ 'dashboard.usageHealthy' | translate }}
                    }
                  </span>
                </div>
              </div>

              <div class="storage-diagram">
                <div class="storage-radial">
                  <svg viewBox="0 0 36 36" class="circular-chart">
                    <path class="circle-bg"
                      d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831"
                    />
                    <path class="circle"
                      [style.stroke-dasharray]="authStore.quotaPercent() + ', 100'"
                      [class.circle--warning]="authStore.quotaPercent() > 80"
                      [class.circle--danger]="authStore.quotaPercent() > 95"
                      d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831"
                    />
                    <text x="18" y="20.35" class="percentage">{{ authStore.quotaPercent() }}%</text>
                  </svg>
                </div>

                <div class="storage-metrics">
                  <div class="metric-item">
                    <span class="metric-label">{{ 'dashboard.storageUsed' | translate }}</span>
                    <span class="metric-value">{{ (authStore.currentUser()?.usedQuotaBytes || 0) | fileSize }}</span>
                  </div>
                  <div class="metric-item">
                    <span class="metric-label">{{ 'dashboard.storageAvailable' | translate }}</span>
                    <span class="metric-value">{{ availableQuotaBytes() | fileSize }}</span>
                  </div>
                </div>
              </div>
            </app-glass-card>

            <!-- Quick Actions Panel -->
            <app-glass-card [elevated]="true" padding="24px">
              <div class="widget-header">
                <div class="widget-icon widget-icon--actions">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
                  </svg>
                </div>
                <div>
                  <h3 class="widget-title">{{ 'dashboard.quickActions' | translate }}</h3>
                </div>
              </div>

              <div class="actions-grid">
                <label class="action-card">
                  <input type="file" multiple class="sr-only" (change)="onFileInputChange($event)" />
                  <div class="action-icon action-icon--upload">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                      <polyline points="17 8 12 3 7 8"></polyline>
                      <line x1="12" y1="3" x2="12" y2="15"></line>
                    </svg>
                  </div>
                  <span class="action-title">{{ 'dashboard.uploadFiles' | translate }}</span>
                </label>

                <button class="action-card" (click)="openCreateFolderDialog()">
                  <div class="action-icon action-icon--folder">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                      <line x1="12" y1="11" x2="12" y2="17"></line>
                      <line x1="9" y1="14" x2="15" y2="14"></line>
                    </svg>
                  </div>
                  <span class="action-title">{{ 'dashboard.createFolder' | translate }}</span>
                </button>

                <a routerLink="/drive" class="action-card">
                  <div class="action-icon action-icon--drive">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                    </svg>
                  </div>
                  <span class="action-title">{{ 'dashboard.openDrive' | translate }}</span>
                </a>

                <a routerLink="/shared" class="action-card">
                  <div class="action-icon action-icon--shared">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <circle cx="18" cy="5" r="3"></circle>
                      <circle cx="6" cy="12" r="3"></circle>
                      <circle cx="18" cy="19" r="3"></circle>
                      <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                      <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
                    </svg>
                  </div>
                  <span class="action-title">{{ 'dashboard.openShared' | translate }}</span>
                </a>
              </div>
            </app-glass-card>
          </div>

          <!-- Bottom Row: Recent Files Widget -->
          <app-glass-card [elevated]="true" padding="24px">
            <div class="recent-header">
              <div class="widget-header">
                <div class="widget-icon widget-icon--recent">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"></circle>
                    <polyline points="12 6 12 12 16 14"></polyline>
                  </svg>
                </div>
                <h3 class="widget-title">{{ 'dashboard.recentFiles' | translate }}</h3>
              </div>

              <a routerLink="/drive" class="view-all-link">
                {{ 'dashboard.viewAll' | translate }} →
              </a>
            </div>

            @if (loadingRecent()) {
              <div class="recent-loading">{{ 'common.loading' | translate }}</div>
            } @else if (recentFiles().length === 0) {
              <div class="recent-empty">
                <p>{{ 'dashboard.noRecentFiles' | translate }}</p>
                <label class="upload-link-btn">
                  <input type="file" multiple class="sr-only" (change)="onFileInputChange($event)" />
                  {{ 'dashboard.uploadFiles' | translate }}
                </label>
              </div>
            } @else {
              <div class="recent-files-list">
                @for (file of recentFiles(); track file.id) {
                  <div class="recent-file-card">
                    <div class="file-icon-badge">
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                        <polyline points="14 2 14 8 20 8"></polyline>
                      </svg>
                    </div>

                    <div class="file-meta">
                      <span class="file-title" [title]="file.name">{{ file.name }}</span>
                      <span class="file-sub">{{ file.sizeBytes | fileSize }} • {{ file.updatedAt | relativeTime }}</span>
                    </div>

                    <div class="file-actions">
                      <a [href]="apiService.getDownloadUrl(file.id)" class="action-btn" [title]="'files.download' | translate">
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                          <polyline points="7 10 12 15 17 10"></polyline>
                          <line x1="12" y1="15" x2="12" y2="3"></line>
                        </svg>
                      </a>
                    </div>
                  </div>
                }
              </div>
            }
          </app-glass-card>
        </div>
      </main>

      <!-- Upload Queue Modal -->
      <app-upload-queue-modal
        [isOpen]="isUploadQueueOpen()"
        [inputFiles]="queuedFiles()"
        [destinationName]="'Root Drive'"
        [destinationFolderId]="null"
        [isShared]="false"
        (close)="isUploadQueueOpen.set(false)"
        (startUpload)="onStartUpload($event)"
      ></app-upload-queue-modal>

      <!-- New Folder Modal -->
      <app-glass-modal
        [isOpen]="isNewFolderModalOpen()"
        [title]="'dashboard.createFolder' | translate"
        maxWidth="440px"
        (close)="isNewFolderModalOpen.set(false)"
      >
        <div class="modal-form">
          <label for="newFolderName">{{ 'files.name' | translate }}</label>
          <input
            id="newFolderName"
            type="text"
            [(ngModel)]="newFolderName"
            placeholder="Folder name"
            (keydown.enter)="createFolder()"
          />
        </div>
        <div footer class="modal-actions">
          <app-glass-button variant="ghost" (click)="isNewFolderModalOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </app-glass-button>
          <app-glass-button variant="primary" [disabled]="!newFolderName.trim()" (click)="createFolder()">
            {{ 'common.save' | translate }}
          </app-glass-button>
        </div>
      </app-glass-modal>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .dashboard-layout {
      display: flex;
      height: 100vh;
      width: 100vw;
      overflow: hidden;
      background: var(--omega-bg-canvas);
      background-image: var(--omega-bg-gradient);
    }

    .sidebar {
      width: 260px;
      min-width: 260px;
      height: 100%;
      display: flex;
      flex-direction: column;
      border-right: 1px solid var(--omega-glass-border-subtle);
      background: var(--omega-glass-bg);
      backdrop-filter: blur(20px);
      -webkit-backdrop-filter: blur(20px);
      z-index: 50;
      transition: transform 0.25s var(--omega-ease-smooth);
    }

    .sidebar-brand {
      height: 64px;
      padding: 0 20px;
      display: flex;
      align-items: center;
      gap: 12px;
      border-bottom: 1px solid var(--omega-glass-border-subtle);

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

      .brand-text {
        font-size: 17px;
        font-weight: 700;
        color: var(--omega-text-primary);
        letter-spacing: -0.01em;
        flex: 1;
      }

      .mobile-close-btn {
        display: none;
        background: none;
        border: none;
        color: var(--omega-text-secondary);
        font-size: 18px;
        cursor: pointer;
        min-width: 44px;
        min-height: 44px;
      }
    }

    .sidebar-nav {
      flex: 1;
      padding: 16px 12px;
      display: flex;
      flex-direction: column;
      gap: 4px;
      overflow-y: auto;
    }

    .nav-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      min-height: 44px;
      border-radius: var(--omega-radius-md);
      font-size: 14px;
      font-weight: 500;
      color: var(--omega-text-secondary);
      text-decoration: none;
      transition: all 0.15s;

      &:hover {
        color: var(--omega-text-primary);
        background: var(--omega-glass-bg-hover);
      }

      &--active {
        background: var(--omega-accent-subtle);
        color: var(--omega-accent);
        font-weight: 600;
      }

      &--admin {
        margin-top: 12px;
        border-top: 1px solid var(--omega-glass-border-subtle);
        padding-top: 16px;
      }
    }

    .sidebar-footer {
      padding: 16px;
      border-top: 1px solid var(--omega-glass-border-subtle);
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .quota-card {
      @include glass-surface;
      padding: 12px;
      border-radius: var(--omega-radius-md);
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .quota-header {
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      font-weight: 600;
      color: var(--omega-text-secondary);
    }

    .quota-bar-track {
      height: 6px;
      border-radius: 3px;
      background: var(--omega-glass-bg-sunken);
      overflow: hidden;
    }

    .quota-bar-fill {
      height: 100%;
      background: var(--omega-accent);
      border-radius: 3px;
      transition: width 0.3s;

      &--warning { background: var(--omega-warning); }
      &--danger { background: var(--omega-danger); }
    }

    .quota-text {
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .user-strip {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }

    .user-capsule {
      display: flex;
      align-items: center;
      gap: 10px;
      min-width: 0;
    }

    .user-avatar {
      width: 34px;
      height: 34px;
      border-radius: 50%;
      background: var(--omega-accent-gradient);
      color: #ffffff;
      font-size: 12px;
      font-weight: 600;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .user-info {
      display: flex;
      flex-direction: column;
      min-width: 0;

      .username {
        font-size: 13px;
        font-weight: 600;
        color: var(--omega-text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .role-badge {
        font-size: 11px;
        color: var(--omega-text-muted);
      }
    }

    .strip-actions {
      display: flex;
      gap: 4px;
    }

    .strip-btn {
      width: 32px;
      height: 32px;
      min-height: 44px;
      min-width: 44px;
      border-radius: var(--omega-radius-sm);
      background: none;
      border: none;
      color: var(--omega-text-secondary);
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;

      &:hover {
        color: var(--omega-text-primary);
        background: var(--omega-glass-bg-hover);
      }
    }

    .main-content {
      flex: 1;
      height: 100%;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
    }

    .app-bar {
      height: 72px;
      padding: 0 28px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
      background: var(--omega-glass-bg-subtle);
      backdrop-filter: blur(16px);
      -webkit-backdrop-filter: blur(16px);
    }

    .bar-left {
      display: flex;
      align-items: center;
      gap: 16px;

      .hamburger-btn {
        display: none;
        background: none;
        border: none;
        color: var(--omega-text-primary);
        cursor: pointer;
        min-width: 44px;
        min-height: 44px;
      }
    }

    .page-title {
      font-size: 20px;
      font-weight: 700;
      color: var(--omega-text-primary);
      margin: 0;
    }

    .page-subtitle {
      font-size: 12px;
      color: var(--omega-text-muted);
      margin: 2px 0 0 0;
    }

    .lang-selector {
      display: flex;
      align-items: center;
      gap: 6px;
      @include glass-surface;
      padding: 6px 10px;
      min-height: 44px;
      border-radius: var(--omega-radius-md);
      color: var(--omega-text-secondary);

      select {
        background: none;
        border: none;
        color: var(--omega-text-primary);
        font-size: 13px;
        font-weight: 500;
        outline: none;
        cursor: pointer;

        option {
          background: var(--omega-bg-canvas);
          color: var(--omega-text-primary);
        }
      }
    }

    .dashboard-grid {
      padding: 28px;
      display: flex;
      flex-direction: column;
      gap: 24px;
      max-width: 1200px;
    }

    .grid-row-two {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 24px;
    }

    .widget-header {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 18px;
    }

    .widget-icon {
      width: 40px;
      height: 40px;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;

      &--storage {
        background: rgba(0, 113, 227, 0.15);
        color: var(--omega-accent);
      }

      &--actions {
        background: rgba(52, 199, 89, 0.15);
        color: #34c759;
      }

      &--recent {
        background: rgba(175, 82, 222, 0.15);
        color: #af52de;
      }
    }

    .widget-title {
      font-size: 16px;
      font-weight: 700;
      color: var(--omega-text-primary);
      margin: 0;
    }

    .status-tag {
      font-size: 11px;
      font-weight: 500;
      color: var(--omega-text-muted);

      &--warning { color: var(--omega-warning); }
      &--danger { color: var(--omega-danger); }
    }

    .storage-diagram {
      display: flex;
      align-items: center;
      gap: 24px;
    }

    .storage-radial {
      width: 100px;
      height: 100px;
      flex-shrink: 0;
    }

    .circular-chart {
      display: block;
      margin: 0 auto;
      max-width: 100%;
      max-height: 100%;
    }

    .circle-bg {
      fill: none;
      stroke: var(--omega-glass-bg-sunken);
      stroke-width: 3.8;
    }

    .circle {
      fill: none;
      stroke-width: 3.8;
      stroke-linecap: round;
      stroke: var(--omega-accent);
      transition: stroke-dasharray 0.5s ease;

      &--warning { stroke: var(--omega-warning); }
      &--danger { stroke: var(--omega-danger); }
    }

    .percentage {
      fill: var(--omega-text-primary);
      font-family: inherit;
      font-size: 8px;
      font-weight: 700;
      text-anchor: middle;
    }

    .storage-metrics {
      display: flex;
      flex-direction: column;
      gap: 12px;
      flex: 1;
    }

    .metric-item {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .metric-label {
      font-size: 12px;
      color: var(--omega-text-muted);
    }

    .metric-value {
      font-size: 17px;
      font-weight: 700;
      color: var(--omega-text-primary);
    }

    .actions-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .action-card {
      @include glass-surface;
      padding: 14px;
      border-radius: var(--omega-radius-md);
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 8px;
      text-decoration: none;
      cursor: pointer;
      min-height: 72px;
      transition: all 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
        transform: translateY(-2px);
      }

      .action-icon {
        width: 38px;
        height: 38px;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;

        &--upload { background: rgba(0, 113, 227, 0.15); color: var(--omega-accent); }
        &--folder { background: rgba(255, 159, 10, 0.15); color: #ff9f0a; }
        &--drive { background: rgba(52, 199, 89, 0.15); color: #34c759; }
        &--shared { background: rgba(175, 82, 222, 0.15); color: #af52de; }
      }

      .action-title {
        font-size: 12px;
        font-weight: 600;
        color: var(--omega-text-primary);
        text-align: center;
      }
    }

    .recent-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 16px;

      .view-all-link {
        font-size: 13px;
        font-weight: 600;
        color: var(--omega-accent);
        text-decoration: none;

        &:hover { text-decoration: underline; }
      }
    }

    .recent-files-list {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: 12px;
    }

    .recent-file-card {
      @include glass-surface;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .file-icon-badge {
      color: var(--omega-accent);
      display: flex;
      align-items: center;
    }

    .file-meta {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;

      .file-title {
        font-size: 13px;
        font-weight: 600;
        color: var(--omega-text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .file-sub {
        font-size: 11px;
        color: var(--omega-text-muted);
      }
    }

    .action-btn {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-secondary);
      background: var(--omega-glass-bg-subtle);
      border: 1px solid var(--omega-glass-border-subtle);
      text-decoration: none;
      cursor: pointer;

      &:hover {
        color: var(--omega-accent);
        background: var(--omega-glass-bg-hover);
      }
    }

    .recent-empty, .recent-loading {
      text-align: center;
      padding: 36px 16px;
      color: var(--omega-text-muted);
      font-size: 13px;
    }

    .upload-link-btn {
      display: inline-block;
      margin-top: 10px;
      color: var(--omega-accent);
      font-weight: 600;
      cursor: pointer;
      text-decoration: underline;
    }

    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      border: 0;
    }

    .modal-form {
      display: flex;
      flex-direction: column;
      gap: 8px;

      label {
        font-size: 13px;
        color: var(--omega-text-secondary);
      }

      input {
        width: 100%;
        padding: 10px 14px;
        border-radius: var(--omega-radius-md);
        background: var(--omega-glass-bg-sunken);
        border: 1px solid var(--omega-glass-border-subtle);
        color: var(--omega-text-primary);
        font-size: 14px;
        outline: none;

        &:focus {
          border-color: var(--omega-accent);
        }
      }
    }

    .modal-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      width: 100%;
    }

    /* Mobile Drawer */
    @media (max-width: 768px) {
      .sidebar {
        position: fixed;
        top: 0;
        left: 0;
        bottom: 0;
        transform: translateX(-100%);
        box-shadow: 0 0 30px rgba(0,0,0,0.4);

        &--open {
          transform: translateX(0);
        }
      }

      .sidebar-brand .mobile-close-btn {
        display: block;
      }

      .mobile-drawer-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.45);
        backdrop-filter: blur(8px);
        z-index: 49;
      }

      .bar-left .hamburger-btn {
        display: block;
      }

      .grid-row-two {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class DashboardComponent implements OnInit {
  readonly apiService = inject(ApiService);
  readonly authStore = inject(AuthStore);
  readonly themeService = inject(ThemeService);
  readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly uploader = inject(ResumableUploaderService);

  readonly languageOptions: DropdownOption[] = SUPPORTED_LANGUAGES.map(l => ({
    value: l.code,
    label: l.shortLabel
  }));

  readonly recentFiles = signal<FileItem[]>([]);
  readonly loadingRecent = signal<boolean>(false);
  readonly isMobileMenuOpen = signal<boolean>(false);

  // Upload queue modal
  readonly isUploadQueueOpen = signal<boolean>(false);
  readonly queuedFiles = signal<File[]>([]);

  // Create folder modal
  readonly isNewFolderModalOpen = signal<boolean>(false);
  newFolderName = '';

  readonly userInitials = computed(() => {
    const name = this.authStore.currentUser()?.username || 'U';
    return name.substring(0, 2).toUpperCase();
  });

  readonly availableQuotaBytes = computed(() => {
    const user = this.authStore.currentUser();
    if (!user) return 0;
    return Math.max(0, user.allocatedQuotaBytes - user.usedQuotaBytes);
  });

  ngOnInit(): void {
    this.loadRecentFiles();
  }

  loadRecentFiles(): void {
    this.loadingRecent.set(true);
    this.apiService.getRecentFiles(8).subscribe({
      next: files => {
        this.recentFiles.set(files);
        this.loadingRecent.set(false);
      },
      error: () => {
        // Fallback: try root folder files
        this.apiService.getRootFolderContent().subscribe({
          next: content => {
            this.recentFiles.set(content.files.slice(0, 8));
            this.loadingRecent.set(false);
          },
          error: () => this.loadingRecent.set(false)
        });
      }
    });
  }

  onFileInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.queuedFiles.set(Array.from(input.files));
      this.isUploadQueueOpen.set(true);
      input.value = '';
    }
  }

  onStartUpload(event: { files: File[]; resolutions?: Record<string, 'Replace' | 'Rename' | 'Skip'> }): void {
    if (!event.files || event.files.length === 0) return;

    for (const file of event.files) {
      const res = event.resolutions?.[file.name];
      this.uploader.enqueue([file], null, 'Root Drive', res, false);
    }

    this.toast.info(`Started uploading ${event.files.length} file(s)`);
  }

  openCreateFolderDialog(): void {
    this.newFolderName = '';
    this.isNewFolderModalOpen.set(true);
  }

  createFolder(): void {
    const name = this.newFolderName.trim();
    if (!name) return;

    this.apiService.createFolder({ name }).subscribe({
      next: folder => {
        this.isNewFolderModalOpen.set(false);
        this.toast.success(`Folder "${folder.name}" created`);
      },
      error: err => {
        this.toast.error(err?.error?.message || 'Failed to create folder');
      }
    });
  }

  logout(): void {
    this.authStore.logout();
    this.router.navigate(['/login']);
  }
}
