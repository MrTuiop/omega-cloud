import { Component, OnInit, OnDestroy, inject, signal, computed, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { ThemeService } from '../../../core/services/theme.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { ResumableUploaderService } from '../../../core/services/resumable-uploader.service';
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
    ContextMenuComponent,
    FileGridComponent,
    FileListComponent,
    FileDetailsSheetComponent,
    UploadTrayComponent,
    TrashBinComponent,
    FileSizePipe,
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

      <!-- Left Sidebar -->
      <aside class="sidebar">
        <!-- Logo -->
        <div class="sidebar-brand">
          <div class="brand-icon">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2">
              <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
            </svg>
          </div>
          <span class="brand-text">Omega Cloud</span>
        </div>

        <!-- Navigation Menu -->
        <nav class="sidebar-nav">
          <button
            class="nav-item"
            [class.nav-item--active]="currentTab() === 'files'"
            (click)="selectTab('files')"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
            </svg>
            <span>My Drive</span>
          </button>

          <button
            class="nav-item"
            [class.nav-item--active]="currentTab() === 'favorites'"
            (click)="selectTab('favorites')"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
            </svg>
            <span>Favorites</span>
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
            <span>Trash</span>
          </button>

          @if (authStore.isAdmin()) {
            <a
              routerLink="/admin"
              class="nav-item nav-item--admin"
              title="Admin Console"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
              </svg>
              <span>Admin Console</span>
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
            @if (currentTab() === 'files') {
              <app-breadcrumbs
                [breadcrumbs]="folderContent()?.breadcrumbs || []"
                (navigate)="onNavigateFolder($event)"
              ></app-breadcrumbs>
            } @else if (currentTab() === 'favorites') {
              <h2 class="view-title">Starred Items</h2>
            } @else {
              <h2 class="view-title">Trash</h2>
            }
          </div>

          <div class="header-right">
            <!-- Search bar -->
            <div class="search-box">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
              <input
                type="text"
                placeholder="Search..."
                [ngModel]="searchQuery()"
                (ngModelChange)="searchQuery.set($event)"
              />
              @if (searchQuery()) {
                <button class="clear-search" (click)="searchQuery.set('')">✕</button>
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

              <!-- New Folder & Upload Buttons -->
              <app-glass-button variant="secondary" size="md" (btnClick)="openCreateFolderModal()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                  <line x1="12" y1="11" x2="12" y2="17"></line>
                  <line x1="9" y1="14" x2="15" y2="14"></line>
                </svg>
                New Folder
              </app-glass-button>

              <app-glass-button variant="primary" size="md" (btnClick)="fileInput.click()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                  <polyline points="17 8 12 3 7 8"></polyline>
                  <line x1="12" y1="3" x2="12" y2="15"></line>
                </svg>
                Upload
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
                  [selectedFileId]="selectedFile()?.id || null"
                  (selectFolder)="onSelectFolder($event)"
                  (openFolder)="onOpenFolder($event)"
                  (selectFile)="onSelectFile($event)"
                  (downloadFile)="onDownloadFile($event)"
                  (toggleFavorite)="onToggleFavorite($event)"
                  (folderContextMenu)="onFolderContextMenu($event)"
                  (fileContextMenu)="onFileContextMenu($event)"
                  (folderDrop)="onFolderDrop($event)"
                ></app-file-grid>
              } @else {
                <app-file-list
                  [folders]="filteredFolders()"
                  [files]="filteredFiles()"
                  [selectedFolderId]="selectedFolderId()"
                  [selectedFileId]="selectedFile()?.id || null"
                  (selectFolder)="onSelectFolder($event)"
                  (openFolder)="onOpenFolder($event)"
                  (selectFile)="onSelectFile($event)"
                  (downloadFile)="onDownloadFile($event)"
                  (toggleFavorite)="onToggleFavorite($event)"
                  (folderContextMenu)="onFolderContextMenu($event)"
                  (fileContextMenu)="onFileContextMenu($event)"
                  (folderDrop)="onFolderDrop($event)"
                ></app-file-list>
              }
            }
          }
        </div>
      </main>

      <!-- Right Inspector Sheet -->
      @if (selectedFile()) {
        <app-file-details-sheet
          [file]="selectedFile()"
          (close)="selectedFile.set(null)"
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
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

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
      width: 250px;
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
      padding: 24px 20px 20px;
      display: flex;
      align-items: center;
      gap: 12px;
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

      svg {
        position: absolute;
        left: 12px;
        color: var(--omega-text-muted);
        pointer-events: none;
      }

      input {
        width: 200px;
        height: 38px;
        padding-left: 34px;
        padding-right: 28px;
        border-radius: var(--omega-radius-pill);
      }

      .clear-search {
        position: absolute;
        right: 10px;
        font-size: 11px;
        color: var(--omega-text-muted);

        &:hover {
          color: var(--omega-text-primary);
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
      backdrop-filter: blur(16px);
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
  `]
})
export class DriveLayoutComponent implements OnInit, OnDestroy {
  private readonly apiService = inject(ApiService);
  readonly authStore = inject(AuthStore);
  readonly themeService = inject(ThemeService);
  private readonly signalRService = inject(SignalRService);
  private readonly uploader = inject(ResumableUploaderService);
  private readonly router = inject(Router);

  currentTab = signal<DriveViewTab>('files');
  displayMode = signal<DisplayMode>('grid');

  currentFolderId = signal<string | null>(null);
  folderContent = signal<FolderContent | null>(null);
  loadingFolder = signal<boolean>(false);

  selectedFolderId = signal<string | null>(null);
  selectedFile = signal<FileItem | null>(null);

  searchQuery = signal<string>('');

  isDraggingOver = signal<boolean>(false);
  private dragCounter = 0;

  // Context Menu State
  contextMenuOpen = signal<boolean>(false);
  contextMenuX = signal<number>(0);
  contextMenuY = signal<number>(0);
  contextMenuItems = signal<ContextMenuItem[]>([]);
  private contextTargetFolder: FolderItem | null = null;
  private contextTargetFile: FileItem | null = null;

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

  private subs: Subscription[] = [];

  readonly currentFolderName = computed(() => {
    return this.folderContent()?.currentFolder?.name || 'Root Drive';
  });

  readonly userInitials = computed(() => {
    const user = this.authStore.currentUser();
    if (!user || !user.username) return 'U';
    return user.username.slice(0, 2).toUpperCase();
  });

  readonly filteredFolders = computed(() => {
    const content = this.folderContent();
    if (!content) return [];
    if (this.currentTab() === 'favorites') return [];
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return content.folders;
    return content.folders.filter(f => f.name.toLowerCase().includes(q));
  });

  readonly filteredFiles = computed(() => {
    const content = this.folderContent();
    if (!content) return [];
    let list = content.files;
    if (this.currentTab() === 'favorites') {
      list = list.filter(f => f.isFavorite);
    }
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return list;
    return list.filter(f => f.name.toLowerCase().includes(q));
  });

  ngOnInit(): void {
    this.loadCurrentFolder();

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

  selectTab(tab: DriveViewTab): void {
    this.currentTab.set(tab);
    this.selectedFile.set(null);
    this.selectedFolderId.set(null);
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
    const prevFolderId = this.currentFolderId();
    if (prevFolderId !== folderId) {
      this.signalRService.unsubscribeFromFolder(prevFolderId);
      this.signalRService.subscribeToFolder(folderId);
    }
    this.currentFolderId.set(folderId);
    this.selectedFile.set(null);
    this.selectedFolderId.set(null);
    this.loadCurrentFolder();
  }

  onSelectFolder(folder: FolderItem): void {
    this.selectedFolderId.set(folder.id);
    this.selectedFile.set(null);
  }

  onOpenFolder(folder: FolderItem): void {
    this.onNavigateFolder(folder.id);
  }

  onSelectFile(file: FileItem): void {
    this.selectedFile.set(file);
    this.selectedFolderId.set(null);
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
      },
      error: () => {
        // Fallback to direct download endpoint
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
      }
    });
  }

  // Drag and Drop (from OS into Window)
  onWindowDragEnter(event: DragEvent): void {
    event.preventDefault();
    this.dragCounter++;
    if (event.dataTransfer?.types?.includes('Files')) {
      this.isDraggingOver.set(true);
    }
  }

  onWindowDragOver(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  onWindowDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragCounter--;
    if (this.dragCounter <= 0) {
      this.isDraggingOver.set(false);
      this.dragCounter = 0;
    }
  }

  onWindowDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDraggingOver.set(false);
    this.dragCounter = 0;

    if (event.dataTransfer?.files && event.dataTransfer.files.length > 0) {
      this.uploader.enqueue(
        event.dataTransfer.files,
        this.currentFolderId(),
        this.currentFolderName()
      );
    }
  }

  onFileInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.uploader.enqueue(
        input.files,
        this.currentFolderId(),
        this.currentFolderName()
      );
      input.value = '';
    }
  }

  // Drag-and-drop file onto folder to move file
  onFolderDrop(payload: { targetFolder: FolderItem; fileId: string }): void {
    this.apiService.updateFile(payload.fileId, { folderId: payload.targetFolder.id }).subscribe({
      next: () => {
        this.loadCurrentFolder();
      }
    });
  }

  // Context Menu Handlers
  onFolderContextMenu(payload: { event: MouseEvent; folder: FolderItem }): void {
    this.contextTargetFolder = payload.folder;
    this.contextTargetFile = null;
    this.contextMenuX.set(payload.event.clientX);
    this.contextMenuY.set(payload.event.clientY);

    this.contextMenuItems.set([
      { id: 'open', label: 'Open Folder' },
      { id: 'rename', label: 'Rename' },
      { id: 'delete', label: 'Move to Trash', danger: true, dividerBefore: true }
    ]);
    this.contextMenuOpen.set(true);
  }

  onFileContextMenu(payload: { event: MouseEvent; file: FileItem }): void {
    this.contextTargetFile = payload.file;
    this.contextTargetFolder = null;
    this.contextMenuX.set(payload.event.clientX);
    this.contextMenuY.set(payload.event.clientY);

    this.contextMenuItems.set([
      { id: 'download', label: 'Download' },
      { id: 'details', label: 'Inspector' },
      { id: 'star', label: payload.file.isFavorite ? 'Remove from Favorites' : 'Add to Favorites' },
      { id: 'rename', label: 'Rename' },
      { id: 'delete', label: 'Move to Trash', danger: true, dividerBefore: true }
    ]);
    this.contextMenuOpen.set(true);
  }

  onContextMenuItemClick(actionId: string): void {
    if (this.contextTargetFolder) {
      const folder = this.contextTargetFolder;
      if (actionId === 'open') {
        this.onOpenFolder(folder);
      } else if (actionId === 'rename') {
        this.openRenameModal({ type: 'folder', id: folder.id, name: folder.name });
      } else if (actionId === 'delete') {
        this.openDeleteModal({ type: 'folder', id: folder.id, name: folder.name });
      }
    } else if (this.contextTargetFile) {
      const file = this.contextTargetFile;
      if (actionId === 'download') {
        this.onDownloadFile(file);
      } else if (actionId === 'details') {
        this.selectedFile.set(file);
      } else if (actionId === 'star') {
        this.onToggleFavorite(file);
      } else if (actionId === 'rename') {
        this.openRenameModal({ type: 'file', id: file.id, name: file.name });
      } else if (actionId === 'delete') {
        this.openDeleteModal({ type: 'file', id: file.id, name: file.name });
      }
    }
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
        }
      });
    } else {
      this.apiService.updateFile(id, { name: this.renameItemNewName.trim() }).subscribe({
        next: () => {
          this.renameModalOpen.set(false);
          this.loadCurrentFolder();
          if (this.selectedFile()?.id === id) {
            this.selectedFile.update(f => f ? { ...f, name: this.renameItemNewName.trim() } : f);
          }
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
        }
      });
    } else {
      this.apiService.deleteFile(id).subscribe({
        next: () => {
          this.deleteModalOpen.set(false);
          if (this.selectedFile()?.id === id) {
            this.selectedFile.set(null);
          }
          this.loadCurrentFolder();
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
