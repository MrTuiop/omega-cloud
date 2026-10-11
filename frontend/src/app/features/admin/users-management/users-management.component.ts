import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';
import {
  AdminUser,
  CreateUserRequest,
  UpdateUserRequest,
  AdminResetPasswordRequest
} from '../../../core/models/admin.models';
import { UserSession } from '../../../core/models/auth.models';
import { GlassCardComponent } from '../../../shared/components/glass-card/glass-card.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';

@Component({
  selector: 'app-users-management',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    GlassCardComponent,
    GlassButtonComponent,
    GlassModalComponent,
    FileSizePipe,
    RelativeTimePipe
  ],
  template: `
    <div class="page-container anim-fade-in">
      <!-- Header -->
      <div class="page-header">
        <div class="header-titles">
          <h1 class="page-title">Users & Quotas</h1>
          <p class="page-subtitle">Manage accounts, storage quotas, device sessions, and role permissions.</p>
        </div>
        <div class="header-actions">
          <app-glass-button variant="secondary" size="md" (btnClick)="loadUsers()">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67" />
            </svg>
            Refresh
          </app-glass-button>
          <app-glass-button variant="primary" size="md" (btnClick)="openCreateUserModal()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
            Create User
          </app-glass-button>
        </div>
      </div>

      <!-- Overview Cards -->
      <div class="stats-grid">
        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Total Users</div>
            <div class="stat-value">{{ totalUsersCount() }}</div>
            <div class="stat-sub">{{ activeUsersCount() }} active accounts</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Active Sessions</div>
            <div class="stat-value">{{ totalSessionsCount() }}</div>
            <div class="stat-sub">Connected client devices</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Total Quota Allocated</div>
            <div class="stat-value">{{ totalAllocatedQuota() | fileSize }}</div>
            <div class="stat-sub">Across all registered users</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Total Quota Used</div>
            <div class="stat-value">{{ totalUsedQuota() | fileSize }}</div>
            <div class="stat-sub">{{ fleetQuotaPercent() }}% of total capacity</div>
          </div>
        </app-glass-card>
      </div>

      <!-- Filters & Search Toolbar -->
      <div class="toolbar">
        <div class="search-input-wrapper">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"></circle>
            <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
          </svg>
          <input
            type="text"
            placeholder="Search by username or email..."
            [ngModel]="searchQuery()"
            (ngModelChange)="searchQuery.set($event)"
          />
          @if (searchQuery()) {
            <button class="clear-btn" (click)="searchQuery.set('')">✕</button>
          }
        </div>

        <div class="filters">
          <select [ngModel]="roleFilter()" (ngModelChange)="roleFilter.set($event)" class="glass-select">
            <option value="All">All Roles</option>
            <option value="Admin">Admin</option>
            <option value="User">User</option>
          </select>

          <select [ngModel]="statusFilter()" (ngModelChange)="statusFilter.set($event)" class="glass-select">
            <option value="All">All Statuses</option>
            <option value="Active">Active Only</option>
            <option value="Inactive">Inactive Only</option>
          </select>
        </div>
      </div>

      <!-- Users Table Card -->
      <app-glass-card padding="0px" [elevated]="true">
        @if (isLoading()) {
          <div class="loading-state">
            <div class="loading-spinner"></div>
            <span>Loading users...</span>
          </div>
        } @else if (filteredUsers().length === 0) {
          <div class="empty-state">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
              <circle cx="9" cy="7" r="4"></circle>
            </svg>
            <h3>No users found</h3>
            <p>Try adjusting your search query or filters.</p>
          </div>
        } @else {
          <div class="table-wrapper">
            <table class="glass-table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Role</th>
                  <th>Storage Quota</th>
                  <th>Status</th>
                  <th>Active Sessions</th>
                  <th>Last Active</th>
                  <th>Created</th>
                  <th class="actions-header">Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (u of filteredUsers(); track u.id) {
                  <tr class="table-row">
                    <td>
                      <div class="user-cell">
                        <div class="user-avatar" [class.user-avatar--admin]="u.role.toLowerCase() === 'admin'">
                          {{ u.username.slice(0, 2).toUpperCase() }}
                        </div>
                        <div class="user-meta">
                          <span class="user-name">{{ u.username }}</span>
                          <span class="user-email">{{ u.email }}</span>
                        </div>
                      </div>
                    </td>
                    <td>
                      <span class="role-pill" [class]="'role-pill--' + u.role.toLowerCase()">
                        {{ u.role }}
                      </span>
                    </td>
                    <td>
                      <div class="quota-cell">
                        <div class="quota-progress-track">
                          <div
                            class="quota-progress-bar"
                            [style.width.%]="u.quotaPercent"
                            [class.quota-progress-bar--warning]="u.quotaPercent > 80"
                            [class.quota-progress-bar--danger]="u.quotaPercent > 95"
                          ></div>
                        </div>
                        <div class="quota-text">
                          {{ u.usedQuotaBytes | fileSize }} / {{ u.allocatedQuotaBytes | fileSize }} ({{ u.quotaPercent }}%)
                        </div>
                      </div>
                    </td>
                    <td>
                      @if (u.isActive) {
                        <span class="status-pill status-pill--active">
                          <span class="status-dot"></span> Active
                        </span>
                      } @else {
                        <span class="status-pill status-pill--inactive">
                          <span class="status-dot"></span> Inactive
                        </span>
                      }
                    </td>
                    <td>
                      <div
                        class="sessions-badge sessions-badge--clickable"
                        title="Inspect & manage active sessions"
                        (click)="openSessionsModal(u)"
                      >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect>
                          <line x1="8" y1="21" x2="16" y2="21"></line>
                          <line x1="12" y1="17" x2="12" y2="21"></line>
                        </svg>
                        <span>{{ u.activeSessionsCount }}</span>
                      </div>
                    </td>
                    <td>
                      <span class="created-text" [title]="u.lastActiveAt || u.createdAt">
                        {{ u.lastActiveAt ? (u.lastActiveAt | relativeTime) : (u.createdAt | relativeTime) }}
                      </span>
                    </td>
                    <td>
                      <span class="created-text" [title]="u.createdAt">
                        {{ u.createdAt | relativeTime }}
                      </span>
                    </td>
                    <td class="actions-cell">
                      <div class="row-actions">
                        <button class="action-btn" title="Inspect & Manage Sessions" (click)="openSessionsModal(u)">
                          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect>
                            <line x1="8" y1="21" x2="16" y2="21"></line>
                            <line x1="12" y1="17" x2="12" y2="21"></line>
                          </svg>
                        </button>

                        <button class="action-btn" title="Edit User & Quota" (click)="openEditModal(u)">
                          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M12 20h9"></path>
                            <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"></path>
                          </svg>
                        </button>

                        <button class="action-btn" title="Reset Password" (click)="openResetPasswordModal(u)">
                          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
                            <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
                          </svg>
                        </button>

                        <button
                          class="action-btn"
                          title="Terminate Sessions"
                          (click)="openRevokeModal(u)"
                          [disabled]="u.activeSessionsCount === 0"
                        >
                          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M18.36 6.64a9 9 0 1 1-12.73 0"></path>
                            <line x1="12" y1="2" x2="12" y2="12"></line>
                          </svg>
                        </button>

                        <button
                          class="action-btn action-btn--danger"
                          title="Delete User"
                          [disabled]="u.id === authStore.currentUser()?.id"
                          (click)="openDeleteModal(u)"
                        >
                          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="3 6 5 6 21 6"></polyline>
                            <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                          </svg>
                        </button>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </app-glass-card>

      <!-- Create User Modal -->
      <app-glass-modal
        [isOpen]="createUserModalOpen()"
        title="Create New User"
        (close)="createUserModalOpen.set(false)"
      >
        <div class="modal-form">
          <label>Username</label>
          <input
            type="text"
            [(ngModel)]="newUsername"
            placeholder="e.g. john_doe"
            autocomplete="off"
          />

          <label>Email Address</label>
          <input
            type="email"
            [(ngModel)]="newEmail"
            placeholder="e.g. john@example.com"
            autocomplete="off"
          />

          <label>Password</label>
          <input
            type="password"
            [(ngModel)]="newPassword"
            placeholder="Minimum 6 characters"
            autocomplete="new-password"
          />

          <label>Role</label>
          <select [(ngModel)]="newRole" class="modal-select">
            <option value="User">User</option>
            <option value="Admin">Admin</option>
          </select>

          <label>Allocated Quota</label>
          <div class="preset-buttons">
            @for (p of quotaPresets; track p.bytes) {
              <button
                type="button"
                class="preset-btn"
                [class.preset-btn--active]="newQuotaBytes === p.bytes"
                (click)="newQuotaBytes = p.bytes"
              >
                {{ p.label }}
              </button>
            }
          </div>
          <input
            type="number"
            [(ngModel)]="newQuotaGigabytes"
            (ngModelChange)="onGigabytesChange($event)"
            placeholder="Custom Quota in GB"
          />

          @if (formError()) {
            <div class="form-error">{{ formError() }}</div>
          }
        </div>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="createUserModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button
            variant="primary"
            [disabled]="isSaving() || !newUsername.trim() || !newEmail.trim() || !newPassword"
            [loading]="isSaving()"
            (btnClick)="createUser()"
          >
            Create User
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Edit User Modal -->
      <app-glass-modal
        [isOpen]="editUserModalOpen()"
        [title]="'Edit ' + (selectedUser()?.username || 'User')"
        (close)="editUserModalOpen.set(false)"
      >
        @if (selectedUser()) {
          <div class="modal-form">
            <label>Role</label>
            <select
              [(ngModel)]="editRole"
              class="modal-select"
              [disabled]="selectedUser()?.id === authStore.currentUser()?.id"
            >
              <option value="User">User</option>
              <option value="Admin">Admin</option>
            </select>

            <label>Storage Quota</label>
            <div class="preset-buttons">
              @for (p of quotaPresets; track p.bytes) {
                <button
                  type="button"
                  class="preset-btn"
                  [class.preset-btn--active]="editQuotaBytes === p.bytes"
                  (click)="editQuotaBytes = p.bytes"
                >
                  {{ p.label }}
                </button>
              }
            </div>
            <input
              type="number"
              [(ngModel)]="editQuotaGigabytes"
              (ngModelChange)="onEditGigabytesChange($event)"
              placeholder="Custom Quota in GB"
            />

            <label class="checkbox-label" [class.checkbox-label--disabled]="selectedUser()?.id === authStore.currentUser()?.id">
              <input
                type="checkbox"
                [(ngModel)]="editIsActive"
                [disabled]="selectedUser()?.id === authStore.currentUser()?.id"
              />
              <span>Active Account (Users can log in and upload files)</span>
            </label>

            @if (selectedUser()?.id === authStore.currentUser()?.id) {
              <div class="modal-notice">Current administrator cannot deactivate or remove admin permissions from their own account.</div>
            }

            @if (formError()) {
              <div class="form-error">{{ formError() }}</div>
            }
          </div>
        }

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="editUserModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button
            variant="primary"
            [disabled]="isSaving()"
            [loading]="isSaving()"
            (btnClick)="saveUserEdit()"
          >
            Save Changes
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Reset Password Modal -->
      <app-glass-modal
        [isOpen]="resetPasswordModalOpen()"
        [title]="'Reset Password for ' + (selectedUser()?.username || 'User')"
        (close)="resetPasswordModalOpen.set(false)"
      >
        <div class="modal-form">
          <p class="modal-note">
            Entering a new password will immediately terminate all active sessions for this user.
          </p>

          <label>New Password</label>
          <input
            type="password"
            [(ngModel)]="resetPasswordValue"
            placeholder="At least 6 characters"
            autocomplete="new-password"
          />

          @if (formError()) {
            <div class="form-error">{{ formError() }}</div>
          }
        </div>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="resetPasswordModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button
            variant="primary"
            [disabled]="isSaving() || resetPasswordValue.length < 6"
            [loading]="isSaving()"
            (btnClick)="saveResetPassword()"
          >
            Set Password
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Revoke Sessions Modal -->
      <app-glass-modal
        [isOpen]="revokeModalOpen()"
        title="Terminate Sessions"
        (close)="revokeModalOpen.set(false)"
      >
        <p>
          Are you sure you want to terminate all <strong>{{ selectedUser()?.activeSessionsCount }}</strong> active sessions for user
          <strong>{{ selectedUser()?.username }}</strong>? They will be logged out immediately.
        </p>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="revokeModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="danger" [loading]="isSaving()" (btnClick)="confirmRevokeSessions()">
            Terminate Sessions
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Inspect User Sessions Modal -->
      <app-glass-modal
        [isOpen]="sessionsModalOpen()"
        [title]="'Active Sessions: ' + (selectedUser()?.username || 'User')"
        (close)="sessionsModalOpen.set(false)"
      >
        <div class="sessions-modal-content">
          @if (isLoadingSessions()) {
            <div class="loading-state">
              <div class="loading-spinner"></div>
              <span>Loading active sessions...</span>
            </div>
          } @else if (userSessions().length === 0) {
            <div class="empty-sessions">
              <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect>
                <line x1="8" y1="21" x2="16" y2="21"></line>
                <line x1="12" y1="17" x2="12" y2="21"></line>
              </svg>
              <h4>No active sessions</h4>
              <p>User is not currently logged in on any device.</p>
            </div>
          } @else {
            <div class="sessions-list">
              @for (s of userSessions(); track s.id) {
                <div class="session-item" [class.session-item--current]="s.isCurrent">
                  <div class="session-icon">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect>
                      <line x1="8" y1="21" x2="16" y2="21"></line>
                      <line x1="12" y1="17" x2="12" y2="21"></line>
                    </svg>
                  </div>
                  <div class="session-info">
                    <div class="session-title-line">
                      <span class="session-device">{{ s.deviceName || 'Web Client' }}</span>
                      @if (s.isCurrent) {
                        <span class="current-badge">Current Device</span>
                      }
                    </div>
                    <div class="session-details-line">
                      <span class="session-ip">{{ s.ipAddress }}</span>
                      <span class="session-dot">•</span>
                      <span class="session-time" [title]="s.lastActiveAt">Active {{ s.lastActiveAt | relativeTime }}</span>
                    </div>
                    @if (s.userAgent) {
                      <div class="session-agent" [title]="s.userAgent">{{ s.userAgent }}</div>
                    }
                  </div>
                  <div class="session-actions">
                    <app-glass-button
                      variant="danger"
                      size="sm"
                      [loading]="revokingSessionId() === s.id"
                      (btnClick)="revokeSingleSession(s.id)"
                    >
                      Revoke
                    </app-glass-button>
                  </div>
                </div>
              }
            </div>
          }
        </div>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="sessionsModalOpen.set(false)">Close</app-glass-button>
          @if (userSessions().length > 0) {
            <app-glass-button variant="danger" [loading]="isSaving()" (btnClick)="confirmRevokeSessions()">
              Terminate All ({{ userSessions().length }})
            </app-glass-button>
          }
        </div>
      </app-glass-modal>

      <!-- Delete User Modal -->
      <app-glass-modal
        [isOpen]="deleteModalOpen()"
        title="Delete User Account"
        (close)="deleteModalOpen.set(false)"
      >
        <p>
          Are you sure you want to permanently delete user <strong>{{ selectedUser()?.username }}</strong>?
          All associated data, files, and sessions will be removed.
        </p>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="deleteModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="danger" [loading]="isSaving()" (btnClick)="confirmDeleteUser()">
            Delete User
          </app-glass-button>
        </div>
      </app-glass-modal>
    </div>
  `,
  styles: [`
    .page-container {
      padding: 28px 36px;
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 20px;
    }

    .page-title {
      font-size: 24px;
      font-weight: 700;
      color: var(--omega-text-primary);
      letter-spacing: -0.01em;
    }

    .page-subtitle {
      font-size: 14px;
      color: var(--omega-text-secondary);
      margin-top: 4px;
    }

    .header-actions {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 18px;
    }

    .stat-card {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .stat-label {
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--omega-text-muted);
    }

    .stat-value {
      font-size: 26px;
      font-weight: 700;
      color: var(--omega-text-primary);
    }

    .stat-sub {
      font-size: 12px;
      color: var(--omega-text-secondary);
    }

    .toolbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
    }

    .search-input-wrapper {
      position: relative;
      display: flex;
      align-items: center;
      width: 320px;

      svg {
        position: absolute;
        left: 12px;
        color: var(--omega-text-muted);
        pointer-events: none;
      }

      input {
        width: 100%;
        height: 40px;
        padding-left: 36px;
        padding-right: 32px;
        border-radius: var(--omega-radius-pill);
      }

      .clear-btn {
        position: absolute;
        right: 12px;
        font-size: 11px;
        color: var(--omega-text-muted);

        &:hover {
          color: var(--omega-text-primary);
        }
      }
    }

    .filters {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .glass-select, .modal-select {
      height: 40px;
      padding: 0 14px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-glass-bg-sunken);
      border: 1px solid var(--omega-glass-border);
      color: var(--omega-text-primary);
      outline: none;
      cursor: pointer;
    }

    .table-wrapper {
      overflow-x: auto;
    }

    .glass-table {
      width: 100%;
      border-collapse: collapse;
      text-align: left;
      font-size: 13px;

      th {
        padding: 14px 18px;
        font-size: 11px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: var(--omega-text-muted);
        border-bottom: 1px solid var(--omega-glass-border-subtle);
        background: rgba(255, 255, 255, 0.02);
      }

      td {
        padding: 16px 18px;
        border-bottom: 1px solid var(--omega-glass-border-subtle);
        vertical-align: middle;
      }

      .actions-header {
        text-align: right;
      }
    }

    .table-row {
      transition: background 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
      }
    }

    .user-cell {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .user-avatar {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: var(--omega-accent);
      color: #ffffff;
      font-size: 13px;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;

      &--admin {
        background: linear-gradient(135deg, #bf5af2, #5e5ce6);
      }
    }

    .user-meta {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .user-name {
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .user-email {
      font-size: 12px;
      color: var(--omega-text-muted);
    }

    .role-pill {
      display: inline-block;
      padding: 3px 10px;
      border-radius: var(--omega-radius-pill);
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;

      &--admin {
        background: rgba(191, 90, 242, 0.15);
        color: #bf5af2;
        border: 1px solid rgba(191, 90, 242, 0.3);
      }

      &--user {
        background: rgba(0, 113, 227, 0.15);
        color: var(--omega-accent);
        border: 1px solid rgba(0, 113, 227, 0.3);
      }
    }

    .quota-cell {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 170px;
    }

    .quota-progress-track {
      width: 100%;
      height: 5px;
      background: rgba(120, 120, 128, 0.2);
      border-radius: var(--omega-radius-pill);
      overflow: hidden;
    }

    .quota-progress-bar {
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

    .status-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 500;

      &--active {
        color: var(--omega-success);
        .status-dot {
          background: var(--omega-success);
          box-shadow: 0 0 8px var(--omega-success);
        }
      }

      &--inactive {
        color: var(--omega-danger);
        .status-dot {
          background: var(--omega-danger);
        }
      }
    }

    .status-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
    }

    .sessions-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: var(--omega-radius-pill);
      background: var(--omega-glass-bg-sunken);
      border: 1px solid var(--omega-glass-border-subtle);
      font-size: 12px;
      font-weight: 600;
      color: var(--omega-text-secondary);

      &--clickable {
        cursor: pointer;
        transition: all 0.2s;
        &:hover {
          color: var(--omega-accent);
          border-color: var(--omega-accent);
        }
      }
    }

    .created-text {
      font-size: 12px;
      color: var(--omega-text-muted);
    }

    .actions-cell {
      text-align: right;
    }

    .row-actions {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }

    .action-btn {
      width: 32px;
      height: 32px;
      border-radius: var(--omega-radius-sm);
      display: inline-flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);
      transition: all 0.15s;

      &:hover:not(:disabled) {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }

      &--danger:hover:not(:disabled) {
        background: rgba(255, 69, 58, 0.15);
        color: var(--omega-danger);
      }

      &:disabled {
        opacity: 0.3;
        cursor: not-allowed;
      }
    }

    .loading-state, .empty-state {
      padding: 60px;
      text-align: center;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      color: var(--omega-text-muted);

      h3 {
        color: var(--omega-text-primary);
        font-size: 18px;
        font-weight: 600;
      }

      p {
        font-size: 14px;
      }
    }

    .loading-spinner {
      width: 30px;
      height: 30px;
      border: 3px solid rgba(120, 120, 128, 0.2);
      border-top-color: var(--omega-accent);
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    // Modal Form styling
    .modal-form {
      display: flex;
      flex-direction: column;
      gap: 12px;

      label {
        font-size: 13px;
        font-weight: 500;
        color: var(--omega-text-secondary);
      }

      input[type="text"], input[type="email"], input[type="password"], input[type="number"] {
        width: 100%;
        height: 42px;
      }
    }

    .modal-note {
      font-size: 13px;
      color: var(--omega-text-secondary);
      line-height: 1.4;
      padding: 10px 14px;
      background: var(--omega-glass-bg-sunken);
      border: 1px solid var(--omega-glass-border-subtle);
      border-radius: var(--omega-radius-md);
    }

    .preset-buttons {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .preset-btn {
      padding: 6px 12px;
      border-radius: var(--omega-radius-md);
      font-size: 12px;
      font-weight: 500;
      background: var(--omega-glass-bg);
      border: 1px solid var(--omega-glass-border);
      color: var(--omega-text-secondary);
      transition: all 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }

      &--active {
        background: var(--omega-accent-subtle);
        color: var(--omega-accent);
        border-color: var(--omega-accent);
        font-weight: 600;
      }
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      font-size: 13px;
      color: var(--omega-text-primary);
      margin-top: 6px;

      &--disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      input[type="checkbox"] {
        width: 18px;
        height: 18px;
        cursor: pointer;
      }
    }

    .modal-notice {
      font-size: 12px;
      color: var(--omega-warning);
      margin-top: 6px;
      padding: 6px 10px;
      border-radius: var(--omega-radius-sm);
      background: rgba(255, 159, 10, 0.1);
      border: 1px solid rgba(255, 159, 10, 0.25);
    }

    .form-error {
      padding: 10px 14px;
      border-radius: var(--omega-radius-md);
      background: rgba(255, 69, 58, 0.15);
      border: 1px solid rgba(255, 69, 58, 0.3);
      color: var(--omega-danger);
      font-size: 13px;
    }

    .sessions-modal-content {
      display: flex;
      flex-direction: column;
      gap: 12px;
      max-height: 420px;
      overflow-y: auto;
    }

    .empty-sessions {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 30px 20px;
      color: var(--omega-text-muted);
      gap: 8px;
      text-align: center;

      h4 {
        margin: 0;
        font-size: 15px;
        color: var(--omega-text-primary);
      }

      p {
        margin: 0;
        font-size: 13px;
      }
    }

    .sessions-list {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .session-item {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-glass-bg-sunken);
      border: 1px solid var(--omega-glass-border-subtle);

      &--current {
        border-color: var(--omega-accent);
      }
    }

    .session-icon {
      width: 36px;
      height: 36px;
      border-radius: var(--omega-radius-sm);
      background: var(--omega-glass-bg);
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-secondary);
      flex-shrink: 0;
    }

    .session-info {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .session-title-line {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .session-device {
      font-size: 13px;
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .current-badge {
      font-size: 10px;
      padding: 2px 6px;
      border-radius: var(--omega-radius-pill);
      background: rgba(0, 113, 227, 0.15);
      color: var(--omega-accent);
      font-weight: 600;
    }

    .session-details-line {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: var(--omega-text-secondary);
    }

    .session-dot {
      color: var(--omega-text-muted);
    }

    .session-agent {
      font-size: 10px;
      color: var(--omega-text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      max-width: 340px;
    }
  `]
})
export class UsersManagementComponent implements OnInit {
  private readonly apiService = inject(ApiService);
  readonly authStore = inject(AuthStore);

