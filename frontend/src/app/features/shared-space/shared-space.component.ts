import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AuthStore } from '../../core/auth/auth.store';
import { ThemeService } from '../../core/services/theme.service';
import { I18nService, SUPPORTED_LANGUAGES } from '../../core/services/i18n.service';
import { ToastService } from '../../core/services/toast.service';
import { ResumableUploaderService } from '../../core/services/resumable-uploader.service';
import { FolderContent, FolderItem, FileItem } from '../../core/models/file.models';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../shared/pipes/relative-time.pipe';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';
import { GlassButtonComponent } from '../../shared/components/glass-button/glass-button.component';
import { GlassModalComponent } from '../../shared/components/glass-modal/glass-modal.component';
import { GlassDropdownComponent, DropdownOption } from '../../shared/components/glass-dropdown/glass-dropdown.component';
import { BreadcrumbsComponent } from '../../shared/components/breadcrumbs/breadcrumbs.component';
import { UploadQueueModalComponent } from '../drive/upload-queue-modal/upload-queue-modal.component';

@Component({
  selector: 'app-shared-space',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    FormsModule,
    FileSizePipe,
    RelativeTimePipe,
    TranslatePipe,
    GlassButtonComponent,
    GlassModalComponent,
    GlassDropdownComponent,
    BreadcrumbsComponent,
    UploadQueueModalComponent
  ],
  template: `
    <div class="shared-space-layout" (dragover)="onWindowDragOver($event)" (dragleave)="onWindowDragLeave($event)" (drop)="onWindowDrop($event)">
      <!-- Mobile Drawer Backdrop -->
      @if (isMobileMenuOpen()) {
        <div class="mobile-drawer-backdrop" (click)="isMobileMenuOpen.set(false)"></div>
      }

      <!-- Drag Over Overlay (Authenticated only) -->
      @if (isDraggingOver() && authStore.isAuthenticated()) {
        <div class="drag-drop-overlay">
          <div class="drop-banner">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="17 8 12 3 7 8"></polyline>
              <line x1="12" y1="3" x2="12" y2="15"></line>
            </svg>
            <h3>{{ 'upload.dropHint' | translate }}</h3>
            <p>{{ 'upload.dropSub' | translate }} {{ currentFolderName() }}</p>
          </div>
        </div>
      }

      <!-- Sidebar (Only shown when authenticated or collapsible) -->
      @if (authStore.isAuthenticated()) {
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
            <a routerLink="/dashboard" class="nav-item">
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

            <a routerLink="/shared" class="nav-item nav-item--active">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="18" cy="5" r="3"></circle>
                <circle cx="6" cy="12" r="3"></circle>
                <circle cx="18" cy="19" r="3"></circle>
                <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
              </svg>
              <span>{{ 'nav.shared' | translate }}</span>
            </a>

            <a routerLink="/drive" [queryParams]="{ tab: 'favorites' }" class="nav-item" [title]="'nav.favorites' | translate">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
              </svg>
              <span>{{ 'nav.favorites' | translate }}</span>
            </a>

            <a routerLink="/drive" [queryParams]="{ tab: 'trash' }" class="nav-item" [title]="'nav.trash' | translate">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
              <span>{{ 'nav.trash' | translate }}</span>
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
      }

      <!-- Main Stage -->
      <main class="main-stage">
        <!-- Top Navigation Header -->
        <header class="stage-header">
          <div class="header-left">
            @if (authStore.isAuthenticated()) {
              <button class="hamburger-btn" (click)="isMobileMenuOpen.set(true)">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="3" y1="12" x2="21" y2="12"></line>
                  <line x1="3" y1="6" x2="21" y2="6"></line>
                  <line x1="3" y1="18" x2="21" y2="18"></line>
                </svg>
              </button>
            } @else {
              <div class="brand-badge-anon">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2">
                  <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
                </svg>
                <span>Omega Cloud</span>
              </div>
            }

            <app-breadcrumbs
              [breadcrumbs]="folderContent()?.breadcrumbs || []"
              (navigate)="onNavigateFolder($event)"
            ></app-breadcrumbs>
          </div>

          <div class="header-right">
            <!-- Search bar with dropdown scope selector -->
            <div class="search-box">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
              <input
                type="text"
                [placeholder]="'search.placeholder' | translate"
                [ngModel]="searchQuery()"
                (ngModelChange)="searchQuery.set($event)"
              />
              <div class="scope-dropdown">
                <app-glass-dropdown
                  size="sm"
                  variant="subtle"
                  [options]="searchScopeOptions()"
                  [value]="searchScope()"
                  (valueChange)="searchScope.set($event)"
                ></app-glass-dropdown>
              </div>
              @if (searchQuery()) {
                <button class="clear-search" (click)="searchQuery.set('')">✕</button>
              }
            </div>

            <!-- Display Mode Toggle -->
            <div class="view-toggle">
              <button
                class="toggle-btn"
                [class.toggle-btn--active]="displayMode() === 'grid'"
                (click)="displayMode.set('grid')"
                title="Grid View"
              >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="7" height="7"></rect>
                  <rect x="14" y="3" width="7" height="7"></rect>
                  <rect x="14" y="14" width="7" height="7"></rect>
                  <rect x="3" y="14" width="7" height="7"></rect>
                </svg>
              </button>
              <button
                class="toggle-btn"
                [class.toggle-btn--active]="displayMode() === 'list'"
                (click)="displayMode.set('list')"
                title="List View"
              >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="8" y1="6" x2="21" y2="6"></line>
                  <line x1="8" y1="12" x2="21" y2="12"></line>
                  <line x1="8" y1="18" x2="21" y2="18"></line>
                  <line x1="3" y1="6" x2="3.01" y2="6"></line>
                  <line x1="3" y1="12" x2="3.01" y2="12"></line>
                  <line x1="3" y1="18" x2="3.01" y2="18"></line>
                </svg>
              </button>
            </div>

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

            <!-- Actions: Upload / New Folder if authenticated, Login if anonymous -->
            @if (authStore.isAuthenticated()) {
              <app-glass-button variant="primary" (click)="fileInput.click()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="12" y1="5" x2="12" y2="19"></line>
                  <line x1="5" y1="12" x2="19" y2="12"></line>
                </svg>
                <span>{{ 'shared.uploadShared' | translate }}</span>
              </app-glass-button>
              <input #fileInput type="file" multiple class="sr-only" (change)="onFileInputChange($event)" />

              <app-glass-button variant="secondary" (click)="openCreateFolderModal()">
                <span>{{ 'shared.createSharedFolder' | translate }}</span>
              </app-glass-button>
            } @else {
              <a routerLink="/login" class="login-anon-btn">
                {{ 'shared.loginToUpload' | translate }}
              </a>
            }
          </div>
        </header>

        <!-- Anonymous Info Banner -->
        @if (!authStore.isAuthenticated()) {
          <div class="anon-banner">
            <div class="anon-badge">{{ 'shared.anonymousBadge' | translate }}</div>
            <p>{{ 'shared.anonymousNotice' | translate }}</p>
          </div>
        }

        <!-- Content Area -->
        <div class="stage-content">
          @if (loading()) {
            <div class="loading-state">{{ 'common.loading' | translate }}</div>
          } @else if (filteredFolders().length === 0 && filteredFiles().length === 0) {
            <div class="empty-state">
              <div class="empty-icon">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                  <circle cx="18" cy="5" r="3"></circle>
                  <circle cx="6" cy="12" r="3"></circle>
                  <circle cx="18" cy="19" r="3"></circle>
                  <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                  <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
                </svg>
              </div>
              <h3>{{ 'shared.empty' | translate }}</h3>
              @if (searchQuery()) {
                <p>{{ 'search.tryAdjusting' | translate }}</p>
              } @else if (authStore.isAuthenticated()) {
                <p>{{ 'files.dragToUpload' | translate }}</p>
              }
            </div>
          } @else {
            <!-- Folders Section -->
            @if (filteredFolders().length > 0) {
              <div class="section-title">Folders</div>
              <div class="folders-grid">
                @for (folder of filteredFolders(); track folder.id) {
                  <div class="folder-card" (dblclick)="openFolder(folder.id)">
                    <div class="folder-icon">
                      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                      </svg>
                    </div>
                    <div class="folder-name" [title]="folder.name">{{ folder.name }}</div>
                    @if (canMutate(folder.userId)) {
                      <button class="delete-icon-btn" (click)="confirmDeleteFolder(folder)" [title]="'common.delete' | translate">
                        ✕
                      </button>
                    }
                  </div>
                }
              </div>
            }

            <!-- Files Section: Grid or List -->
            @if (filteredFiles().length > 0) {
              <div class="section-title">Files</div>
              @if (displayMode() === 'grid') {
                <div class="files-grid">
                  @for (file of filteredFiles(); track file.id) {
                    <div class="file-card">
                      <div class="file-card-preview">
                        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                          <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                          <polyline points="14 2 14 8 20 8"></polyline>
                        </svg>
                      </div>

                      <div class="file-card-meta">
                        <span class="file-card-name" [title]="file.name">{{ file.name }}</span>
                        <div class="file-card-sub">
                          <span>{{ file.sizeBytes | fileSize }}</span>
                          @if (file.userId) {
                            <span class="owner-tag" [title]="'shared.owner' | translate">
                              {{ isCurrentUserOwner(file.userId) ? 'You' : 'Author' }}
                            </span>
                          }
                        </div>
                      </div>

                      <div class="file-card-actions">
                        <a [href]="apiService.getSharedDownloadUrl(file.id)" class="card-action-btn" [title]="'files.download' | translate">
                          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                            <polyline points="7 10 12 15 17 10"></polyline>
                            <line x1="12" y1="15" x2="12" y2="3"></line>
                          </svg>
                        </a>
                        @if (canMutate(file.userId)) {
                          <button class="card-action-btn card-action-btn--danger" (click)="confirmDeleteFile(file)" [title]="'common.delete' | translate">
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <polyline points="3 6 5 6 21 6"></polyline>
                              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                            </svg>
                          </button>
                        }
                      </div>
                    </div>
                  }
                </div>
              } @else {
                <!-- List View -->
                <div class="files-list">
                  <div class="list-header">
                    <span class="col-name">{{ 'files.name' | translate }}</span>
                    <span class="col-size">{{ 'files.size' | translate }}</span>
                    <span class="col-owner">{{ 'shared.owner' | translate }}</span>
                    <span class="col-date">{{ 'files.modified' | translate }}</span>
                    <span class="col-actions">{{ 'files.actions' | translate }}</span>
                  </div>

                  @for (file of filteredFiles(); track file.id) {
                    <div class="list-row">
                      <div class="col-name file-row-name">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                          <polyline points="14 2 14 8 20 8"></polyline>
                        </svg>
                        <span [title]="file.name">{{ file.name }}</span>
                      </div>
                      <div class="col-size">{{ file.sizeBytes | fileSize }}</div>
                      <div class="col-owner">
                        <span class="owner-pill">{{ isCurrentUserOwner(file.userId) ? 'You' : 'Author' }}</span>
                      </div>
                      <div class="col-date">{{ file.updatedAt | relativeTime }}</div>
                      <div class="col-actions">
                        <a [href]="apiService.getSharedDownloadUrl(file.id)" class="table-btn" [title]="'files.download' | translate">
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                            <polyline points="7 10 12 15 17 10"></polyline>
                            <line x1="12" y1="15" x2="12" y2="3"></line>
                          </svg>
                        </a>
                        @if (canMutate(file.userId)) {
                          <button class="table-btn table-btn--danger" (click)="confirmDeleteFile(file)" [title]="'common.delete' | translate">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <polyline points="3 6 5 6 21 6"></polyline>
                              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                            </svg>
                          </button>
                        }
                      </div>
                    </div>
                  }
                </div>
              }
            }
          }
        </div>
      </main>

      <!-- Upload Queue Modal -->
      <app-upload-queue-modal
        [isOpen]="isUploadQueueOpen()"
        [inputFiles]="queuedFiles()"
        [destinationName]="currentFolderName()"
        [destinationFolderId]="currentFolderId()"
        [isShared]="true"
        (close)="isUploadQueueOpen.set(false)"
        (startUpload)="onStartSharedUpload($event)"
      ></app-upload-queue-modal>

      <!-- Create Shared Folder Modal -->
      <app-glass-modal
        [isOpen]="isCreateFolderOpen()"
        [title]="'shared.createSharedFolder' | translate"
        maxWidth="440px"
        (close)="isCreateFolderOpen.set(false)"
      >
        <div class="modal-form">
          <label for="sharedFolderName">{{ 'files.name' | translate }}</label>
          <input
            id="sharedFolderName"
            type="text"
            [(ngModel)]="newFolderName"
            placeholder="Folder name"
            (keydown.enter)="createSharedFolder()"
          />
        </div>
        <div footer class="modal-actions">
          <app-glass-button variant="ghost" (click)="isCreateFolderOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </app-glass-button>
          <app-glass-button variant="primary" [disabled]="!newFolderName.trim()" (click)="createSharedFolder()">
            {{ 'common.save' | translate }}
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Delete Confirm Modal -->
      <app-glass-modal
        [isOpen]="isDeleteModalOpen()"
        [title]="'shared.deleteConfirmTitle' | translate"
        maxWidth="440px"
        (close)="isDeleteModalOpen.set(false)"
      >
        <p class="delete-msg">{{ 'shared.deleteConfirmMsg' | translate }}</p>
        <div footer class="modal-actions">
          <app-glass-button variant="ghost" (click)="isDeleteModalOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </app-glass-button>
          <app-glass-button variant="danger" (click)="deleteConfirmed()">
            {{ 'common.delete' | translate }}
          </app-glass-button>
        </div>
      </app-glass-modal>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .shared-space-layout {
      display: flex;
      height: 100vh;
      width: 100vw;
      overflow: hidden;
      background: var(--omega-bg-canvas);
      background-image: var(--omega-bg-gradient);
      position: relative;
    }

    .sidebar {
      width: 260px;
      min-width: 260px;
      height: 100%;
      display: flex;
      flex-direction: column;
      border-right: 1px solid var(--omega-glass-border-subtle);
      background: var(--omega-glass-bg);
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
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

    .main-stage {
      flex: 1;
      height: 100%;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    .stage-header {
      height: 64px;
      padding: 0 24px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
      background: var(--omega-glass-bg-subtle);
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
      flex-shrink: 0;
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 16px;
      flex: 1;
      min-width: 0;

      .hamburger-btn {
        display: none;
        background: none;
        border: none;
        color: var(--omega-text-primary);
        cursor: pointer;
        min-width: 44px;
        min-height: 44px;
      }

      .brand-badge-anon {
        display: flex;
        align-items: center;
        gap: 8px;
        font-weight: 700;
        color: var(--omega-accent);
        font-size: 16px;
      }
    }

    .header-right {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 6px;
      @include glass-surface;
      padding: 0 10px;
      height: 38px;
      border-radius: var(--omega-radius-pill);

      input {
        background: none;
        border: none;
        outline: none;
        color: var(--omega-text-primary);
        font-size: 13px;
        width: 140px;
      }

      .scope-dropdown {
        min-width: 140px;
      }

      .clear-search {
        background: none;
        border: none;
        color: var(--omega-text-muted);
        cursor: pointer;
        padding: 0 4px;
      }
    }

    .view-toggle {
      display: flex;
      background: var(--omega-glass-bg-sunken);
      padding: 2px;
      border-radius: var(--omega-radius-sm);

      .toggle-btn {
        width: 32px;
        height: 32px;
        min-height: 44px;
        min-width: 44px;
        border: none;
        background: none;
        color: var(--omega-text-muted);
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        border-radius: 4px;

        &--active {
          background: var(--omega-glass-bg);
          color: var(--omega-accent);
        }
      }
    }

    .lang-selector select {
      background: var(--omega-glass-bg-subtle);
      border: 1px solid var(--omega-glass-border-subtle);
      color: var(--omega-text-primary);
      padding: 6px 8px;
      min-height: 44px;
      border-radius: 6px;
      font-size: 12px;
      outline: none;
      cursor: pointer;

      option {
        background: var(--omega-bg-canvas);
      }
    }

    .login-anon-btn {
      display: flex;
      align-items: center;
      padding: 8px 16px;
      min-height: 44px;
      background: var(--omega-accent);
      color: #ffffff;
      border-radius: var(--omega-radius-pill);
      font-size: 13px;
      font-weight: 600;
      text-decoration: none;
      transition: opacity 0.15s;

      &:hover { opacity: 0.9; }
    }

    .anon-banner {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 24px;
      background: rgba(0, 113, 227, 0.08);
      border-bottom: 1px solid rgba(0, 113, 227, 0.2);
      font-size: 13px;
      color: var(--omega-text-secondary);

      .anon-badge {
        padding: 2px 8px;
        border-radius: 4px;
        background: var(--omega-accent);
        color: #ffffff;
        font-size: 11px;
        font-weight: 600;
      }
    }

    .stage-content {
      flex: 1;
      padding: 24px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .section-title {
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--omega-text-muted);
    }

    .folders-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
      gap: 12px;
    }

    .folder-card {
      @include glass-surface;
      content-visibility: auto;
      contain-intrinsic-size: auto 54px;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      position: relative;
      user-select: none;

      &:hover {
        background: var(--omega-glass-bg-hover);
      }

      .folder-icon {
        color: var(--omega-accent);
        display: flex;
      }

      .folder-name {
        flex: 1;
        font-size: 13px;
        font-weight: 600;
        color: var(--omega-text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .delete-icon-btn {
        background: none;
        border: none;
        color: var(--omega-text-muted);
        cursor: pointer;
        padding: 2px 6px;
        border-radius: 4px;
        font-size: 12px;

        &:hover {
          color: var(--omega-danger);
          background: var(--omega-danger-subtle);
        }
      }
    }

    .files-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 14px;
    }

    .file-card {
      @include glass-surface;
      content-visibility: auto;
      contain-intrinsic-size: auto 120px;
      border-radius: var(--omega-radius-md);
      overflow: hidden;
      display: flex;
      flex-direction: column;

      .file-card-preview {
        height: 110px;
        background: var(--omega-glass-bg-sunken);
        display: flex;
        align-items: center;
        justify-content: center;
        color: var(--omega-accent);
      }

      .file-card-meta {
        padding: 12px;
        display: flex;
        flex-direction: column;
        gap: 4px;

        .file-card-name {
          font-size: 13px;
          font-weight: 600;
          color: var(--omega-text-primary);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .file-card-sub {
          display: flex;
          align-items: center;
          justify-content: space-between;
          font-size: 11px;
          color: var(--omega-text-muted);
        }
      }

      .file-card-actions {
        display: flex;
        justify-content: flex-end;
        gap: 6px;
        padding: 0 12px 12px 12px;
      }

      .card-action-btn {
        width: 32px;
        height: 32px;
        min-height: 44px;
        min-width: 44px;
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

        &--danger:hover {
          color: var(--omega-danger);
          background: var(--omega-danger-subtle);
        }
      }
    }

    .files-list {
      @include glass-surface;
      border-radius: var(--omega-radius-md);
      overflow: hidden;

      .list-header, .list-row {
        display: grid;
        grid-template-columns: 2fr 100px 100px 130px 100px;
        align-items: center;
        padding: 10px 16px;
        font-size: 13px;
      }

      .list-header {
        background: var(--omega-glass-bg-sunken);
        font-size: 11px;
        font-weight: 700;
        text-transform: uppercase;
        color: var(--omega-text-muted);
        letter-spacing: 0.05em;
      }

      .list-row {
        border-top: 1px solid var(--omega-glass-border-subtle);

        &:hover {
          background: var(--omega-glass-bg-hover);
        }
      }

      .file-row-name {
        display: flex;
        align-items: center;
        gap: 8px;
        font-weight: 500;
        color: var(--omega-text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;

        svg {
          color: var(--omega-accent);
          flex-shrink: 0;
        }
      }

      .table-btn {
        width: 28px;
        height: 28px;
        min-height: 44px;
        min-width: 44px;
        border-radius: 4px;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        background: none;
        border: none;
        color: var(--omega-text-secondary);
        cursor: pointer;
        text-decoration: none;

        &:hover {
          color: var(--omega-accent);
        }

        &--danger:hover {
          color: var(--omega-danger);
        }
      }
    }

    .owner-tag, .owner-pill {
      font-size: 10px;
      font-weight: 600;
      padding: 2px 6px;
      border-radius: 4px;
      background: var(--omega-glass-bg-sunken);
      color: var(--omega-text-secondary);
    }

    .empty-state, .loading-state {
      text-align: center;
      padding: 64px 20px;
      color: var(--omega-text-muted);
    }

    .drag-drop-overlay {
      position: absolute;
      inset: 0;
      background: rgba(0, 113, 227, 0.25);
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
      z-index: 100;
      display: flex;
      align-items: center;
      justify-content: center;

      .drop-banner {
        @include glass-elevated;
        padding: 40px;
        border-radius: var(--omega-radius-lg);
        text-align: center;
        color: var(--omega-accent);
      }
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

      label { font-size: 13px; color: var(--omega-text-secondary); }
      input {
        width: 100%;
        padding: 10px 14px;
        border-radius: var(--omega-radius-md);
        background: var(--omega-glass-bg-sunken);
        border: 1px solid var(--omega-glass-border-subtle);
        color: var(--omega-text-primary);
        font-size: 14px;
        outline: none;

        &:focus { border-color: var(--omega-accent); }
      }
    }

    .modal-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      width: 100%;
    }

    .delete-msg {
      font-size: 13px;
      color: var(--omega-text-secondary);
      line-height: 1.5;
    }

    @media (max-width: 768px), (pointer: coarse) {
      .sidebar {
        position: fixed;
        top: 0;
        left: 0;
        bottom: 0;
        transform: translateX(-100%);
        box-shadow: 0 0 30px rgba(0,0,0,0.4);
        will-change: transform;

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
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
        z-index: 49;
      }

      .stage-header {
        padding: 0 14px;
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .stage-content {
        padding: 14px;
        -webkit-overflow-scrolling: touch;
        overscroll-behavior-y: contain;
      }

      .folders-grid {
        grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
        gap: 8px;
      }

      .files-grid {
        grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
        gap: 10px;
      }

      .folder-card,
      .file-card,
      .list-wrapper {
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .header-left .hamburger-btn {
        display: block;
      }

      .search-box input {
        width: 90px;
      }

      .files-list .list-header, .files-list .list-row {
        grid-template-columns: 2fr 80px 80px;

        .col-owner, .col-date {
          display: none;
        }
      }
    }
  `]
})
export class SharedSpaceComponent implements OnInit {
  readonly apiService = inject(ApiService);
  readonly authStore = inject(AuthStore);
  readonly themeService = inject(ThemeService);
  readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly uploader = inject(ResumableUploaderService);

