import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { AuditLog } from '../../../core/models/admin.models';
import { GlassCardComponent } from '../../../shared/components/glass-card/glass-card.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';

@Component({
  selector: 'app-audit-log',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    GlassCardComponent,
    GlassButtonComponent,
    GlassModalComponent,
    RelativeTimePipe
  ],
  template: `
    <div class="page-container anim-fade-in">
      <!-- Header -->
      <div class="page-header">
        <div class="header-titles">
          <h1 class="page-title">System Audit Trail</h1>
          <p class="page-subtitle">Historical audit trail of security, user authentication, file modifications & node actions.</p>
        </div>
        <div class="header-actions">
          <app-glass-button variant="secondary" size="md" (btnClick)="loadLogs()">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67" />
            </svg>
            Refresh
          </app-glass-button>
        </div>
      </div>

      <!-- Quick Metrics -->
      <div class="stats-grid">
        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Total Events Loaded</div>
            <div class="stat-value">{{ logs().length }}</div>
            <div class="stat-sub">Audit trail records</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Security & Auth Events</div>
            <div class="stat-value">{{ authEventsCount() }}</div>
            <div class="stat-sub">Logins, logouts & tokens</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Administrative Actions</div>
            <div class="stat-value">{{ adminActionsCount() }}</div>
            <div class="stat-sub">User updates, password resets</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Node Operations</div>
            <div class="stat-value">{{ nodeEventsCount() }}</div>
            <div class="stat-sub">Pings, registrations & updates</div>
          </div>
        </app-glass-card>
      </div>

      <!-- Search & Filter Toolbar -->
      <div class="toolbar">
        <div class="search-input-wrapper">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"></circle>
            <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
          </svg>
          <input
            type="text"
            placeholder="Search by user, IP, resource..."
            [ngModel]="searchQuery()"
            (ngModelChange)="onSearchChange($event)"
          />
          @if (searchQuery()) {
            <button class="clear-btn" (click)="clearSearch()">✕</button>
          }
        </div>

        <div class="filters">
          <select [ngModel]="actionFilter()" (ngModelChange)="onActionChange($event)" class="glass-select">
            <option value="All">All Actions</option>
            <option value="UserLogin">User Login</option>
            <option value="UserLogout">User Logout</option>
            <option value="UserCreated">User Created</option>
            <option value="UserUpdated">User Updated</option>
            <option value="PasswordReset">Password Reset</option>
            <option value="SessionRevoked">Session Revoked</option>
            <option value="SessionsRevoked">Sessions Revoked</option>
            <option value="UserDeleted">User Deleted</option>
            <option value="FileUploaded">File Uploaded</option>
            <option value="FileDownloaded">File Downloaded</option>
            <option value="FileDeleted">File Deleted</option>
            <option value="TrashEmptied">Trash Emptied</option>
            <option value="NodeRegistered">Node Registered</option>
            <option value="NodeWentOffline">Node Went Offline</option>
            <option value="NodeTokenGenerated">Node Token Generated</option>
            <option value="NodePinged">Node Pinged</option>
            <option value="NodeUpdated">Node Updated</option>
            <option value="NodeDeleted">Node Deleted</option>
          </select>

          <select [ngModel]="limitOption()" (ngModelChange)="onLimitChange($event)" class="glass-select">
            <option [value]="50">50 entries</option>
            <option [value]="100">100 entries</option>
            <option [value]="250">250 entries</option>
          </select>
        </div>
      </div>

      <!-- Audit Logs Table -->
      <app-glass-card padding="0px" [elevated]="true">
        @if (isLoading()) {
          <div class="loading-state">
            <div class="loading-spinner"></div>
            <span>Loading audit log entries...</span>
          </div>
        } @else if (logs().length === 0) {
          <div class="empty-state">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
              <polyline points="14 2 14 8 20 8"></polyline>
            </svg>
            <h3>No audit records found</h3>
            <p>Try clearing your filter criteria.</p>
          </div>
        } @else {
          <div class="table-wrapper">
            <table class="glass-table">
              <thead>
                <tr>
                  <th>Timestamp</th>
                  <th>Action</th>
                  <th>Actor / User</th>
                  <th>Target Resource</th>
                  <th>IP Address</th>
                  <th>Details</th>
                  <th class="actions-header">Inspect</th>
                </tr>
              </thead>
              <tbody>
                @for (log of logs(); track log.id) {
                  <tr class="table-row">
                    <td>
                      <div class="time-cell">
                        <span class="time-primary">{{ log.timestamp | date:'MMM d, HH:mm:ss' }}</span>
                        <span class="time-relative">{{ log.timestamp | relativeTime }}</span>
                      </div>
                    </td>
                    <td>
                      <span class="action-pill" [class]="getActionClass(log.action)">
                        {{ log.action }}
                      </span>
                    </td>
                    <td>
                      <div class="actor-cell">
                        <div class="actor-avatar">
                          {{ (log.userName || 'System').slice(0, 2).toUpperCase() }}
                        </div>
                        <span class="actor-name">{{ log.userName || 'System / Anonymous' }}</span>
                      </div>
                    </td>
                    <td>
                      <div class="resource-cell">
                        <span class="resource-name">{{ log.resource }}</span>
                        @if (log.resourceId) {
                          <span class="resource-id" [title]="log.resourceId">
                            #{{ log.resourceId.slice(0, 8) }}
                          </span>
                        }
                      </div>
                    </td>
                    <td>
                      <code class="ip-code">{{ log.ipAddress }}</code>
                    </td>
                    <td>
                      <span class="details-preview" [title]="log.detailsJson || ''">
                        {{ formatDetailsPreview(log.detailsJson) }}
                      </span>
                    </td>
                    <td class="actions-cell">
                      <button class="inspect-btn" title="Inspect details" (click)="openInspectModal(log)">
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <circle cx="11" cy="11" r="8"></circle>
                          <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                        </svg>
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </app-glass-card>

      <!-- Details Inspection Modal -->
      <app-glass-modal
        [isOpen]="inspectModalOpen()"
        title="Audit Event Details"
        (close)="inspectModalOpen.set(false)"
      >
        @if (selectedLog()) {
          <div class="inspect-dialog">
            <div class="inspect-row">
              <span class="inspect-lbl">Action</span>
              <span class="action-pill" [class]="getActionClass(selectedLog()!.action)">
                {{ selectedLog()!.action }}
              </span>
            </div>

            <div class="inspect-row">
              <span class="inspect-lbl">Timestamp</span>
              <span>{{ selectedLog()!.timestamp | date:'yyyy-MM-dd HH:mm:ss UTC' }}</span>
            </div>

            <div class="inspect-row">
              <span class="inspect-lbl">Actor</span>
              <span>{{ selectedLog()!.userName || 'System' }} (ID: {{ selectedLog()!.userId || 'N/A' }})</span>
            </div>

            <div class="inspect-row">
              <span class="inspect-lbl">Target Resource</span>
              <span>{{ selectedLog()!.resource }} ({{ selectedLog()!.resourceId || 'N/A' }})</span>
            </div>

            <div class="inspect-row">
              <span class="inspect-lbl">Client IP</span>
              <code>{{ selectedLog()!.ipAddress }}</code>
            </div>

            <div class="inspect-json-section">
              <label>Event Payload JSON</label>
              <pre><code>{{ formatJson(selectedLog()!.detailsJson) }}</code></pre>
            </div>
          </div>
        }

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="inspectModalOpen.set(false)">Close</app-glass-button>
        </div>
      </app-glass-modal>
    </div>
  `,
  styles: [`
    @use 'styles/glass' as *;
    @use 'styles/animations' as *;

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

    .glass-select {
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
        padding: 14px 18px;
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

    .time-cell {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .time-primary {
      font-weight: 600;
      color: var(--omega-text-primary);
      white-space: nowrap;
    }

    .time-relative {
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .action-pill {
      display: inline-block;
      padding: 3px 10px;
      border-radius: var(--omega-radius-pill);
      font-size: 11px;
      font-weight: 600;
      white-space: nowrap;

      &--auth {
        background: rgba(48, 209, 88, 0.15);
        color: var(--omega-success);
        border: 1px solid rgba(48, 209, 88, 0.3);
      }

      &--admin {
        background: rgba(0, 113, 227, 0.15);
        color: var(--omega-accent);
        border: 1px solid rgba(0, 113, 227, 0.3);
      }

      &--node {
        background: rgba(191, 90, 242, 0.15);
        color: #bf5af2;
        border: 1px solid rgba(191, 90, 242, 0.3);
      }

      &--danger {
        background: rgba(255, 69, 58, 0.15);
        color: var(--omega-danger);
        border: 1px solid rgba(255, 69, 58, 0.3);
      }

      &--file {
        background: rgba(255, 159, 10, 0.15);
        color: #ff9f0a;
        border: 1px solid rgba(255, 159, 10, 0.3);
      }

      &--default {
        background: rgba(120, 120, 128, 0.15);
        color: var(--omega-text-secondary);
        border: 1px solid var(--omega-glass-border);
      }
    }

    .actor-cell {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .actor-avatar {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      background: var(--omega-glass-bg-elevated);
      border: 1px solid var(--omega-glass-border);
      font-size: 11px;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      color: var(--omega-text-secondary);
    }

    .actor-name {
      font-weight: 500;
      color: var(--omega-text-primary);
    }

    .resource-cell {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .resource-name {
      font-weight: 500;
      color: var(--omega-text-secondary);
    }

    .resource-id {
      font-size: 11px;
      font-family: monospace;
      color: var(--omega-text-muted);
      background: var(--omega-glass-bg-sunken);
      padding: 1px 5px;
      border-radius: 4px;
    }

    .ip-code {
      font-size: 11px;
      font-family: monospace;
      color: var(--omega-text-muted);
    }

    .details-preview {
      display: block;
      max-width: 220px;
      font-size: 12px;
      color: var(--omega-text-muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .actions-cell {
      text-align: right;
    }

    .inspect-btn {
      width: 30px;
      height: 30px;
      border-radius: var(--omega-radius-sm);
      display: inline-flex;
      align-items: center;
      justify-content: center;
      color: var(--omega-text-muted);
      transition: all 0.15s;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
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

    // Inspect dialog
    .inspect-dialog {
      display: flex;
      flex-direction: column;
      gap: 12px;
      font-size: 13px;
    }

    .inspect-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 8px 12px;
      border-radius: var(--omega-radius-sm);
      @include glass-sunken;
    }

    .inspect-lbl {
      color: var(--omega-text-muted);
      font-weight: 500;
    }

    .inspect-json-section {
      display: flex;
      flex-direction: column;
      gap: 6px;
      margin-top: 6px;

      label {
        font-size: 12px;
        font-weight: 600;
        text-transform: uppercase;
        color: var(--omega-text-muted);
      }

      pre {
        padding: 12px;
        border-radius: var(--omega-radius-md);
        background: rgba(0, 0, 0, 0.35);
        color: #30d158;
        font-size: 11px;
        line-height: 1.4;
        overflow-x: auto;
        max-height: 250px;
      }
    }
  `]
})
export class AuditLogComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  logs = signal<AuditLog[]>([]);
  isLoading = signal<boolean>(false);

  searchQuery = signal<string>('');
  actionFilter = signal<string>('All');
  limitOption = signal<number>(100);

  inspectModalOpen = signal<boolean>(false);
  selectedLog = signal<AuditLog | null>(null);

  readonly authEventsCount = computed(() => {
    return this.logs().filter(l => l.action.includes('Login') || l.action.includes('Logout')).length;
  });

  readonly adminActionsCount = computed(() => {
    return this.logs().filter(l =>
      (l.action.includes('User') && !l.action.includes('Login') && !l.action.includes('Logout')) ||
      l.action.includes('Password') ||
      l.action.includes('Session')
    ).length;
  });

  readonly nodeEventsCount = computed(() => {
    return this.logs().filter(l => l.action.includes('Node')).length;
  });

  readonly fileEventsCount = computed(() => {
    return this.logs().filter(l => l.action.includes('File') || l.action.includes('Trash')).length;
  });

  ngOnInit(): void {
    this.loadLogs();
  }

  loadLogs(): void {
    this.isLoading.set(true);

    const action = this.actionFilter() === 'All' ? undefined : this.actionFilter();
    const search = this.searchQuery().trim() || undefined;
    const limit = this.limitOption();

    this.apiService.getAuditLogs({ action, search, limit }).subscribe({
      next: data => {
        this.logs.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  onSearchChange(q: string): void {
    this.searchQuery.set(q);
    this.loadLogs();
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.loadLogs();
  }

  onActionChange(act: string): void {
    this.actionFilter.set(act);
    this.loadLogs();
  }

  onLimitChange(lim: number): void {
    this.limitOption.set(Number(lim));
    this.loadLogs();
  }

  getActionClass(action: string): string {
    const act = action.toLowerCase();
    if (act.includes('delete') || act.includes('revoke') || act.includes('offline')) return 'action-pill--danger';
    if (act.includes('file') || act.includes('trash') || act.includes('upload') || act.includes('download')) return 'action-pill--file';
    if (act.includes('login') || act.includes('logout') || act.includes('auth')) return 'action-pill--auth';
    if (act.includes('node')) return 'action-pill--node';
    if (act.includes('user') || act.includes('password') || act.includes('session')) return 'action-pill--admin';
    return 'action-pill--default';
  }

  formatDetailsPreview(json: string | null | undefined): string {
    if (!json) return '—';
    try {
      const obj = JSON.parse(json);
      return Object.entries(obj)
        .map(([k, v]) => `${k}: ${v}`)
        .join(', ');
    } catch {
      return json;
    }
  }

  formatJson(json: string | null | undefined): string {
    if (!json) return '// No details recorded for this event';
    try {
      const obj = JSON.parse(json);
      return JSON.stringify(obj, null, 2);
    } catch {
      return json;
    }
  }

  openInspectModal(log: AuditLog): void {
    this.selectedLog.set(log);
    this.inspectModalOpen.set(true);
  }
}