  users = signal<AdminUser[]>([]);
  isLoading = signal<boolean>(false);
  isSaving = signal<boolean>(false);
  formError = signal<string | null>(null);

  searchQuery = signal<string>('');
  roleFilter = signal<string>('All');
  statusFilter = signal<string>('All');

  // Modals state
  createUserModalOpen = signal<boolean>(false);
  editUserModalOpen = signal<boolean>(false);
  resetPasswordModalOpen = signal<boolean>(false);
  revokeModalOpen = signal<boolean>(false);
  deleteModalOpen = signal<boolean>(false);
  sessionsModalOpen = signal<boolean>(false);
  isLoadingSessions = signal<boolean>(false);
  userSessions = signal<UserSession[]>([]);
  revokingSessionId = signal<string | null>(null);

  selectedUser = signal<AdminUser | null>(null);

  // Create Form
  newUsername = '';
  newEmail = '';
  newPassword = '';
  newRole = 'User';
  newQuotaBytes = 107_374_182_400; // 100 GB default
  newQuotaGigabytes: number = 100;

  // Edit Form
  editRole = 'User';
  editQuotaBytes = 107_374_182_400;
  editQuotaGigabytes: number = 100;
  editIsActive = true;

  // Reset Password Form
  resetPasswordValue = '';