  readonly searchScopeOptions = computed<DropdownOption[]>(() => [
    { value: 'everywhere', label: this.i18n.translate('search.scopeEverywhere') },
    { value: 'current', label: this.i18n.translate('search.scopeCurrentFolder') }
  ]);

  readonly languageOptions: DropdownOption[] = SUPPORTED_LANGUAGES.map(l => ({
    value: l.code,
    label: l.shortLabel
  }));

  readonly folderContent = signal<FolderContent | null>(null);
  readonly loading = signal<boolean>(false);
  readonly displayMode = signal<'grid' | 'list'>('grid');
  readonly currentFolderId = signal<string | null>(null);
  readonly searchQuery = signal<string>('');
  readonly searchScope = signal<'everywhere' | 'current'>('everywhere');
  readonly isMobileMenuOpen = signal<boolean>(false);
  readonly isDraggingOver = signal<boolean>(false);

  // Upload Queue Modal
  readonly isUploadQueueOpen = signal<boolean>(false);
  readonly queuedFiles = signal<File[]>([]);

  // Create Shared Folder Modal
  readonly isCreateFolderOpen = signal<boolean>(false);
  newFolderName = '';

  // Delete Modal
  readonly isDeleteModalOpen = signal<boolean>(false);
  itemToDelete: { type: 'file' | 'folder'; id: string; name: string } | null = null;

