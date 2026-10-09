import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { SystemHealth } from '../../../core/models/admin.models';
import { GlassCardComponent } from '../../../shared/components/glass-card/glass-card.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { ProgressRingComponent } from '../../../shared/components/progress-ring/progress-ring.component';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';

@Component({
  selector: 'app-system-health',
  standalone: true,
  imports: [
    CommonModule,
    GlassCardComponent,
    GlassButtonComponent,
    ProgressRingComponent,
    FileSizePipe
  ],
  template: `
    <div class="page-container anim-fade-in">
      <!-- Header -->
      <div class="page-header">
        <div class="header-titles">
          <h1 class="page-title">Master Server Health & Telemetry</h1>
          <p class="page-subtitle">Real-time hardware telemetry, memory pressure, database connectivity & platform state.</p>
        </div>
        <div class="header-actions">
          <button
            class="auto-refresh-toggle"
            [class.auto-refresh-toggle--active]="autoRefresh()"
            (click)="toggleAutoRefresh()"
          >
            <span class="refresh-indicator" [class.refresh-indicator--live]="autoRefresh()"></span>
            {{ autoRefresh() ? 'Live (5s)' : 'Paused' }}
          </button>
          <app-glass-button variant="secondary" size="md" (btnClick)="loadHealth()">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67" />
            </svg>
            Refresh
          </app-glass-button>
        </div>
      </div>

      @if (isLoading() && !health()) {
        <div class="loading-state">
          <div class="loading-spinner"></div>
          <span>Sampling server telemetry...</span>
        </div>
      } @else if (health()) {
        <!-- Main Telemetry Gauges Grid -->
        <div class="gauges-grid">
          <!-- CPU Load Card -->
          <app-glass-card padding="22px" [elevated]="true">
            <div class="gauge-card">
              <div class="gauge-header">
                <div class="gauge-title-group">
                  <div class="gauge-icon gauge-icon--cpu">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="4" y="4" width="16" height="16" rx="2" ry="2"></rect>
                      <rect x="9" y="9" width="6" height="6"></rect>
                      <line x1="9" y1="1" x2="9" y2="4"></line>
                      <line x1="15" y1="1" x2="15" y2="4"></line>
                      <line x1="9" y1="20" x2="9" y2="23"></line>
                      <line x1="15" y1="20" x2="15" y2="23"></line>
                      <line x1="20" y1="9" x2="23" y2="9"></line>
                      <line x1="20" y1="14" x2="23" y2="14"></line>
                      <line x1="1" y1="9" x2="4" y2="9"></line>
                      <line x1="1" y1="14" x2="4" y2="14"></line>
                    </svg>
                  </div>
                  <div>
                    <h3 class="gauge-name">CPU Load</h3>
                    <span class="gauge-desc">Master process processor load</span>
                  </div>
                </div>
                <span class="gauge-badge" [class.gauge-badge--warn]="health()!.cpuLoadPercentage > 75">
                  {{ health()!.cpuLoadPercentage > 75 ? 'Heavy Load' : 'Normal' }}
                </span>
              </div>

              <div class="gauge-body">
                <app-progress-ring
                  [size]="96"
                  [strokeWidth]="8"
                  [progress]="health()!.cpuLoadPercentage"
                  [showText]="true"
                  [fontSize]="18"
                  [color]="getColorForPercent(health()!.cpuLoadPercentage)"
                ></app-progress-ring>
                <div class="gauge-stats">
                  <div class="g-stat-row">
                    <span class="g-stat-lbl">Utilization</span>
                    <span class="g-stat-val">{{ health()!.cpuLoadPercentage }}%</span>
                  </div>
                  <div class="g-stat-row">
                    <span class="g-stat-lbl">Status</span>
                    <span class="g-stat-val val-green">Optimized</span>
                  </div>
                </div>
              </div>
            </div>
          </app-glass-card>

          <!-- RAM Memory Card -->
          <app-glass-card padding="22px" [elevated]="true">
            <div class="gauge-card">
              <div class="gauge-header">
                <div class="gauge-title-group">
                  <div class="gauge-icon gauge-icon--ram">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polygon points="12 2 2 7 12 12 22 7 12 2"></polygon>
                      <polyline points="2 17 12 22 22 17"></polyline>
                      <polyline points="2 12 12 17 22 12"></polyline>
                    </svg>
                  </div>
                  <div>
                    <h3 class="gauge-name">RAM Working Set</h3>
                    <span class="gauge-desc">Process memory utilization</span>
                  </div>
                </div>
                <span class="gauge-badge" [class.gauge-badge--warn]="health()!.memoryUsagePercentage > 85">
                  {{ health()!.memoryUsagePercentage > 85 ? 'High Usage' : 'Healthy' }}
                </span>
              </div>

              <div class="gauge-body">
                <app-progress-ring
                  [size]="96"
                  [strokeWidth]="8"
                  [progress]="health()!.memoryUsagePercentage"
                  [showText]="true"
                  [fontSize]="18"
                  [color]="getColorForPercent(health()!.memoryUsagePercentage)"
                ></app-progress-ring>
                <div class="gauge-stats">
                  <div class="g-stat-row">
                    <span class="g-stat-lbl">Working Set</span>
                    <span class="g-stat-val">{{ health()!.usedMemoryBytes | fileSize }}</span>
                  </div>
                  <div class="g-stat-row">
                    <span class="g-stat-lbl">Total Available</span>
                    <span class="g-stat-val">{{ health()!.totalMemoryBytes | fileSize }}</span>
                  </div>
                </div>
              </div>
            </div>
          </app-glass-card>

          <!-- Storage Pool Card -->
          <app-glass-card padding="22px" [elevated]="true">
            <div class="gauge-card">
              <div class="gauge-header">
                <div class="gauge-title-group">
                  <div class="gauge-icon gauge-icon--disk">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
                      <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
                      <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
                    </svg>
                  </div>
                  <div>
                    <h3 class="gauge-name">Local Storage Pool</h3>
                    <span class="gauge-desc">Master disk storage partition</span>
                  </div>
                </div>
                <span class="gauge-badge">
                  {{ health()!.storageUsagePercentage }}% Used
                </span>
              </div>

              <div class="gauge-body">
                <app-progress-ring
                  [size]="96"
                  [strokeWidth]="8"
                  [progress]="health()!.storageUsagePercentage"
                  [showText]="true"
                  [fontSize]="18"
                  [color]="getColorForPercent(health()!.storageUsagePercentage)"
                ></app-progress-ring>
                <div class="gauge-stats">
                  <div class="g-stat-row">
                    <span class="g-stat-lbl">Used Space</span>
                    <span class="g-stat-val">{{ health()!.storageUsedBytes | fileSize }}</span>
                  </div>
                  <div class="g-stat-row">
                    <span class="g-stat-lbl">Free Space</span>
                    <span class="g-stat-val">{{ health()!.storageFreeBytes | fileSize }}</span>
                  </div>
                </div>
              </div>
            </div>
          </app-glass-card>
        </div>

        <!-- Infrastructure & Health State Cards -->
        <div class="infra-grid">
          <!-- PostgreSQL Database State -->
          <app-glass-card padding="20px">
            <div class="infra-card">
              <div class="infra-header">
                <div class="infra-icon-wrap">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 14H9V8h2v8zm4 0h-2V8h2v8z"/>
                  </svg>
                </div>
                <span class="infra-title">PostgreSQL Database</span>
                <span class="db-status-pill" [class.db-status-pill--healthy]="health()!.databaseStatus === 'Healthy'">
                  {{ health()!.databaseStatus }}
                </span>
              </div>
              <div class="infra-details">
                <div class="detail-line">
                  <span>Round-Trip Latency:</span>
                  <strong>{{ health()!.databaseLatencyMs }} ms</strong>
                </div>
                <div class="detail-line">
                  <span>Connection Status:</span>
                  <span class="val-green">Online & Responsive</span>
                </div>
              </div>
            </div>
          </app-glass-card>

          <!-- Storage Nodes Fleet Status -->
          <app-glass-card padding="20px">
            <div class="infra-card">
              <div class="infra-header">
                <div class="infra-icon-wrap infra-icon-wrap--purple">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
                    <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
                  </svg>
                </div>
                <span class="infra-title">Node Fleet</span>
                <span class="node-fleet-count">{{ health()!.totalNodes }} Registered</span>
              </div>
              <div class="infra-details">
                <div class="detail-line">
                  <span>Online Daemons:</span>
                  <strong class="val-green">{{ health()!.onlineNodes }} nodes</strong>
                </div>
                <div class="detail-line">
                  <span>Offline / Warning:</span>
                  <span [class.val-red]="health()!.offlineNodes > 0">{{ health()!.offlineNodes }} nodes</span>
                </div>
              </div>
            </div>
          </app-glass-card>

          <!-- Active Users & Sessions -->
          <app-glass-card padding="20px">
            <div class="infra-card">
              <div class="infra-header">
                <div class="infra-icon-wrap infra-icon-wrap--green">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
                    <circle cx="9" cy="7" r="4"></circle>
                  </svg>
                </div>
                <span class="infra-title">User Accounts</span>
                <span class="user-count">{{ health()!.totalUsers }} Users</span>
              </div>
              <div class="infra-details">
                <div class="detail-line">
                  <span>Active Sessions:</span>
                  <strong>{{ health()!.activeSessionsCount }} connected</strong>
                </div>
                <div class="detail-line">
                  <span>Auth Mechanism:</span>
                  <span>Argon2id + JWT</span>
                </div>
              </div>
            </div>
          </app-glass-card>
        </div>

        <!-- Host System & Runtime Environment Details -->
        <app-glass-card padding="24px" [elevated]="true">
          <div class="runtime-card">
            <div class="runtime-header">
              <div class="runtime-icon">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect>
                  <line x1="8" y1="21" x2="16" y2="21"></line>
                  <line x1="12" y1="17" x2="12" y2="21"></line>
                </svg>
              </div>
              <div>
                <h3 class="runtime-title">Server Runtime Environment</h3>
                <span class="runtime-sub">Host operating system and .NET execution runtime</span>
              </div>
            </div>

            <div class="runtime-grid">
              <div class="runtime-item">
                <span class="runtime-lbl">Operating System</span>
                <span class="runtime-val">{{ health()!.osDescription }}</span>
              </div>

              <div class="runtime-item">
                <span class="runtime-lbl">Framework & Runtime</span>
                <span class="runtime-val">{{ health()!.frameworkDescription }}</span>
              </div>

              <div class="runtime-item">
                <span class="runtime-lbl">Process Uptime</span>
                <span class="runtime-val val-green">{{ formattedUptime() }}</span>
              </div>

              <div class="runtime-item">
                <span class="runtime-lbl">Server Started At</span>
                <span class="runtime-val">{{ health()!.startedAt | date:'yyyy-MM-dd HH:mm:ss' }}</span>
              </div>
            </div>
          </div>
        </app-glass-card>
      }
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

    .auto-refresh-toggle {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 14px;
      border-radius: var(--omega-radius-pill);
      @include glass-surface;
      font-size: 13px;
      font-weight: 600;
      color: var(--omega-text-secondary);
      transition: all 0.2s;

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
      }

      &--active {
        background: rgba(48, 209, 88, 0.15);
        color: var(--omega-success);
        border-color: rgba(48, 209, 88, 0.3);
      }
    }

    .refresh-indicator {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--omega-text-muted);

      &--live {
        background: var(--omega-success);
        box-shadow: 0 0 8px var(--omega-success);
        animation: pulse 2s infinite;
      }
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; transform: scale(1); }
      50% { opacity: 0.5; transform: scale(0.85); }
    }

    .gauges-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 20px;
    }

    .gauge-card {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .gauge-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
    }

    .gauge-title-group {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .gauge-icon {
      width: 40px;
      height: 40px;
      border-radius: var(--omega-radius-md);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;

      &--cpu {
        background: rgba(0, 113, 227, 0.15);
        color: var(--omega-accent);
      }

      &--ram {
        background: rgba(191, 90, 242, 0.15);
        color: #bf5af2;
      }

      &--disk {
        background: rgba(48, 209, 88, 0.15);
        color: var(--omega-success);
      }
    }

    .gauge-name {
      font-size: 16px;
      font-weight: 600;
      color: var(--omega-text-primary);
      margin: 0;
    }

    .gauge-desc {
      font-size: 11px;
      color: var(--omega-text-muted);
    }

    .gauge-badge {
      font-size: 11px;
      font-weight: 600;
      padding: 3px 8px;
      border-radius: var(--omega-radius-pill);
      @include glass-sunken;
      color: var(--omega-text-secondary);

      &--warn {
        background: rgba(255, 69, 58, 0.15);
        color: var(--omega-danger);
      }
    }

    .gauge-body {
      display: flex;
      align-items: center;
      justify-content: space-around;
      gap: 16px;
      padding: 8px 0;
    }

    .gauge-stats {
      display: flex;
      flex-direction: column;
      gap: 8px;
      flex: 1;
      max-width: 160px;
    }

    .g-stat-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      font-size: 12px;
    }

    .g-stat-lbl {
      color: var(--omega-text-muted);
    }

    .g-stat-val {
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .val-green {
      color: var(--omega-success);
    }

    .val-red {
      color: var(--omega-danger);
      font-weight: 600;
    }

    .infra-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 20px;
    }

    .infra-card {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .infra-header {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .infra-icon-wrap {
      width: 32px;
      height: 32px;
      border-radius: var(--omega-radius-sm);
      background: rgba(0, 113, 227, 0.15);
      color: var(--omega-accent);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;

      &--purple {
        background: rgba(191, 90, 242, 0.15);
        color: #bf5af2;
      }

      &--green {
        background: rgba(48, 209, 88, 0.15);
        color: var(--omega-success);
      }
    }

    .infra-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--omega-text-primary);
      flex: 1;
    }

    .db-status-pill, .node-fleet-count, .user-count {
      font-size: 11px;
      font-weight: 600;
      padding: 2px 8px;
      border-radius: var(--omega-radius-pill);
      @include glass-sunken;
      color: var(--omega-text-secondary);

      &--healthy {
        background: rgba(48, 209, 88, 0.15);
        color: var(--omega-success);
      }
    }

    .infra-details {
      display: flex;
      flex-direction: column;
      gap: 6px;
      font-size: 12px;
    }

    .detail-line {
      display: flex;
      align-items: center;
      justify-content: space-between;

      span {
        color: var(--omega-text-muted);
      }

      strong {
        color: var(--omega-text-primary);
      }
    }

    .runtime-card {
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    .runtime-header {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .runtime-icon {
      width: 40px;
      height: 40px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-glass-bg-elevated);
      color: var(--omega-text-primary);
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .runtime-title {
      font-size: 16px;
      font-weight: 600;
      color: var(--omega-text-primary);
      margin: 0;
    }

    .runtime-sub {
      font-size: 12px;
      color: var(--omega-text-muted);
    }

    .runtime-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      padding-top: 6px;
    }

    .runtime-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      @include glass-sunken;
    }

    .runtime-lbl {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--omega-text-muted);
    }

    .runtime-val {
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-primary);
      word-break: break-word;
    }

    .loading-state {
      padding: 80px;
      text-align: center;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 14px;
      color: var(--omega-text-muted);
    }

    .loading-spinner {
      width: 32px;
      height: 32px;
      border: 3px solid rgba(120, 120, 128, 0.2);
      border-top-color: var(--omega-accent);
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `]
})
export class SystemHealthComponent implements OnInit, OnDestroy {
  private readonly apiService = inject(ApiService);