  readonly quotaPresets = [
    { label: '10 GB', bytes: 10_737_418_240 },
    { label: '50 GB', bytes: 53_687_091_200 },
    { label: '100 GB', bytes: 107_374_182_400 },
    { label: '500 GB', bytes: 536_870_912_000 },
    { label: '1 TB', bytes: 1_099_511_627_776 }
  ];

  readonly totalUsersCount = computed(() => this.users().length);
  readonly activeUsersCount = computed(() => this.users().filter(u => u.isActive).length);
  readonly totalSessionsCount = computed(() => this.users().reduce((acc, u) => acc + u.activeSessionsCount, 0));
  readonly totalAllocatedQuota = computed(() => this.users().reduce((acc, u) => acc + u.allocatedQuotaBytes, 0));
  readonly totalUsedQuota = computed(() => this.users().reduce((acc, u) => acc + u.usedQuotaBytes, 0));
  readonly fleetQuotaPercent = computed(() => {
    const total = this.totalAllocatedQuota();
    if (total <= 0) return 0;
    return Math.min(100, Math.round((this.totalUsedQuota() / total) * 1000) / 10);
  });

  readonly filteredUsers = computed(() => {
    let list = this.users();
    const q = this.searchQuery().trim().toLowerCase();
    if (q) {
      list = list.filter(u => u.username.toLowerCase().includes(q) || u.email.toLowerCase().includes(q));
    }
    const role = this.roleFilter();
    if (role !== 'All') {
      list = list.filter(u => u.role.toLowerCase() === role.toLowerCase());
    }
    const status = this.statusFilter();
    if (status === 'Active') {
      list = list.filter(u => u.isActive);
    } else if (status === 'Inactive') {
      list = list.filter(u => !u.isActive);
    }
    return list;
  });

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.isLoading.set(true);
    this.apiService.getAdminUsers().subscribe({
      next: users => {
        this.users.set(users);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  onGigabytesChange(gb: number): void {
    if (gb && gb > 0) {
      this.newQuotaBytes = Math.round(gb * 1024 * 1024 * 1024);
    }
  }

  onEditGigabytesChange(gb: number): void {
    if (gb && gb > 0) {
      this.editQuotaBytes = Math.round(gb * 1024 * 1024 * 1024);
    }
  }

  openCreateUserModal(): void {
    this.newUsername = '';
    this.newEmail = '';
    this.newPassword = '';
    this.newRole = 'User';
    this.newQuotaBytes = 107_374_182_400;
    this.newQuotaGigabytes = 100;
    this.formError.set(null);
    this.createUserModalOpen.set(true);
  }

  createUser(): void {
    if (!this.newUsername.trim() || !this.newEmail.trim() || !this.newPassword) return;

    this.isSaving.set(true);
    this.formError.set(null);

    const req: CreateUserRequest = {
      username: this.newUsername.trim(),
      email: this.newEmail.trim(),
      password: this.newPassword,
      role: this.newRole,
      allocatedQuotaBytes: this.newQuotaBytes
    };

    this.apiService.createAdminUser(req).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.createUserModalOpen.set(false);
        this.loadUsers();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.formError.set(err?.error?.message || 'Failed to create user.');
      }
    });
  }