  private dragCounter = 0;

  readonly currentFolderName = computed(() => {
    return this.folderContent()?.currentFolder?.name || 'Shared Space';
  });

  readonly userInitials = computed(() => {
    const name = this.authStore.currentUser()?.username || 'U';
    return name.substring(0, 2).toUpperCase();
  });

  readonly filteredFolders = computed(() => {
    const folders = this.folderContent()?.folders || [];
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return folders;
    return folders.filter(f => f.name.toLowerCase().includes(q));
  });

  readonly filteredFiles = computed(() => {
    const files = this.folderContent()?.files || [];
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return files;
    return files.filter(f => f.name.toLowerCase().includes(q));
  });

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const folderId = params['folder'] || null;
      this.currentFolderId.set(folderId);
      this.loadFolderContent(folderId);
    });
  }

  loadFolderContent(folderId: string | null): void {
    this.loading.set(true);
    this.apiService.getSharedFolders(folderId).subscribe({
      next: content => {
        this.folderContent.set(content);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load shared content.');
      }
    });
  }

  openFolder(folderId: string): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { folder: folderId },
      queryParamsHandling: 'merge'
    });
  }

  onNavigateFolder(folderId: string | null): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { folder: folderId || null },
      queryParamsHandling: 'merge'
    });
  }

  canMutate(ownerId?: string): boolean {
    if (!this.authStore.isAuthenticated()) return false;
    if (this.authStore.isAdmin()) return true;
    return !!ownerId && ownerId === this.authStore.currentUser()?.id;
  }

  isCurrentUserOwner(ownerId?: string): boolean {
    return !!ownerId && ownerId === this.authStore.currentUser()?.id;
  }

  onWindowDragOver(event: DragEvent): void {
    if (!this.authStore.isAuthenticated()) return;
    event.preventDefault();
    if (this.dragCounter === 0) {
      this.isDraggingOver.set(true);
    }
    this.dragCounter++;
  }

  onWindowDragLeave(event: DragEvent): void {
    if (!this.authStore.isAuthenticated()) return;
    event.preventDefault();
    this.dragCounter--;
    if (this.dragCounter <= 0) {
      this.isDraggingOver.set(false);
      this.dragCounter = 0;
    }
  }

  onWindowDrop(event: DragEvent): void {
    if (!this.authStore.isAuthenticated()) return;
    event.preventDefault();
    this.isDraggingOver.set(false);
    this.dragCounter = 0;

    if (event.dataTransfer?.files && event.dataTransfer.files.length > 0) {
      this.queuedFiles.set(Array.from(event.dataTransfer.files));
      this.isUploadQueueOpen.set(true);
    }
  }

  onFileInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.queuedFiles.set(Array.from(input.files));
      this.isUploadQueueOpen.set(true);
      input.value = '';
    }
  }

  onStartSharedUpload(event: { files: File[]; resolutions?: Record<string, 'Replace' | 'Rename' | 'Skip'> }): void {
    if (!event.files || event.files.length === 0) return;

    for (const file of event.files) {
      const res = event.resolutions?.[file.name];
      this.uploader.enqueue([file], this.currentFolderId(), this.currentFolderName(), res, true);
    }

    this.toast.info(`Uploading ${event.files.length} shared file(s)`);
  }

  openCreateFolderModal(): void {
    this.newFolderName = '';
    this.isCreateFolderOpen.set(true);
  }

  createSharedFolder(): void {
    const name = this.newFolderName.trim();
    if (!name) return;

    this.apiService.createSharedFolder(name, this.currentFolderId()).subscribe({
      next: () => {
        this.isCreateFolderOpen.set(false);
        this.loadFolderContent(this.currentFolderId());
        this.toast.success(`Shared folder "${name}" created`);
      },
      error: err => {
        this.toast.error(err?.error?.message || 'Failed to create shared folder');
      }
    });
  }

  confirmDeleteFile(file: FileItem): void {
    this.itemToDelete = { type: 'file', id: file.id, name: file.name };
    this.isDeleteModalOpen.set(true);
  }

  confirmDeleteFolder(folder: FolderItem): void {
    this.itemToDelete = { type: 'folder', id: folder.id, name: folder.name };
    this.isDeleteModalOpen.set(true);
  }

  deleteConfirmed(): void {
    if (!this.itemToDelete) return;

    if (this.itemToDelete.type === 'file') {
      this.apiService.deleteSharedFile(this.itemToDelete.id).subscribe({
        next: () => {
          this.isDeleteModalOpen.set(false);
          this.loadFolderContent(this.currentFolderId());
          this.toast.success(`Deleted ${this.itemToDelete?.name}`);
          this.itemToDelete = null;
        },
        error: err => {
          this.toast.error(err?.error?.message || 'Failed to delete file');
        }
      });
    } else {
      this.apiService.deleteSharedFolder(this.itemToDelete.id).subscribe({
        next: () => {
          this.isDeleteModalOpen.set(false);
          this.loadFolderContent(this.currentFolderId());
          this.toast.success(`Deleted folder ${this.itemToDelete?.name}`);
          this.itemToDelete = null;
        },
        error: err => {
          this.toast.error(err?.error?.message || 'Failed to delete folder');
        }
      });
    }
  }

  logout(): void {
    this.authStore.logout();
    this.router.navigate(['/login']);
  }
}
