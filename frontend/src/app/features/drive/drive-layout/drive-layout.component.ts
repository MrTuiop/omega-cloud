import { Component, OnInit, OnDestroy, inject, signal, computed, ViewChild, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { ThemeService } from '../../../core/services/theme.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { ResumableUploaderService } from '../../../core/services/resumable-uploader.service';
import { ToastService } from '../../../core/services/toast.service';
import {
  Breadcrumb,
  FileItem,
  FolderContent,
  FolderItem
} from '../../../core/models/file.models';
import { BreadcrumbsComponent } from '../../../shared/components/breadcrumbs/breadcrumbs.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { ContextMenuComponent, ContextMenuItem } from '../../../shared/components/context-menu/context-menu.component';
import { FileGridComponent } from '../file-grid/file-grid.component';
import { FileListComponent } from '../file-list/file-list.component';
import { FileDetailsSheetComponent } from '../file-details-sheet/file-details-sheet.component';
import { UploadTrayComponent } from '../upload-tray/upload-tray.component';
import { TrashBinComponent } from '../trash-bin/trash-bin.component';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { TranslatePipe } from '../../../shared/pipes/translate.pipe';
import { I18nService, SUPPORTED_LANGUAGES } from '../../../core/services/i18n.service';
import { DragStateService } from '../../../core/services/drag-state.service';
import { GlassDropdownComponent, DropdownOption } from '../../../shared/components/glass-dropdown/glass-dropdown.component';
import { UploadQueueModalComponent } from '../upload-queue-modal/upload-queue-modal.component';
import { SearchScopeModalComponent, SearchScopeResult } from '../search-scope-modal/search-scope-modal.component';

type DriveViewTab = 'files' | 'favorites' | 'trash';
type DisplayMode = 'grid' | 'list';

@Component({
  selector: 'app-drive-layout',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    BreadcrumbsComponent,
    GlassButtonComponent,
    GlassModalComponent,
    GlassDropdownComponent,
    ContextMenuComponent,
    FileGridComponent,
    FileListComponent,
    FileDetailsSheetComponent,
    UploadTrayComponent,
    TrashBinComponent,
    FileSizePipe,
    TranslatePipe,
    UploadQueueModalComponent,
    SearchScopeModalComponent,
    RouterModule
  ],
  template: `
    <div
      class="app-shell"
      (dragenter)="onWindowDragEnter($event)"
      (dragover)="onWindowDragOver($event)"
      (dragleave)="onWindowDragLeave($event)"
      (drop)="onWindowDrop($event)"
    >
      <!-- Drop Overlay -->
      @if (isDraggingOver()) {
        <div class="drag-drop-overlay anim-scale-in">
          <div class="drop-card">
            <svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="17 8 12 3 7 8"></polyline>
              <line x1="12" y1="3" x2="12" y2="15"></line>
            </svg>
            <h3>Drop files to upload</h3>
            <p>Upload to {{ currentFolderName() }}</p>
          </div>
        </div>
      }

      <!-- Mobile Backdrop -->
      @if (isMobileMenuOpen()) {
        <div class="mobile-drawer-backdrop" (click)="isMobileMenuOpen.set(false)"></div>
      }

      <!-- Left Sidebar -->
      <aside class="sidebar" [class.sidebar--open]="isMobileMenuOpen()">
        <!-- Logo -->
        <div class="sidebar-brand">
          <div class="brand-icon">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2">
              <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
            </svg>
          </div>
          <span class="brand-text">Omega Cloud</span>
          <button class="mobile-close-btn" (click)="isMobileMenuOpen.set(false)">✕</button>
        </div>

        <!-- Navigation Menu -->
        <nav class="sidebar-nav">
          <a
            routerLink="/dashboard"
            class="nav-item"
            [title]="'nav.dashboard' | translate"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7"></rect>
              <rect x="14" y="3" width="7" height="7"></rect>
              <rect x="14" y="14" width="7" height="7"></rect>
              <rect x="3" y="14" width="7" height="7"></rect>
            </svg>
            <span>{{ 'nav.dashboard' | translate }}</span>
          </a>

          <button
            class="nav-item"
            [class.nav-item--active]="currentTab() === 'files'"
            (click)="selectTab('files')"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
            </svg>
            <span>{{ 'nav.drive' | translate }}</span>
          </button>

          <a
            routerLink="/shared"
            class="nav-item"
            [class.nav-item--dragover]="isSharedNavDragOver()"
            (dragover)="onSharedNavDragOver($event)"
            (dragleave)="onSharedNavDragLeave($event)"
            (drop)="onSharedNavDrop($event)"
            [title]="'nav.shared' | translate"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="18" cy="5" r="3"></circle>
              <circle cx="6" cy="12" r="3"></circle>
              <circle cx="18" cy="19" r="3"></circle>
              <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
              <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
            </svg>
            <span>{{ 'nav.shared' | translate }}</span>
          </a>

          <button
            class="nav-item"
            [class.nav-item--active]="currentTab() === 'favorites'"
            (click)="selectTab('favorites')"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
            </svg>
            <span>{{ 'nav.favorites' | translate }}</span>
          </button>

          <button
            class="nav-item"
            [class.nav-item--active]="currentTab() === 'trash'"
            (click)="selectTab('trash')"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
            <span>{{ 'nav.trash' | translate }}</span>
          </button>

          @if (authStore.isAdmin()) {
            <a
              routerLink="/admin"
              class="nav-item nav-item--admin"
              [title]="'nav.admin' | translate"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
              </svg>
              <span>{{ 'nav.admin' | translate }}</span>
            </a>
          }
        </nav>

        <!-- Sidebar Footer -->
        <div class="sidebar-footer">
          <!-- Quota Indicator -->
          <div class="quota-card">
            <div class="quota-header">
              <span class="quota-title">Storage</span>
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
              {{ (authStore.currentUser()?.usedQuotaBytes || 0) | fileSize }} of
              {{ (authStore.currentUser()?.allocatedQuotaBytes || 0) | fileSize }}
            </div>
          </div>

          <!-- Theme & User Actions -->
          <div class="user-strip">
            <div class="user-capsule">
              <div class="user-avatar">
                {{ userInitials() }}
              </div>
              <div class="user-info">
                <span class="username">{{ authStore.currentUser()?.username || 'User' }}</span>
                <span class="role-badge">{{ authStore.currentUser()?.role || 'User' }}</span>
              </div>
            </div>

            <div class="strip-actions">
              <button class="strip-btn" (click)="themeService.toggleTheme()" [title]="'Switch theme'">
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
              <button class="strip-btn" (click)="logout()" title="Logout">
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
      <main class="main-stage">
        <!-- Top Navigation & Action Header -->
        <header class="stage-header">
          <div class="header-left">
            <button class="hamburger-btn" (click)="isMobileMenuOpen.set(true)">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="3" y1="12" x2="21" y2="12"></line>
                <line x1="3" y1="6" x2="21" y2="6"></line>
                <line x1="3" y1="18" x2="21" y2="18"></line>
              </svg>
            </button>

            @if (currentTab() === 'files') {
              <app-breadcrumbs
                [breadcrumbs]="folderContent()?.breadcrumbs || []"
                (navigate)="onNavigateFolder($event)"
              ></app-breadcrumbs>
            } @else if (currentTab() === 'favorites') {
              <h2 class="view-title">{{ 'nav.favorites' | translate }}</h2>
            } @else {
              <h2 class="view-title">{{ 'nav.trash' | translate }}</h2>
            }
          </div>

          <div class="header-right">
            <!-- Search bar with Scope dropdown -->
            <div class="search-box">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
              <input
                type="text"
                [placeholder]="'search.placeholder' | translate"
                [ngModel]="searchQuery()"
                (ngModelChange)="onSearchQueryChange($event)"
              />
              <button
                type="button"
                class="scope-pill-btn"
                (click)="openSearchScopeModal()"
                [title]="'search.scopeTitle' | translate"
              >
                <svg class="scope-icon" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="4" y1="21" x2="4" y2="14"></line>
                  <line x1="4" y1="10" x2="4" y2="3"></line>
                  <line x1="12" y1="21" x2="12" y2="12"></line>
                  <line x1="12" y1="8" x2="12" y2="3"></line>
                  <line x1="20" y1="21" x2="20" y2="16"></line>
                  <line x1="20" y1="12" x2="20" y2="3"></line>
                  <line x1="1" y1="14" x2="7" y2="14"></line>
                  <line x1="9" y1="8" x2="15" y2="8"></line>
                  <line x1="17" y1="16" x2="23" y2="16"></line>
                </svg>
                <span class="scope-pill-text">{{ 'search.scopePrefix' | translate }}{{ searchScopeLabel() }}</span>
                <svg class="scope-chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="6 9 12 15 18 9"></polyline>
                </svg>
              </button>
              @if (searchQuery()) {
                <button class="clear-search" (click)="clearSearch()">✕</button>
              }
            </div>

            <!-- Display Mode Toggle -->
            @if (currentTab() !== 'trash') {
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

              <!-- New Folder & Upload Buttons -->
              <app-glass-button variant="secondary" size="md" (btnClick)="openCreateFolderModal()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                  <line x1="12" y1="11" x2="12" y2="17"></line>
                  <line x1="9" y1="14" x2="15" y2="14"></line>
                </svg>
                {{ 'dashboard.createFolder' | translate }}
              </app-glass-button>

              <app-glass-button variant="primary" size="md" (btnClick)="fileInput.click()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                  <polyline points="17 8 12 3 7 8"></polyline>
                  <line x1="12" y1="3" x2="12" y2="15"></line>
                </svg>
                {{ 'dashboard.uploadFiles' | translate }}
              </app-glass-button>

              <input
                #fileInput
                type="file"
                multiple
                style="display: none;"
                (change)="onFileInputChange($event)"
              />
            }
          </div>
        </header>

        <!-- Viewport Content Area -->
        <div class="stage-body">
          @if (currentTab() === 'trash') {
            <app-trash-bin></app-trash-bin>
          } @else {
            @if (loadingFolder()) {
              <div class="loading-container">Loading contents...</div>
            } @else {
              @if (displayMode() === 'grid') {
                <app-file-grid
                  [folders]="filteredFolders()"
                  [files]="filteredFiles()"
                  [selectedFolderId]="selectedFolderId()"
                  [selectedFileId]="selectedFileId()"
                  [selectedFolderIds]="selectedFolderIds()"
                  [selectedFileIds]="selectedFileIds()"
                  (selectFolder)="onSelectFolder($event)"
                  (openFolder)="onOpenFolder($event)"
                  (selectFile)="onSelectFile($event)"
                  (openFile)="onOpenFile($event)"
                  (downloadFile)="onDownloadFile($event)"
                  (toggleFavorite)="onToggleFavorite($event)"
                  (folderContextMenu)="onFolderContextMenu($event)"
                  (fileContextMenu)="onFileContextMenu($event)"
                  (folderDrop)="onFolderDrop($event)"
                  (selectionChange)="onSelectionChange($event)"
                  (workspaceContextMenu)="onWorkspaceContextMenu($event)"
                  (emptySpaceClick)="clearSelection()"
                ></app-file-grid>
              } @else {
                <app-file-list
                  [folders]="filteredFolders()"
                  [files]="filteredFiles()"
                  [selectedFolderId]="selectedFolderId()"
                  [selectedFileId]="selectedFileId()"
                  [selectedFolderIds]="selectedFolderIds()"
                  [selectedFileIds]="selectedFileIds()"
                  (selectFolder)="onSelectFolder($event)"
                  (openFolder)="onOpenFolder($event)"
                  (selectFile)="onSelectFile($event)"
                  (openFile)="onOpenFile($event)"
                  (downloadFile)="onDownloadFile($event)"
                  (toggleFavorite)="onToggleFavorite($event)"
                  (folderContextMenu)="onFolderContextMenu($event)"
                  (fileContextMenu)="onFileContextMenu($event)"
                  (folderDrop)="onFolderDrop($event)"
                  (selectionChange)="onSelectionChange($event)"
                  (workspaceContextMenu)="onWorkspaceContextMenu($event)"
                  (emptySpaceClick)="clearSelection()"
                ></app-file-list>
              }
            }
          }
        </div>
      </main>

      <!-- Right Inspector Sheet -->
      @if (inspectorFile()) {
        <app-file-details-sheet
          [file]="inspectorFile()"
          (close)="inspectorFile.set(null)"
          (download)="onDownloadFile($event)"
          (rename)="openRenameModal($event)"
          (toggleFavorite)="onToggleFavorite($event)"
          (delete)="openDeleteModal($event)"
        ></app-file-details-sheet>
      }

      <!-- Upload Tray Overlay -->
      <app-upload-tray></app-upload-tray>

      <!-- Context Menu -->
      <app-context-menu
        [isOpen]="contextMenuOpen()"
        [x]="contextMenuX()"
        [y]="contextMenuY()"
        [items]="contextMenuItems()"
        (itemClick)="onContextMenuItemClick($event)"
        (close)="contextMenuOpen.set(false)"
      ></app-context-menu>

      <!-- Create Folder Modal -->
      <app-glass-modal
        [isOpen]="createFolderModalOpen()"
        title="Create New Folder"
        (close)="createFolderModalOpen.set(false)"
      >
        <div class="modal-form">
          <label>Folder Name</label>
          <input
            type="text"
            [(ngModel)]="newFolderName"
            placeholder="e.g. Work Documents"
            (keyup.enter)="createFolder()"
            autofocus
          />

          <label>Color Badge</label>
          <div class="color-picker">
            @for (c of folderColors; track c) {
              <button
                class="color-dot"
                [style.background-color]="c"
                [class.color-dot--active]="newFolderColor === c"
                (click)="newFolderColor = c"
              ></button>
            }
          </div>
        </div>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="createFolderModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="primary" [disabled]="!newFolderName.trim()" (btnClick)="createFolder()">Create</app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Rename Item Modal -->
      <app-glass-modal
        [isOpen]="renameModalOpen()"
        title="Rename Item"
        (close)="renameModalOpen.set(false)"
      >
        <div class="modal-form">
          <label>New Name</label>
          <input
            type="text"
            [(ngModel)]="renameItemNewName"
            (keyup.enter)="applyRename()"
            autofocus
          />
        </div>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="renameModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="primary" [disabled]="!renameItemNewName.trim()" (btnClick)="applyRename()">Save</app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Confirm Delete Modal -->
      <app-glass-modal
        [isOpen]="deleteModalOpen()"
        title="Move to Trash"
        (close)="deleteModalOpen.set(false)"
      >
        <p>Are you sure you want to move <strong>{{ itemToDeleteName() }}</strong> to Trash?</p>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="deleteModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="danger" (btnClick)="applyDelete()">Move to Trash</app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Bulk Action Bar -->
      @if (selectedCount() > 0) {
        <div class="bulk-action-bar anim-slide-in-up">
          <div class="bulk-count">
            <span class="bulk-count-label">{{ 'files.selectedLabel' | translate }}</span>
            <span class="count-badge">{{ selectedCount() }}</span>
          </div>

          <div class="bulk-divider"></div>

          <div class="bulk-buttons">
            <button class="bulk-btn" (click)="downloadSelectedZip()" [title]="'files.downloadSelected' | translate">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
              <span>{{ 'files.downloadSelected' | translate }}</span>
            </button>

            <button class="bulk-btn" (click)="openBulkMoveModal()" [title]="'files.move' | translate">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M5 12h14"></path>
                <path d="M12 5l7 7-7 7"></path>
              </svg>
              <span>{{ 'files.move' | translate }}</span>
            </button>

            <button class="bulk-btn" (click)="openBulkCopyToSharedModal()" [title]="'files.copyToShared' | translate">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="18" cy="5" r="3"></circle>
                <circle cx="6" cy="12" r="3"></circle>
                <circle cx="18" cy="19" r="3"></circle>
                <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
              </svg>
              <span>{{ 'files.copyToShared' | translate }}</span>
            </button>

            <button class="bulk-btn bulk-btn--danger" (click)="openBulkDeleteModal()" [title]="'files.delete' | translate">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
              <span>{{ 'files.delete' | translate }}</span>
            </button>
          </div>

          <div class="bulk-divider"></div>

          <button class="bulk-close-btn" (click)="clearSelection()" title="Clear selection">✕</button>
        </div>
      }

      <!-- Bulk Delete Modal -->
      <app-glass-modal
        [isOpen]="bulkDeleteModalOpen()"
        [title]="'files.deleteModalTitle' | translate"
        (close)="bulkDeleteModalOpen.set(false)"
      >
        <p>{{ 'files.deleteModalConfirm' | translate:{ count: selectedCount() } }}</p>
        <div footer>
          <app-glass-button variant="secondary" (btnClick)="bulkDeleteModalOpen.set(false)">{{ 'common.cancel' | translate }}</app-glass-button>
          <app-glass-button variant="danger" (btnClick)="applyBulkDelete()">{{ 'files.moveToTrash' | translate }}</app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Bulk Move Modal -->
      <app-glass-modal
        [isOpen]="bulkMoveModalOpen()"
        [title]="'files.moveModalTitle' | translate"
        (close)="bulkMoveModalOpen.set(false)"
      >
        <p class="modal-sub">{{ 'files.moveModalSubtitle' | translate:{ count: selectedCount() } }}</p>
        <div class="move-folder-list">
          <button
            class="folder-select-item"
            [class.folder-select-item--active]="bulkMoveTargetFolderId === null"
            (click)="bulkMoveTargetFolderId = null"
          >
            <span class="folder-select-icon">📁</span>
            <span>{{ 'files.rootDrive' | translate }}</span>
          </button>
          @for (folder of availableMoveFolders(); track folder.id) {
            <button
              class="folder-select-item"
              [class.folder-select-item--active]="bulkMoveTargetFolderId === folder.id"
              (click)="bulkMoveTargetFolderId = folder.id"
            >
              <span class="folder-select-icon" [style.color]="folder.color || 'var(--omega-accent)'">📁</span>
              <span>{{ folder.name }}</span>
            </button>
          }
        </div>
        <div footer>
          <app-glass-button variant="secondary" (btnClick)="bulkMoveModalOpen.set(false)">{{ 'common.cancel' | translate }}</app-glass-button>
          <app-glass-button variant="primary" (btnClick)="applyBulkMove()">{{ 'files.moveHere' | translate }}</app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Bulk Copy To Shared Modal -->
      <app-glass-modal
        [isOpen]="bulkCopyToSharedModalOpen()"
        [title]="'files.copyToShared' | translate"
        (close)="bulkCopyToSharedModalOpen.set(false)"
      >
        <p class="modal-sub">{{ 'files.copyToSharedSubtitle' | translate }} <strong>{{ selectedCount() }}</strong> эл.:</p>
        <div class="move-folder-list">
          <button
            class="folder-select-item"
            [class.folder-select-item--active]="bulkCopyToSharedTargetFolderId === null"
            (click)="bulkCopyToSharedTargetFolderId = null"
          >
            <span class="folder-select-icon">📁</span>
            <span>{{ 'files.sharedRoot' | translate }}</span>
          </button>
          @for (folder of availableSharedFolders(); track folder.id) {
            <button
              class="folder-select-item"
              [class.folder-select-item--active]="bulkCopyToSharedTargetFolderId === folder.id"
              (click)="bulkCopyToSharedTargetFolderId = folder.id"
            >
              <span class="folder-select-icon" [style.color]="folder.color || 'var(--omega-accent)'">📁</span>
              <span>{{ folder.name }}</span>
            </button>
          }
        </div>
        <div footer>
          <app-glass-button variant="secondary" (btnClick)="bulkCopyToSharedModalOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </app-glass-button>
          <app-glass-button variant="primary" (btnClick)="applyBulkCopyToShared()">
            {{ 'files.copyToShared' | translate }}
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Search Scope Modal -->
      <app-search-scope-modal
        [isOpen]="isSearchScopeModalOpen()"
        [initialScope]="searchScope()"
        [initialFolderIds]="searchFolderIds()"
        (close)="isSearchScopeModalOpen.set(false)"
        (applyScope)="onApplySearchScope($event)"
      ></app-search-scope-modal>

      <!-- Folder Properties Modal -->
      <app-glass-modal
        [isOpen]="folderPropertiesModalOpen()"
        [title]="'Properties: ' + currentFolderName()"
        (close)="folderPropertiesModalOpen.set(false)"
      >
        <div class="properties-list">
          <div class="prop-row">
            <span class="prop-label">Name</span>
            <span class="prop-value">{{ currentFolderName() }}</span>
          </div>
          <div class="prop-row">
            <span class="prop-label">Folders</span>
            <span class="prop-value">{{ filteredFolders().length }}</span>
          </div>
          <div class="prop-row">
            <span class="prop-label">Files</span>
            <span class="prop-value">{{ filteredFiles().length }}</span>
          </div>
          <div class="prop-row">
            <span class="prop-label">Total Size</span>
            <span class="prop-value">{{ currentFolderTotalSizeBytes() | fileSize }}</span>
          </div>
        </div>
        <div footer>
          <app-glass-button variant="secondary" (btnClick)="folderPropertiesModalOpen.set(false)">Close</app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Upload Queue Modal -->
      <app-upload-queue-modal
        [isOpen]="isUploadQueueOpen()"
        [inputFiles]="queuedFiles()"
        [destinationName]="currentFolderName()"
        [destinationFolderId]="currentFolderId()"
        [isShared]="false"
        (close)="isUploadQueueOpen.set(false)"
        (startUpload)="onStartUploadFromQueue($event)"
      ></app-upload-queue-modal>

      <!-- Mobile Action Sheet -->
      <div class="mobile-action-sheet" [class.mobile-action-sheet--visible]="selectedCount() > 0">
        <div class="action-sheet-header">
          <span class="sheet-count">{{ selectedCount() }} {{ 'files.selectedCount' | translate }}</span>
          <button class="sheet-close-btn" (click)="clearSelection()">✕</button>
        </div>
        <div class="sheet-actions">
          <button class="sheet-btn" (click)="downloadSelectedZip()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="7 10 12 15 17 10"></polyline>
              <line x1="12" y1="15" x2="12" y2="3"></line>
            </svg>
            <span>{{ 'files.downloadSelected' | translate }}</span>
          </button>
          <button class="sheet-btn" (click)="openBulkMoveModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M5 12h14"></path>
              <path d="M12 5l7 7-7 7"></path>
            </svg>
            <span>{{ 'files.move' | translate }}</span>
          </button>
          <button class="sheet-btn" (click)="openBulkCopyToSharedModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="18" cy="5" r="3"></circle>
              <circle cx="6" cy="12" r="3"></circle>
              <circle cx="18" cy="19" r="3"></circle>
              <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
              <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
            </svg>
            <span>{{ 'files.copyToShared' | translate }}</span>
          </button>
          <button class="sheet-btn sheet-btn--danger" (click)="openBulkDeleteModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
            <span>{{ 'files.deleteSelected' | translate }}</span>
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .app-shell {
      position: relative;
      width: 100vw;
      height: 100vh;
      display: flex;
      overflow: hidden;
      background: var(--omega-bg-canvas);
      background-image: var(--omega-bg-gradient);
    }

    // Sidebar
    .sidebar {
      width: 260px;
      min-width: 260px;
      height: 100%;
      @include glass-surface;
      border-width: 0 1px 0 0;
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
    }

    .brand-text {
      font-size: 17px;
      font-weight: 700;
      letter-spacing: -0.01em;
      color: var(--omega-text-primary);
    }

    .sidebar-nav {
      padding: 10px 14px;
      display: flex;
      flex-direction: column;
      gap: 4px;
      flex: 1;
    }

    .nav-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      border-radius: var(--omega-radius-md);
      font-size: 14px;
      font-weight: 500;
      color: var(--omega-text-secondary);
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

      &--dragover {
        border-color: var(--omega-accent) !important;
        background: var(--omega-accent-subtle) !important;
        color: var(--omega-accent) !important;
        transform: scale(1.04);
        box-shadow: 0 0 16px rgba(0, 113, 227, 0.45);
      }
    }

    .sidebar-footer {
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 16px;
      border-top: 1px solid var(--omega-glass-border-subtle);
    }

    .quota-card {
      @include glass-sunken;
      border-radius: var(--omega-radius-md);
      padding: 12px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .quota-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .quota-title {
      font-size: 11px;
      font-weight: 600;
      color: var(--omega-text-muted);
      text-transform: uppercase;
    }

    .quota-percent {
      font-size: 11px;
      font-weight: 700;
      color: var(--omega-accent);
    }

    .quota-bar-track {
      width: 100%;
      height: 5px;
      background: rgba(120, 120, 128, 0.2);
      border-radius: var(--omega-radius-pill);
      overflow: hidden;
    }

    .quota-bar-fill {
      height: 100%;
      background: var(--omega-accent-gradient);
      border-radius: var(--omega-radius-pill);
      transition: width 0.3s ease;

      &--warning {
        background: var(--omega-warning);
      }

      &--danger {
        background: var(--omega-danger);
      }
    }

    .quota-text {
      font-size: 11px;
      color: var(--omega-text-muted);
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
      background: var(--omega-accent);
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
      color: var(--omega-text-muted);
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

    // Main Stage
    .main-stage {
      flex: 1;
      height: 100%;
      display: flex;
      flex-direction: column;
      min-width: 0;
      overflow: hidden;
    }

    .stage-header {
      padding: 16px 28px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
    }

    .header-left {
      min-width: 0;
    }

    .view-title {
      font-size: 18px;
      font-weight: 700;
      color: var(--omega-text-primary);
    }

    .header-right {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .search-box {
      position: relative;
      display: flex;
      align-items: center;
      gap: 8px;

      > svg {
        position: absolute;
        left: 12px;
        color: var(--omega-text-muted);
        pointer-events: none;
      }

      input {
        width: 190px;
        height: 38px;
        padding-left: 34px;
        padding-right: 28px;
        border-radius: var(--omega-radius-pill);
      }

      .clear-search {
        position: absolute;
        left: 165px;
        font-size: 11px;
        color: var(--omega-text-muted);
        background: none;
        border: none;
        cursor: pointer;

        &:hover {
          color: var(--omega-text-primary);
        }
      }

      .scope-pill-btn {
        @include glass-surface;
        padding: 5px 12px;
        border-radius: var(--omega-radius-pill);
        border: 1px solid var(--omega-glass-border-subtle);
        color: var(--omega-text-secondary);
        font-size: 12px;
        font-weight: 500;
        display: inline-flex;
        align-items: center;
        gap: 6px;
        cursor: pointer;
        transition: all 0.2s var(--omega-ease-smooth);
        white-space: nowrap;
        max-width: 180px;
        height: 34px;

        &:hover {
          background: var(--omega-glass-bg-hover);
          color: var(--omega-text-primary);
          border-color: var(--omega-accent);
        }

        .scope-pill-text {
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .scope-chevron {
          position: static;
          flex-shrink: 0;
          opacity: 0.7;
          pointer-events: auto;
        }
      }
    }

    .view-toggle {
      display: flex;
      align-items: center;
      @include glass-sunken;
      border-radius: var(--omega-radius-md);
      padding: 2px;
    }

    .toggle-btn {
      width: 32px;
      height: 32px;
      border-radius: var(--omega-radius-sm);
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);
      transition: all 0.15s;

      &--active {
        background: var(--omega-glass-bg-elevated);
        color: var(--omega-text-primary);
        box-shadow: var(--omega-shadow-sm);
      }
    }

    .stage-body {
      flex: 1;
      padding: 24px 28px;
      overflow-y: auto;
    }

    .loading-container {
      padding: 60px;
      text-align: center;
      color: var(--omega-text-muted);
    }

    // Drag-Drop Overlay
    .drag-drop-overlay {
      position: absolute;
      inset: 0;
      z-index: 9999;
      background: rgba(0, 113, 227, 0.15);
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
      display: flex;
      align-items: center;
      justify-content: center;
      pointer-events: none;
    }

    .drop-card {
      @include glass-elevated;
      padding: 40px 60px;
      border-radius: var(--omega-radius-xl);
      border: 2px dashed var(--omega-accent);
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      color: var(--omega-accent);

      h3 {
        font-size: 20px;
        font-weight: 700;
        color: var(--omega-text-primary);
      }

      p {
        font-size: 14px;
        color: var(--omega-text-secondary);
      }
    }

    // Modal Form Elements
    .modal-form {
      display: flex;
      flex-direction: column;
      gap: 12px;

      label {
        font-size: 13px;
        font-weight: 500;
        color: var(--omega-text-secondary);
      }

      input {
        width: 100%;
        height: 42px;
      }
    }

    .color-picker {
      display: flex;
      gap: 10px;
      margin-top: 4px;
    }

    .color-dot {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      transition: all 0.2s var(--omega-ease-spring);

      &:hover {
        transform: scale(1.15);
      }

      &--active {
        box-shadow: 0 0 0 3px var(--omega-accent);
        transform: scale(1.15);
      }
    }

    .bulk-action-bar {
      @include glass-elevated;
      position: fixed;
      bottom: 24px;
      left: 50%;
      transform: translateX(-50%);
      z-index: 9000;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 8px 16px;
      border-radius: var(--omega-radius-full, 9999px);
      box-shadow: var(--omega-shadow-xl), 0 0 24px rgba(0, 113, 227, 0.2);
    }

    .bulk-count {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);

      .count-badge {
        background: var(--omega-accent);
        color: #ffffff;
        font-size: 11px;
        font-weight: 700;
        padding: 2px 7px;
        border-radius: 10px;
      }
    }

    .bulk-divider {
      width: 1px;
      height: 20px;
      background: var(--omega-glass-border-subtle);
    }

    .bulk-buttons {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .bulk-btn {
      background: var(--omega-glass-bg-sunken);
      border: 1px solid var(--omega-glass-border-subtle);
      border-radius: var(--omega-radius-sm);
      padding: 6px 12px;
      color: var(--omega-text-primary);
      font-size: 12px;
      font-weight: 500;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      cursor: pointer;
      transition: all 0.15s ease;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-accent);
        transform: translateY(-1px);
      }

      &--danger:hover {
        color: var(--omega-danger) !important;
      }
    }

    .bulk-close-btn {
      background: transparent;
      border: none;
      color: var(--omega-text-muted);
      cursor: pointer;
      font-size: 14px;
      padding: 4px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;

      &:hover {
        color: var(--omega-text-primary);
        background: var(--omega-glass-bg-hover);
      }
    }

    .move-folder-list {
      display: flex;
      flex-direction: column;
      gap: 6px;
      max-height: 240px;
      overflow-y: auto;
      margin: 12px 0;
    }

    .folder-select-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 12px;
      border-radius: var(--omega-radius-sm);
      background: var(--omega-glass-bg-sunken);
      border: 1px solid transparent;
      color: var(--omega-text-primary);
      font-size: 13px;
      cursor: pointer;
      text-align: left;
      transition: all 0.15s;

      &:hover { background: var(--omega-glass-bg-hover); }
      &--active {
        border-color: var(--omega-accent);
        background: var(--omega-accent-subtle);
        color: var(--omega-accent);
      }
    }

    .properties-list {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding: 6px 0;
    }

    .prop-row {
      display: flex;
      justify-content: space-between;
      padding: 6px 0;
      border-bottom: 1px solid var(--omega-glass-border-subtle);
      font-size: 13px;
      &:last-child { border-bottom: none; }
    }

    .prop-label { color: var(--omega-text-muted); }
    .prop-value { font-weight: 500; color: var(--omega-text-primary); }

    .scope-select {
      background: none;
      border: none;
      border-left: 1px solid var(--omega-glass-border-subtle);
      padding-left: 8px;
      font-size: 11px;
      color: var(--omega-text-secondary);
      outline: none;
      cursor: pointer;

      option {
        background: var(--omega-bg-canvas);
        color: var(--omega-text-primary);
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

    .hamburger-btn, .mobile-close-btn {
      display: none;
      background: none;
      border: none;
      color: var(--omega-text-primary);
      cursor: pointer;
      min-width: 44px;
      min-height: 44px;
    }

    .mobile-drawer-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.45);
      backdrop-filter: var(--omega-backdrop-blur);
      -webkit-backdrop-filter: var(--omega-backdrop-blur);
      z-index: 99;
    }

    .mobile-action-sheet {
      position: fixed;
      bottom: 0;
      left: 0;
      right: 0;
      z-index: 105;
      @include glass-elevated;
      border-radius: 16px 16px 0 0;
      padding: 16px 20px 24px;
      display: none; /* hidden on desktop */
      flex-direction: column;
      gap: 12px;
      transform: translateY(110%);
      transition: transform 0.25s var(--omega-ease-smooth);
      box-shadow: 0 -8px 24px rgba(0, 0, 0, 0.3);

      &--visible {
        transform: translateY(0);
      }

      .action-sheet-header {
        display: flex;
        align-items: center;
        justify-content: space-between;

        .sheet-count {
          font-size: 14px;
          font-weight: 600;
          color: var(--omega-text-primary);
        }

        .sheet-close-btn {
          background: none;
          border: none;
          color: var(--omega-text-muted);
          font-size: 16px;
          cursor: pointer;
          min-width: 44px;
          min-height: 44px;
          display: flex;
          align-items: center;
          justify-content: center;
        }
      }

      .sheet-actions {
        display: flex;
        gap: 12px;
      }

      .sheet-btn {
        flex: 1;
        @include glass-surface;
        min-height: 44px;
        border-radius: var(--omega-radius-md);
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 8px;
        font-size: 13px;
        font-weight: 600;
        color: var(--omega-text-primary);
        cursor: pointer;
        border: 1px solid var(--omega-glass-border-subtle);

        &:hover {
          background: var(--omega-glass-bg-hover);
        }

        &--danger {
          color: var(--omega-danger);
          border-color: rgba(255, 69, 58, 0.3);
          background: var(--omega-danger-subtle);
        }
      }
    }

    /* Mobile media query */
    @media (max-width: 768px), (pointer: coarse) {
      .sidebar {
        position: fixed;
        top: 0;
        left: 0;
        bottom: 0;
        transform: translateX(-100%);
        transition: transform 0.25s var(--omega-ease-smooth);
        z-index: 100;
        box-shadow: 0 0 30px rgba(0, 0, 0, 0.4);
        will-change: transform;

        &--open {
          transform: translateX(0);
        }
      }

      .mobile-drawer-backdrop {
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .hamburger-btn, .mobile-close-btn {
        display: flex;
        align-items: center;
        justify-content: center;
      }

      .stage-header {
        padding: 10px 14px;
        flex-wrap: wrap;
        gap: 8px;
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .header-left {
        width: 100%;
        display: flex;
        align-items: center;
        gap: 8px;
      }

      .header-right {
        width: 100%;
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 8px;
        justify-content: flex-start;
      }

      .search-box {
        flex: 1 1 140px;
        min-width: 120px;

        input {
          width: 100%;
          min-width: 100px;
        }

        .clear-search {
          left: auto;
          right: 48px;
        }

        .scope-pill-text {
          display: none;
        }
      }

      .stage-body {
        padding: 12px 14px;
        -webkit-overflow-scrolling: touch;
        overscroll-behavior-y: contain;
      }

      .mobile-action-sheet {
        display: flex;
        will-change: transform;
        backdrop-filter: none !important;
        -webkit-backdrop-filter: none !important;
      }

      .bulk-action-bar {
        display: none; /* replaced by mobile action sheet */
      }
    }
  `]
})
export class DriveLayoutComponent implements OnInit, OnDestroy {
  @ViewChild('fileInput') fileInput!: ElementRef<HTMLInputElement>;