  openEditModal(user: AdminUser): void {
    this.selectedUser.set(user);
    this.editRole = user.role;
    this.editQuotaBytes = user.allocatedQuotaBytes;
    this.editQuotaGigabytes = Math.round(user.allocatedQuotaBytes / (1024 * 1024 * 1024));
    this.editIsActive = user.isActive;
    this.formError.set(null);
    this.editUserModalOpen.set(true);
  }

  saveUserEdit(): void {
    const user = this.selectedUser();
    if (!user) return;

    this.isSaving.set(true);
    this.formError.set(null);

    const req: UpdateUserRequest = {
      role: this.editRole,
      allocatedQuotaBytes: this.editQuotaBytes,
      isActive: this.editIsActive
    };

    this.apiService.updateAdminUser(user.id, req).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.editUserModalOpen.set(false);
        this.loadUsers();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.formError.set(err?.error?.message || 'Failed to update user.');
      }
    });
  }

  openResetPasswordModal(user: AdminUser): void {
    this.selectedUser.set(user);
    this.resetPasswordValue = '';
    this.formError.set(null);
    this.resetPasswordModalOpen.set(true);
  }

  saveResetPassword(): void {
    const user = this.selectedUser();
    if (!user || this.resetPasswordValue.length < 6) return;

    this.isSaving.set(true);
    this.formError.set(null);

    const req: AdminResetPasswordRequest = {
      newPassword: this.resetPasswordValue
    };

    this.apiService.resetAdminUserPassword(user.id, req).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.resetPasswordModalOpen.set(false);
        this.loadUsers();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.formError.set(err?.error?.message || 'Failed to reset password.');
      }
    });
  }

  openRevokeModal(user: AdminUser): void {
    this.selectedUser.set(user);
    this.revokeModalOpen.set(true);
  }

  openSessionsModal(user: AdminUser): void {
    this.selectedUser.set(user);
    this.userSessions.set([]);
    this.isLoadingSessions.set(true);
    this.sessionsModalOpen.set(true);

    this.apiService.getAdminUser(user.id).subscribe({
      next: detail => {
        this.userSessions.set(detail.sessions || []);
        this.isLoadingSessions.set(false);
      },
      error: () => {
        this.isLoadingSessions.set(false);
      }
    });
  }

  revokeSingleSession(sessionId: string): void {
    const user = this.selectedUser();
    if (!user) return;

    this.revokingSessionId.set(sessionId);
    this.apiService.revokeAdminUserSession(user.id, sessionId).subscribe({
      next: () => {
        this.revokingSessionId.set(null);
        this.userSessions.update(list => list.filter(s => s.id !== sessionId));
        this.users.update(list => list.map(u => u.id === user.id ? { ...u, activeSessionsCount: Math.max(0, u.activeSessionsCount - 1) } : u));
      },
      error: () => {
        this.revokingSessionId.set(null);
      }
    });
  }

  confirmRevokeSessions(): void {
    const user = this.selectedUser();
    if (!user) return;

    this.isSaving.set(true);
    this.apiService.revokeAdminUserSessions(user.id).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.revokeModalOpen.set(false);
        this.sessionsModalOpen.set(false);
        this.userSessions.set([]);
        this.loadUsers();
      },
      error: () => {
        this.isSaving.set(false);
      }
    });
  }

  openDeleteModal(user: AdminUser): void {
    this.selectedUser.set(user);
    this.deleteModalOpen.set(true);
  }

  confirmDeleteUser(): void {
    const user = this.selectedUser();
    if (!user) return;

    this.isSaving.set(true);
    this.apiService.deleteAdminUser(user.id).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.deleteModalOpen.set(false);
        this.loadUsers();
      },
      error: () => {
        this.isSaving.set(false);
      }
    });
  }
}