  health = signal<SystemHealth | null>(null);
  isLoading = signal<boolean>(false);
  autoRefresh = signal<boolean>(true);

  private intervalId: any = null;

  readonly formattedUptime = computed(() => {
    const totalSecs = this.health()?.serverUptimeSeconds || 0;
    const days = Math.floor(totalSecs / 86400);
    const hours = Math.floor((totalSecs % 86400) / 3600);
    const minutes = Math.floor((totalSecs % 3600) / 60);

    const parts: string[] = [];
    if (days > 0) parts.push(`${days}d`);
    if (hours > 0) parts.push(`${hours}h`);
    parts.push(`${minutes}m`);
    return parts.join(' ');
  });

  ngOnInit(): void {
    this.loadHealth();
    this.startAutoRefresh();
  }

  ngOnDestroy(): void {
    this.stopAutoRefresh();
  }

  loadHealth(): void {
    this.isLoading.set(true);
    this.apiService.getSystemHealth().subscribe({
      next: data => {
        this.health.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  toggleAutoRefresh(): void {
    const nextVal = !this.autoRefresh();
    this.autoRefresh.set(nextVal);
    if (nextVal) {
      this.startAutoRefresh();
    } else {
      this.stopAutoRefresh();
    }
  }

  private startAutoRefresh(): void {
    this.stopAutoRefresh();
    this.intervalId = setInterval(() => {
      this.loadHealth();
    }, 5000);
  }

  private stopAutoRefresh(): void {
    if (this.intervalId) {
      clearInterval(this.intervalId);
      this.intervalId = null;
    }
  }

  getColorForPercent(p: number): string {
    if (p > 85) return 'var(--omega-danger)';
    if (p > 70) return 'var(--omega-warning)';
    return 'var(--omega-accent)';
  }
}