  private readonly apiService = inject(ApiService);
  readonly authStore = inject(AuthStore);
  readonly themeService = inject(ThemeService);
  private readonly signalRService = inject(SignalRService);
  private readonly uploader = inject(ResumableUploaderService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly toastService = inject(ToastService);
  readonly i18n = inject(I18nService);
  readonly dragState = inject(DragStateService);

  readonly languageOptions: DropdownOption[] = SUPPORTED_LANGUAGES.map(l => ({
    value: l.code,
    label: l.shortLabel
  }));

  currentTab = signal<DriveViewTab>('files');
  displayMode = signal<DisplayMode>('grid');

  currentFolderId = signal<string | null>(null);
  folderContent = signal<FolderContent | null>(null);
  loadingFolder = signal<boolean>(false);

  // Multi-selection state
  selectedFolderIds = signal<string[]>([]);
  selectedFileIds = signal<string[]>([]);
  selectedFolderId = signal<string | null>(null);
  selectedFileId = signal<string | null>(null);
  selectedFile = signal<FileItem | null>(null);
  inspectorFile = signal<FileItem | null>(null);
  readonly selectedCount = computed(() => this.selectedFolderIds().length + this.selectedFileIds().length);

  searchQuery = signal<string>('');
  searchScope = signal<'everywhere' | 'personal' | 'shared' | 'custom'>('everywhere');
  searchFolderIds = signal<string[]>([]);
  searchScopeLabel = signal<string>(this.i18n.translate('search.scopeEverywhere'));
  isSearchScopeModalOpen = signal<boolean>(false);

  isSharedNavDragOver = signal<boolean>(false);
  readonly searchResults = signal<FolderContent | null>(null);
  readonly isSearching = signal<boolean>(false);
  isMobileMenuOpen = signal<boolean>(false);
  isUploadQueueOpen = signal<boolean>(false);
  queuedFiles = signal<File[]>([]);

  isDraggingOver = signal<boolean>(false);
  private dragCounter = 0;

  // Context Menu State
  contextMenuOpen = signal<boolean>(false);
  contextMenuX = signal<number>(0);
  contextMenuY = signal<number>(0);
  contextMenuItems = signal<ContextMenuItem[]>([]);
  private contextTargetFolder: FolderItem | null = null;
  private contextTargetFile: FileItem | null = null;
  private isWorkspaceContext = false;

  // Create Folder State
  createFolderModalOpen = signal<boolean>(false);
  newFolderName = '';
  newFolderColor = '#0071e3';
  readonly folderColors = ['#0071e3', '#bf5af2', '#ff9f0a', '#ff453a', '#30d158', '#8e8e93'];

  // Rename Item State
  renameModalOpen = signal<boolean>(false);
  renameItemTarget: { type: 'folder' | 'file'; id: string } | null = null;
  renameItemNewName = '';

  // Delete Item State
  deleteModalOpen = signal<boolean>(false);
  deleteItemTarget: { type: 'folder' | 'file'; id: string; name: string } | null = null;
  itemToDeleteName = computed(() => this.deleteItemTarget?.name || '');

  // Bulk Operations State
  bulkDeleteModalOpen = signal<boolean>(false);
  bulkMoveModalOpen = signal<boolean>(false);
  bulkMoveTargetFolderId: string | null = null;
  bulkCopyToSharedModalOpen = signal<boolean>(false);
  bulkCopyToSharedTargetFolderId: string | null = null;
  availableSharedFolders = signal<FolderItem[]>([]);
  folderPropertiesModalOpen = signal<boolean>(false);

  private subs: Subscription[] = [];

  readonly currentFolderName = computed(() => {
    return this.folderContent()?.currentFolder?.name || 'Root Drive';
  });

  readonly userInitials = computed(() => {
    const user = this.authStore.currentUser();
    if (!user || !user.username) return 'U';
    return user.username.slice(0, 2).toUpperCase();
  });

  openSearchScopeModal(): void {
    this.isSearchScopeModalOpen.set(true);
  }

  onApplySearchScope(result: SearchScopeResult): void {
    this.searchScope.set(result.scope);
    this.searchFolderIds.set(result.folderIds);
    this.searchScopeLabel.set(result.label);
    if (this.searchQuery().trim()) {
      this.performSearch();
    }
  }

  onSearchQueryChange(query: string): void {
    this.searchQuery.set(query);
    this.performSearch();
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.searchResults.set(null);
  }

  performSearch(): void {
    const q = this.searchQuery().trim();
    if (!q) {
      this.searchResults.set(null);
      return;
    }

    this.isSearching.set(true);
    const scope = this.searchScope();
    const folderIds = this.searchFolderIds();
    this.apiService.searchFiles(q, null, scope, folderIds).subscribe({
      next: (res) => {
        this.isSearching.set(false);
        this.searchResults.set(res);
      },
      error: () => {
        this.isSearching.set(false);
        this.searchResults.set(null);
      }
    });
  }

  readonly filteredFolders = computed(() => {
    if (this.currentTab() === 'favorites') return [];
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) {
      return this.folderContent()?.folders || [];
    }

    const searchRes = this.searchResults();
    if (searchRes) {
      return searchRes.folders;
    }

    const content = this.folderContent();
    if (!content) return [];
    return content.folders.filter(f => f.name.toLowerCase().includes(q));
  });

  readonly filteredFiles = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) {
      const content = this.folderContent();
      if (!content) return [];
      let list = content.files;
      if (this.currentTab() === 'favorites') {
        list = list.filter(f => f.isFavorite);
      }
      return list;
    }

    const searchRes = this.searchResults();
    if (searchRes) {
      let list = searchRes.files;
      if (this.currentTab() === 'favorites') {
        list = list.filter(f => f.isFavorite);
      }
      return list;
    }

    const content = this.folderContent();
    if (!content) return [];
    let list = content.files;
    if (this.currentTab() === 'favorites') {
      list = list.filter(f => f.isFavorite);
    }
    return list.filter(f => f.name.toLowerCase().includes(q));
  });

  readonly availableMoveFolders = computed(() => {
    const content = this.folderContent();
    if (!content) return [];
    const excluded = new Set(this.selectedFolderIds());
    return content.folders.filter(f => !excluded.has(f.id));
  });

  readonly currentFolderTotalSizeBytes = computed(() => {
    const files = this.folderContent()?.files || [];
    return files.reduce((acc, f) => acc + (f.sizeBytes || 0), 0);
  });

  ngOnInit(): void {
    // Route query param subscription (History API integration)
    this.subs.push(
      this.route.queryParams.subscribe(params => {
        const tabParam = params['tab'];
        if (tabParam === 'favorites' || tabParam === 'trash' || tabParam === 'files') {
          if (this.currentTab() !== tabParam) {
            this.currentTab.set(tabParam as DriveViewTab);
          }
        }
        const folderParam = params['folder'] || null;
        if (folderParam !== this.currentFolderId()) {
          this.switchFolder(folderParam);
        } else if (!this.folderContent()) {
          this.loadCurrentFolder();
        }
      })
    );

    // Start SignalR & subscribe to current folder events
    this.signalRService.start().then(() => {
      this.signalRService.subscribeToFolder(this.currentFolderId());
    });

    this.subs.push(
      this.signalRService.directoryChanged$.subscribe((evt) => {
        const currentId = this.currentFolderId();
        const evtFolderId = evt.folderId || null;
        if (evtFolderId === currentId || !evt.folderId) {
          this.loadCurrentFolder();
        }
      })
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    this.signalRService.unsubscribeFromFolder(this.currentFolderId());
    this.signalRService.stop();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.contextMenuOpen()) {
      this.contextMenuOpen.set(false);
    } else if (this.bulkMoveModalOpen()) {
      this.bulkMoveModalOpen.set(false);
    } else if (this.bulkDeleteModalOpen()) {
      this.bulkDeleteModalOpen.set(false);
    } else if (this.bulkCopyToSharedModalOpen()) {
      this.bulkCopyToSharedModalOpen.set(false);
    } else if (this.isSearchScopeModalOpen()) {
      this.isSearchScopeModalOpen.set(false);
    } else if (this.folderPropertiesModalOpen()) {
      this.folderPropertiesModalOpen.set(false);
    } else if (this.selectedCount() > 0) {
      this.clearSelection();
    }
  }

  @HostListener('document:dragend')
  onDocumentDragEnd(): void {
    this.isSharedNavDragOver.set(false);
    this.isDraggingOver.set(false);
    this.dragCounter = 0;
    this.dragState.endDrag();
  }

  selectTab(tab: DriveViewTab): void {
    this.currentTab.set(tab);
    this.clearSelection();
    this.inspectorFile.set(null);
    if (tab === 'files') {
      this.loadCurrentFolder();
    }
  }

  loadCurrentFolder(): void {
    this.loadingFolder.set(true);
    const folderId = this.currentFolderId();

    const request$ = folderId
      ? this.apiService.getFolderContent(folderId)
      : this.apiService.getRootFolderContent();

    request$.subscribe({
      next: content => {
        this.folderContent.set(content);
        if (content.quota) {
          this.authStore.updateQuota(content.quota.usedBytes, content.quota.allocatedBytes);
        }
        this.loadingFolder.set(false);
      },
      error: () => {
        this.loadingFolder.set(false);
      }
    });
  }

  onNavigateFolder(folderId: string | null): void {
    if (this.currentTab() !== 'files') {
      this.currentTab.set('files');
    }
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { folder: folderId || null },
      queryParamsHandling: 'merge'
    });
  }

  private switchFolder(folderId: string | null): void {
    if (this.currentTab() !== 'files') {
      this.currentTab.set('files');
    }
    const prevFolderId = this.currentFolderId();
    if (prevFolderId !== folderId) {
      this.signalRService.unsubscribeFromFolder(prevFolderId);
      this.signalRService.subscribeToFolder(folderId);
      this.clearSearch();
    }
    this.currentFolderId.set(folderId);
    this.clearSelection();
    this.inspectorFile.set(null);
    this.loadCurrentFolder();
  }

  onSelectFolder(folder: FolderItem): void {
    this.selectedFolderId.set(folder.id);
    this.selectedFileId.set(null);
    this.selectedFolderIds.set([folder.id]);
    this.selectedFileIds.set([]);
    this.selectedFile.set(null);
  }

  onOpenFolder(folder: FolderItem): void {
    this.onNavigateFolder(folder.id);
  }

  onSelectFile(file: FileItem): void {
    this.selectedFile.set(file);
    this.selectedFileId.set(file.id);
    this.selectedFolderId.set(null);
    this.selectedFileIds.set([file.id]);
    this.selectedFolderIds.set([]);
    // Note: Single click selects item; does NOT open inspector
  }

  onOpenFile(file: FileItem): void {
    // Double click: open inspector/preview
    this.inspectorFile.set(file);
  }

  onSelectionChange(selection: { folderIds: string[]; fileIds: string[] }): void {
    this.selectedFolderIds.set(selection.folderIds);
    this.selectedFileIds.set(selection.fileIds);
    this.selectedFolderId.set(selection.folderIds.length === 1 && selection.fileIds.length === 0 ? selection.folderIds[0] : null);
    const singleFile = selection.fileIds.length === 1 && selection.folderIds.length === 0
      ? (this.folderContent()?.files?.find(f => f.id === selection.fileIds[0]) || null)
      : null;
    this.selectedFile.set(singleFile);
    this.selectedFileId.set(singleFile?.id || null);
  }

  clearSelection(): void {
    this.selectedFolderIds.set([]);
    this.selectedFileIds.set([]);
    this.selectedFolderId.set(null);
    this.selectedFileId.set(null);
    this.selectedFile.set(null);
  }

  onEmptySpaceClick(): void {
    this.clearSelection();
    this.inspectorFile.set(null);
  }

  onDownloadFile(file: FileItem): void {
    this.apiService.downloadFileBlob(file.id).subscribe({
      next: blob => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = file.name;
        a.click();
        window.URL.revokeObjectURL(url);
        this.toastService.success(`Downloaded ${file.name}`);
      },
      error: () => {
        window.open(this.apiService.getDownloadUrl(file.id), '_blank');
      }
    });
  }

  onToggleFavorite(file: FileItem): void {
    const nextVal = !file.isFavorite;
    this.apiService.updateFile(file.id, { isFavorite: nextVal }).subscribe({
      next: updated => {
        this.folderContent.update(c => {
          if (!c) return c;
          return {
            ...c,
            files: c.files.map(f => (f.id === updated.id ? updated : f))
          };
        });
        if (this.selectedFile()?.id === file.id) {
          this.selectedFile.set(updated);
        }
        if (this.inspectorFile()?.id === file.id) {
          this.inspectorFile.set(updated);
        }
        this.toastService.info(nextVal ? 'Added to favorites' : 'Removed from favorites');
      }
    });
  }

  // Drag and Drop (from OS into Window)
  onWindowDragEnter(event: DragEvent): void {
    event.preventDefault();
    if (this.dragState.isDragging()) {
      return;
    }
    this.dragCounter++;
    if (event.dataTransfer?.types?.includes('Files')) {
      this.isDraggingOver.set(true);
    }
  }

  onWindowDragOver(event: DragEvent): void {
    event.preventDefault();
    if (this.dragState.isDragging()) {
      return;
    }
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  onWindowDragLeave(event: DragEvent): void {
    event.preventDefault();
    if (this.dragState.isDragging()) {
      return;
    }
    this.dragCounter--;
    if (this.dragCounter <= 0) {
      this.isDraggingOver.set(false);
      this.dragCounter = 0;
    }
  }

  async onWindowDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    if (this.dragState.isDragging()) {
      return;
    }
    this.isDraggingOver.set(false);
    this.dragCounter = 0;

    if (event.dataTransfer) {
      const files = await this.extractFilesFromDataTransfer(event.dataTransfer);
      if (files.length > 0) {
        this.queuedFiles.set(files);
        this.isUploadQueueOpen.set(true);
      }
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

  onStartUploadFromQueue(event: { files: File[]; resolutions?: Record<string, 'Replace' | 'Rename' | 'Skip'> }): void {
    if (!event.files || event.files.length === 0) return;

    for (const file of event.files) {
      const res = event.resolutions?.[file.name];
      this.uploader.enqueue([file], this.currentFolderId(), this.currentFolderName(), res, false);
    }
    this.toastService.info(`Started uploading ${event.files.length} file(s)`);
  }

  private async extractFilesFromDataTransfer(dataTransfer: DataTransfer): Promise<File[]> {
    const items = dataTransfer.items;
    if (!items || items.length === 0) {
      return Array.from(dataTransfer.files || []);
    }

    const fileList: File[] = [];
    const entries: any[] = [];

    for (let i = 0; i < items.length; i++) {
      const entry = (items[i] as any).webkitGetAsEntry?.();
      if (entry) {
        entries.push(entry);
      } else {
        const f = items[i].getAsFile();
        if (f) fileList.push(f);
      }
    }

    if (entries.length === 0) {
      return fileList.length > 0 ? fileList : Array.from(dataTransfer.files || []);
    }

    const traverse = async (entry: any, path: string = ''): Promise<void> => {
      if (entry.isFile) {
        await new Promise<void>((resolve) => {
          entry.file((file: File) => {
            if (path) {
              Object.defineProperty(file, 'webkitRelativePath', {
                value: `${path}/${file.name}`,
                writable: true
              });
            }
            fileList.push(file);
            resolve();
          }, () => resolve());
        });
      } else if (entry.isDirectory) {
        const dirReader = entry.createReader();
        const readBatch = async (): Promise<any[]> => {
          return new Promise<any[]>((resolve) => {
            dirReader.readEntries((res: any[]) => resolve(res || []), () => resolve([]));
          });
        };
        let children = await readBatch();
        while (children.length > 0) {
          for (const child of children) {
            await traverse(child, path ? `${path}/${entry.name}` : entry.name);
          }
          children = await readBatch();
        }
      }
    };

    for (const e of entries) {
      await traverse(e);
    }

    return fileList.length > 0 ? fileList : Array.from(dataTransfer.files || []);
  }

  // Drag-and-drop file/items onto folder
  onFolderDrop(payload: { targetFolder: FolderItem; fileId?: string; fileIds?: string[]; folderIds?: string[] }): void {
    const fIds = payload.fileIds && payload.fileIds.length > 0 ? payload.fileIds : (payload.fileId ? [payload.fileId] : []);
    const folderIds = payload.folderIds && payload.folderIds.length > 0 ? payload.folderIds : [];

    if (folderIds.includes(payload.targetFolder.id)) {
      this.toastService.warning('Cannot move a folder into itself');
      return;
    }

    if (fIds.length > 0 || folderIds.length > 0) {
      this.apiService.bulkMove(fIds, folderIds, payload.targetFolder.id).subscribe({
        next: () => {
          this.clearSelection();
          this.loadCurrentFolder();
          this.toastService.success(`Moved items into ${payload.targetFolder.name}`);
        },
        error: (err) => {
          if (payload.fileId && fIds.length <= 1 && folderIds.length === 0) {
            this.apiService.updateFile(payload.fileId, { folderId: payload.targetFolder.id }).subscribe({
              next: () => {
                this.loadCurrentFolder();
                this.toastService.success(`Moved item into ${payload.targetFolder.name}`);
              }
            });
          } else {
            const msg = err?.error?.message || 'Failed to move items';
            this.toastService.error(msg);
          }
        }
      });
    }
  }

  // Context Menu Handlers
  onFolderContextMenu(payload: { event: MouseEvent; folder: FolderItem }): void {
    this.contextTargetFolder = payload.folder;
    this.contextTargetFile = null;
    this.isWorkspaceContext = false;
    this.contextMenuX.set(payload.event.clientX);
    this.contextMenuY.set(payload.event.clientY);

    this.contextMenuItems.set([
      { id: 'open', label: 'Open Folder' },
      { id: 'copy_shared', label: 'Копировать в Общий' },
      { id: 'rename', label: 'Rename' },
      { id: 'delete', label: 'Move to Trash', danger: true, dividerBefore: true }
    ]);
    this.contextMenuOpen.set(true);
  }

  onFileContextMenu(payload: { event: MouseEvent; file: FileItem }): void {
    this.contextTargetFile = payload.file;
    this.contextTargetFolder = null;
    this.isWorkspaceContext = false;
    this.contextMenuX.set(payload.event.clientX);
    this.contextMenuY.set(payload.event.clientY);

    this.contextMenuItems.set([
      { id: 'download', label: 'Download' },
      { id: 'copy_shared', label: 'Копировать в Общий' },
      { id: 'details', label: 'Inspector' },
      { id: 'star', label: payload.file.isFavorite ? 'Remove from Favorites' : 'Add to Favorites' },
      { id: 'rename', label: 'Rename' },
      { id: 'delete', label: 'Move to Trash', danger: true, dividerBefore: true }
    ]);
    this.contextMenuOpen.set(true);
  }

  onWorkspaceContextMenu(event: MouseEvent): void {
    this.contextTargetFolder = null;
    this.contextTargetFile = null;
    this.isWorkspaceContext = true;
    this.contextMenuX.set(event.clientX);
    this.contextMenuY.set(event.clientY);

    this.contextMenuItems.set([
      { id: 'create_folder', label: 'Создать папку' },
      { id: 'upload_files', label: 'Загрузить файлы' },
      { id: 'refresh', label: 'Обновить' },
      { id: 'folder_properties', label: 'Свойства папки', dividerBefore: true }
    ]);
    this.contextMenuOpen.set(true);
  }

  onContextMenuItemClick(actionId: string): void {
    if (this.isWorkspaceContext) {
      this.isWorkspaceContext = false;
      if (actionId === 'create_folder') {
        this.openCreateFolderModal();
      } else if (actionId === 'upload_files') {
        if (this.fileInput) {
          this.fileInput.nativeElement.click();
        }
      } else if (actionId === 'refresh') {
        this.loadCurrentFolder();
        this.toastService.info('Папка обновлена');
      } else if (actionId === 'folder_properties') {
        this.folderPropertiesModalOpen.set(true);
      }
      return;
    }

    if (this.contextTargetFolder) {
      const folder = this.contextTargetFolder;
      if (actionId === 'open') {
        this.onOpenFolder(folder);
      } else if (actionId === 'copy_shared') {
        this.selectedFolderIds.set([folder.id]);
        this.selectedFileIds.set([]);
        this.openBulkCopyToSharedModal();
      } else if (actionId === 'rename') {
        this.openRenameModal({ type: 'folder', id: folder.id, name: folder.name });
      } else if (actionId === 'delete') {
        this.openDeleteModal({ type: 'folder', id: folder.id, name: folder.name });
      }
    } else if (this.contextTargetFile) {
      const file = this.contextTargetFile;
      if (actionId === 'download') {
        this.onDownloadFile(file);
      } else if (actionId === 'copy_shared') {
        this.selectedFolderIds.set([]);
        this.selectedFileIds.set([file.id]);
        this.openBulkCopyToSharedModal();
      } else if (actionId === 'details') {
        this.inspectorFile.set(file);
      } else if (actionId === 'star') {
        this.onToggleFavorite(file);
      } else if (actionId === 'rename') {
        this.openRenameModal({ type: 'file', id: file.id, name: file.name });
      } else if (actionId === 'delete') {
        this.openDeleteModal({ type: 'file', id: file.id, name: file.name });
      }
    }
  }

  // Bulk Actions
  downloadSelectedZip(): void {
    const fIds = this.selectedFileIds();
    const folderIds = this.selectedFolderIds();
    if (fIds.length === 0 && folderIds.length === 0) return;

    this.toastService.info('Preparing ZIP archive...');
    this.apiService.downloadZip(fIds, folderIds).subscribe({
      next: blob => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `omega-files-${Date.now()}.zip`;
        a.click();
        window.URL.revokeObjectURL(url);
        this.toastService.success('Archive downloaded successfully');
      },
      error: () => {
        this.toastService.error('Failed to download ZIP archive');
      }
    });
  }

  openBulkMoveModal(): void {
    this.bulkMoveTargetFolderId = null;
    this.bulkMoveModalOpen.set(true);
  }

  applyBulkMove(): void {
    const fIds = this.selectedFileIds();
    const folderIds = this.selectedFolderIds();
    if (this.bulkMoveTargetFolderId === this.currentFolderId()) {
      this.toastService.info('Items are already in this folder');
      this.bulkMoveModalOpen.set(false);
      return;
    }
    this.apiService.bulkMove(fIds, folderIds, this.bulkMoveTargetFolderId).subscribe({
      next: () => {
        this.bulkMoveModalOpen.set(false);
        this.clearSelection();
        this.loadCurrentFolder();
        this.toastService.success('Items moved successfully');
      },
      error: (err) => {
        const msg = err?.error?.message || 'Failed to move items';
        this.toastService.error(msg);
      }
    });
  }

  openBulkDeleteModal(): void {
    this.bulkDeleteModalOpen.set(true);
  }

  applyBulkDelete(): void {
    const fIds = this.selectedFileIds();
    const folderIds = this.selectedFolderIds();
    this.apiService.bulkDelete(fIds, folderIds).subscribe({
      next: () => {
        this.bulkDeleteModalOpen.set(false);
        this.clearSelection();
        this.loadCurrentFolder();
        this.toastService.success('Items moved to trash');
      },
      error: (err) => {
        const msg = err?.error?.message || 'Failed to delete items';
        this.toastService.error(msg);
      }
    });
  }

  onSharedNavDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
    this.isSharedNavDragOver.set(true);
  }

  onSharedNavDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const currentTarget = event.currentTarget as HTMLElement;
    const relatedTarget = event.relatedTarget as HTMLElement;
    if (currentTarget && relatedTarget && currentTarget.contains(relatedTarget)) {
      return;
    }
    this.isSharedNavDragOver.set(false);
  }

  onSharedNavDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isSharedNavDragOver.set(false);

    const dragData = this.dragState.getDragData();
    let fileIds: string[] = dragData?.fileIds ? [...dragData.fileIds] : [];
    let folderIds: string[] = dragData?.folderIds ? [...dragData.folderIds] : [];

    if (!fileIds.length && !folderIds.length && event.dataTransfer) {
      const dataStr = event.dataTransfer.getData('application/json');
      if (dataStr) {
        try {
          const parsed = JSON.parse(dataStr);
          fileIds = parsed.fileIds || (parsed.fileId ? [parsed.fileId] : []);
          folderIds = parsed.folderIds || [];
        } catch {
          // ignore
        }
      }
    }

    if (fileIds.length > 0 || folderIds.length > 0) {
      this.apiService.copyToShared(fileIds, folderIds, null).subscribe({
        next: () => {
          this.toastService.success(this.i18n.currentLang() === 'ru' ? 'Скопировано в общий раздел' : 'Copied to Shared Space');
          this.clearSelection();
          this.loadCurrentFolder();
        },
        error: (err) => {
          const msg = err?.error?.message || 'Failed to copy to shared space';
          this.toastService.error(msg);
        }
      });
    }

    this.dragState.endDrag();
  }

  openBulkCopyToSharedModal(): void {
    this.bulkCopyToSharedTargetFolderId = null;
    this.apiService.getSharedFolders(null).subscribe({
      next: (content) => {
        this.availableSharedFolders.set(content.folders || []);
        this.bulkCopyToSharedModalOpen.set(true);
      },
      error: () => {
        this.availableSharedFolders.set([]);
        this.bulkCopyToSharedModalOpen.set(true);
      }
    });
  }

  applyBulkCopyToShared(): void {
    const fIds = this.selectedFileIds();
    const folderIds = this.selectedFolderIds();
    if (fIds.length === 0 && folderIds.length === 0) {
      this.bulkCopyToSharedModalOpen.set(false);
      return;
    }

    this.apiService.copyToShared(fIds, folderIds, this.bulkCopyToSharedTargetFolderId).subscribe({
      next: () => {
        this.bulkCopyToSharedModalOpen.set(false);
        this.clearSelection();
        this.loadCurrentFolder();
        this.toastService.success(this.i18n.currentLang() === 'ru' ? 'Успешно скопировано в Общий раздел' : 'Items copied to Shared Space');
      },
      error: (err) => {
        const msg = err?.error?.message || 'Failed to copy to Shared Space';
        this.toastService.error(msg);
      }
    });
  }

  // Create Folder Modal
  openCreateFolderModal(): void {
    this.newFolderName = '';
    this.newFolderColor = '#0071e3';
    this.createFolderModalOpen.set(true);
  }

  createFolder(): void {
    if (!this.newFolderName.trim()) return;

    this.apiService.createFolder({
      name: this.newFolderName.trim(),
      parentFolderId: this.currentFolderId(),
      color: this.newFolderColor
    }).subscribe({
      next: () => {
        this.createFolderModalOpen.set(false);
        this.loadCurrentFolder();
        this.toastService.success(`Folder '${this.newFolderName.trim()}' created`);
      },
      error: (err) => {
        const msg = err?.error?.message || 'Failed to create folder';
        this.toastService.error(msg);
      }
    });
  }

  // Rename Modal
  openRenameModal(target: { id: string; name: string } | FileItem | { type: 'folder' | 'file'; id: string; name: string }): void {
    const itemType = 'extension' in target ? 'file' : ('type' in target ? target.type : 'file');
    this.renameItemTarget = { type: itemType, id: target.id };
    this.renameItemNewName = target.name;
    this.renameModalOpen.set(true);
  }

  applyRename(): void {
    if (!this.renameItemTarget || !this.renameItemNewName.trim()) return;

    const { type, id } = this.renameItemTarget;
    if (type === 'folder') {
      this.apiService.updateFolder(id, { name: this.renameItemNewName.trim() }).subscribe({
        next: () => {
          this.renameModalOpen.set(false);
          this.loadCurrentFolder();
          this.toastService.success('Folder renamed');
        },
        error: (err) => {
          this.toastService.error(err?.error?.message || 'Failed to rename folder');
        }
      });
    } else {
      this.apiService.updateFile(id, { name: this.renameItemNewName.trim() }).subscribe({
        next: (updated) => {
          this.renameModalOpen.set(false);
          this.loadCurrentFolder();
          if (this.selectedFile()?.id === id) {
            this.selectedFile.set(updated);
          }
          if (this.inspectorFile()?.id === id) {
            this.inspectorFile.set(updated);
          }
          this.toastService.success('File renamed');
        },
        error: (err) => {
          this.toastService.error(err?.error?.message || 'Failed to rename file');
        }
      });
    }
  }

  // Delete Modal
  openDeleteModal(target: { id: string; name: string } | FileItem | { type: 'folder' | 'file'; id: string; name: string }): void {
    const itemType = 'extension' in target ? 'file' : ('type' in target ? target.type : 'file');
    this.deleteItemTarget = { type: itemType, id: target.id, name: target.name };
    this.deleteModalOpen.set(true);
  }

  applyDelete(): void {
    if (!this.deleteItemTarget) return;

    const { type, id } = this.deleteItemTarget;
    if (type === 'folder') {
      this.apiService.deleteFolder(id).subscribe({
        next: () => {
          this.deleteModalOpen.set(false);
          this.loadCurrentFolder();
          this.toastService.success('Folder moved to trash');
        },
        error: (err) => {
          this.toastService.error(err?.error?.message || 'Failed to delete folder');
        }
      });
    } else {
      this.apiService.deleteFile(id).subscribe({
        next: () => {
          this.deleteModalOpen.set(false);
          if (this.selectedFile()?.id === id) {
            this.selectedFile.set(null);
          }
          if (this.inspectorFile()?.id === id) {
            this.inspectorFile.set(null);
          }
          this.loadCurrentFolder();
          this.toastService.success('File moved to trash');
        },
        error: (err) => {
          this.toastService.error(err?.error?.message || 'Failed to delete file');
        }
      });
    }
  }

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
