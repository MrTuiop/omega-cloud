import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SignalRService } from '../../../core/services/signalr.service';
import {
  StorageNode,
  GenerateNodeTokenRequest,
  NodeRegistrationToken,
  UpdateStorageNodeRequest,
  NodePingResult
} from '../../../core/models/admin.models';
import { GlassCardComponent } from '../../../shared/components/glass-card/glass-card.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { ProgressRingComponent } from '../../../shared/components/progress-ring/progress-ring.component';
import { FileSizePipe } from '../../../shared/pipes/file-size.pipe';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';

@Component({
  selector: 'app-storage-nodes',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    GlassCardComponent,
    GlassButtonComponent,
    GlassModalComponent,
    ProgressRingComponent,
    FileSizePipe,
    RelativeTimePipe
  ],
  template: `
    <div class="page-container anim-fade-in">
      <!-- Header -->
      <div class="page-header">
        <div class="header-titles">
          <h1 class="page-title">Storage Nodes & Fleet</h1>
          <p class="page-subtitle">Distributed storage daemons, direct pre-signed URLs, disk usage & live telemetry.</p>
        </div>
        <div class="header-actions">
          <app-glass-button variant="secondary" size="md" (btnClick)="loadNodes()">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67" />
            </svg>
            Refresh
          </app-glass-button>
          <app-glass-button variant="primary" size="md" (btnClick)="openGenerateTokenModal()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 2l-2 2m-6 6l7-7m-7 7l-2-2m-4 4l-4 4a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l4-4m4-4l2 2m-2-2l-4-4"></path>
            </svg>
            Generate Node Token
          </app-glass-button>
        </div>
      </div>

      <!-- Stats Grid -->
      <div class="stats-grid">
        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Total Nodes</div>
            <div class="stat-value">{{ nodes().length }}</div>
            <div class="stat-sub">{{ remoteNodesCount() }} remote daemons, {{ localNodesCount() }} local</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Fleet Status</div>
            <div class="stat-value fleet-status">
              <span class="status-indicator-dot"></span>
              {{ onlineNodesCount() }} / {{ nodes().length }} Online
            </div>
            <div class="stat-sub">{{ offlineNodesCount() }} offline / unreachable</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Total Fleet Storage</div>
            <div class="stat-value">{{ totalFleetCapacity() | fileSize }}</div>
            <div class="stat-sub">{{ totalFleetAvailable() | fileSize }} available</div>
          </div>
        </app-glass-card>

        <app-glass-card padding="18px">
          <div class="stat-card">
            <div class="stat-label">Fleet Utilization</div>
            <div class="stat-value">{{ fleetUsagePercentage() }}%</div>
            <div class="stat-sub">{{ totalFleetUsed() | fileSize }} used across nodes</div>
          </div>
        </app-glass-card>
      </div>

      <!-- Nodes Grid -->
      @if (isLoading()) {
        <div class="loading-state">
          <div class="loading-spinner"></div>
          <span>Loading storage nodes...</span>
        </div>
      } @else if (nodes().length === 0) {
        <app-glass-card padding="50px">
          <div class="empty-state">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
              <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
              <line x1="6" y1="6" x2="6.01" y2="6"></line>
              <line x1="6" y1="18" x2="6.01" y2="18"></line>
            </svg>
            <h3>No storage nodes registered</h3>
            <p>Generate a node registration token to connect your first storage daemon.</p>
            <app-glass-button variant="primary" (btnClick)="openGenerateTokenModal()">Generate Token</app-glass-button>
          </div>
        </app-glass-card>
      } @else {
        <div class="nodes-grid">
          @for (n of nodes(); track n.id) {
            <app-glass-card padding="22px" [elevated]="true">
              <div class="node-card">
                <!-- Header -->
                <div class="node-header">
                  <div class="node-title-group">
                    <div class="node-icon" [class.node-icon--remote]="n.type === 'RemoteDaemon'">
                      @if (n.type === 'RemoteDaemon') {
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z" />
                        </svg>
                      } @else {
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
                          <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
                          <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
                        </svg>
                      }
                    </div>

                    <div class="node-meta">
                      <h3 class="node-name">{{ n.name }}</h3>
                      <div class="node-badges">
                        <span class="type-badge">{{ n.type }}</span>
                        @if (n.type === 'RemoteDaemon') {
                          <span class="nat-badge" [class.nat-badge--nat]="n.isBehindNat">
                            {{ n.isBehindNat ? 'NAT Fallback' : 'Direct Access' }}
                          </span>
                        }
                      </div>
                    </div>
                  </div>

                  <!-- Live status -->
                  <div class="node-status-pill" [class]="'node-status-pill--' + n.status.toLowerCase()">
                    <span class="status-pulse-dot"></span>
                    <span>{{ n.status }}</span>
                  </div>
                </div>

                <!-- Storage Gauge -->
                <div class="storage-section">
                  <div class="ring-wrapper">
                    <app-progress-ring
                      [size]="64"
                      [strokeWidth]="6"
                      [progress]="n.usedSpacePercentage"
                      [showText]="true"
                      [fontSize]="12"
                      [color]="getRingColor(n.usedSpacePercentage)"
                    ></app-progress-ring>
                  </div>
                  <div class="storage-info">
                    <div class="storage-row">
                      <span class="storage-label">Used Space</span>
                      <span class="storage-val">{{ n.usedSpaceBytes | fileSize }}</span>
                    </div>
                    <div class="storage-row">
                      <span class="storage-label">Available</span>
                      <span class="storage-val">{{ n.availableSpaceBytes | fileSize }}</span>
                    </div>
                    <div class="storage-row">
                      <span class="storage-label">Total Capacity</span>
                      <span class="storage-val">{{ n.totalSpaceBytes | fileSize }}</span>
                    </div>
                  </div>
                </div>

                <!-- Telemetry Metrics -->
                <div class="telemetry-grid">
                  <div class="metric-item">
                    <span class="metric-label">Latency</span>
                    <span class="metric-value">
                      @if (pingResults()[n.id]) {
                        @if (pingResults()[n.id].isReachable && pingResults()[n.id].latencyMs !== null && pingResults()[n.id].latencyMs !== undefined) {
                          <span class="ping-fresh">{{ pingResults()[n.id].latencyMs }} ms</span>
                        } @else {
                          <span class="ping-unreachable">Unreachable</span>
                        }
                      } @else if (n.latencyMs !== null && n.latencyMs !== undefined) {
                        {{ n.latencyMs }} ms
                      } @else {
                        <span class="metric-na">—</span>
                      }
                    </span>
                  </div>

                  <div class="metric-item">
                    <span class="metric-label">CPU Load</span>
                    <span class="metric-value">
                      @if (n.cpuLoad !== null && n.cpuLoad !== undefined) {
                        {{ n.cpuLoad }}%
                      } @else {
                        <span class="metric-na">—</span>
                      }
                    </span>
                  </div>

                  <div class="metric-item">
                    <span class="metric-label">Heartbeat</span>
                    <span class="metric-value" [title]="n.lastHeartbeatAt">
                      {{ n.lastHeartbeatAt | relativeTime }}
                    </span>
                  </div>

                  <div class="metric-item">
                    <span class="metric-label">Disk Health</span>
                    <span class="metric-value status-good">
                      {{ n.diskStatus || 'Healthy' }}
                    </span>
                  </div>
                </div>

                <!-- Base URL (if present) -->
                @if (n.baseUrl) {
                  <div class="node-endpoint">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"></path>
                      <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"></path>
                    </svg>
                    <code>{{ n.baseUrl }}</code>
                  </div>
                }

                <!-- Card Actions Footer -->
                <div class="node-footer">
                  <app-glass-button
                    variant="secondary"
                    size="sm"
                    [loading]="pingingNodes()[n.id]"
                    (btnClick)="pingNode(n)"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
                    </svg>
                    Ping Node
                  </app-glass-button>

                  <div class="footer-btn-group">
                    <button class="icon-action-btn" title="Edit Node" (click)="openEditNodeModal(n)">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M12 20h9"></path>
                        <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"></path>
                      </svg>
                    </button>

                    @if (n.type === 'RemoteDaemon') {
                      <button class="icon-action-btn icon-action-btn--danger" title="Delete Node" (click)="openDeleteNodeModal(n)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"></polyline>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                        </svg>
                      </button>
                    }
                  </div>
                </div>
              </div>
            </app-glass-card>
          }
        </div>
      }

      <!-- Generate Token Modal -->
      <app-glass-modal
        [isOpen]="generateTokenModalOpen()"
        title="Generate Node Registration Key"
        (close)="generateTokenModalOpen.set(false)"
      >
        @if (!generatedToken()) {
          <div class="modal-form">
            <p class="modal-note">
              This one-time key is used by the <strong>Omega Storage Node Daemon</strong> to authenticate and establish a secure HMAC pairing with the Master Server.
            </p>

            <label>Node Name</label>
            <input
              type="text"
              [(ngModel)]="newNodeName"
              placeholder="e.g. Home-Synology-NAS or VPS-Worker-1"
            />

            <label>Node Type</label>
            <select [(ngModel)]="newNodeType" class="modal-select">
              <option value="RemoteDaemon">Remote Storage Daemon</option>
              <option value="LocalDisk">Local Disk Mount</option>
            </select>

            <label>Description (Optional)</label>
            <input
              type="text"
              [(ngModel)]="newNodeDescription"
              placeholder="e.g. Secondary backup storage in Munich"
            />
          </div>
        } @else {
          <!-- Token Created View -->
          <div class="token-display anim-scale-in">
            <div class="token-success-icon">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                <polyline points="20 6 9 17 4 12"></polyline>
              </svg>
            </div>
            <h3>Registration Key Generated!</h3>
            <p>Save this key now. It will not be shown again.</p>

            <div class="token-box">
              <code>{{ generatedToken()?.registrationKey }}</code>
              <button class="copy-btn" (click)="copyToken()">
                {{ isCopied() ? 'Copied!' : 'Copy' }}
              </button>
            </div>

            <div class="daemon-instructions">
              <h4>Run Daemon Command:</h4>
              <pre><code>docker run -d --name omega-node \\
  -e MASTER_URL=https://your-domain.com \\
  -e REGISTRATION_KEY={{ generatedToken()?.registrationKey }} \\
  -v /mnt/storage:/data \\
  omegacloud/storage-node:latest</code></pre>
            </div>
          </div>
        }

        <div footer>
          @if (!generatedToken()) {
            <app-glass-button variant="secondary" (btnClick)="generateTokenModalOpen.set(false)">Cancel</app-glass-button>
            <app-glass-button
              variant="primary"
              [disabled]="isGeneratingToken() || !newNodeName.trim()"
              [loading]="isGeneratingToken()"
              (btnClick)="submitGenerateToken()"
            >
              Generate Token
            </app-glass-button>
          } @else {
            <app-glass-button variant="primary" (btnClick)="closeTokenModal()">Done</app-glass-button>
          }
        </div>
      </app-glass-modal>

      <!-- Edit Node Modal -->
      <app-glass-modal
        [isOpen]="editNodeModalOpen()"
        [title]="'Edit Node: ' + (selectedNode()?.name || '')"
        (close)="editNodeModalOpen.set(false)"
      >
        @if (selectedNode()) {
          <div class="modal-form">
            <label>Node Name</label>
            <input type="text" [(ngModel)]="editNodeName" />

            <label>Public Base URL (for direct client downloads)</label>
            <input type="text" [(ngModel)]="editNodeBaseUrl" placeholder="https://node1.mydomain.com:5001" />

            <label class="checkbox-label">
              <input type="checkbox" [(ngModel)]="editNodeIsBehindNat" />
              <span>Behind NAT / Firewall (stream traffic through Master fallback)</span>
            </label>

            <label>Status Override</label>
            <select [(ngModel)]="editNodeStatus" class="modal-select">
              <option value="Online">Online</option>
              <option value="Degraded">Degraded</option>
              <option value="Offline">Offline</option>
              <option value="Maintenance">Maintenance</option>
            </select>
          </div>
        }

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="editNodeModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="primary" [loading]="isSavingNode()" (btnClick)="submitEditNode()">
            Save Node
          </app-glass-button>
        </div>
      </app-glass-modal>

      <!-- Delete Node Modal -->
      <app-glass-modal
        [isOpen]="deleteNodeModalOpen()"
        title="Archive / Remove Storage Node"
        (close)="deleteNodeModalOpen.set(false)"
      >
        <p>
          Are you sure you want to disconnect storage node <strong>{{ selectedNode()?.name }}</strong>?
          New uploads will no longer target this node.
        </p>

        <div footer>
          <app-glass-button variant="secondary" (btnClick)="deleteNodeModalOpen.set(false)">Cancel</app-glass-button>
          <app-glass-button variant="danger" [loading]="isSavingNode()" (btnClick)="submitDeleteNode()">
            Disconnect Node
          </app-glass-button>
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

    .fleet-status {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .status-indicator-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--omega-success);
      box-shadow: 0 0 10px var(--omega-success);
    }

    .stat-sub {
      font-size: 12px;
      color: var(--omega-text-secondary);
    }

    .nodes-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
      gap: 20px;
    }

    .node-card {
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    .node-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
    }

    .node-title-group {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .node-icon {
      width: 40px;
      height: 40px;
      border-radius: var(--omega-radius-md);
      background: rgba(0, 113, 227, 0.15);
      color: var(--omega-accent);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;

      &--remote {
        background: rgba(191, 90, 242, 0.15);
        color: #bf5af2;
      }
    }

    .node-meta {
      display: flex;
      flex-direction: column;
      gap: 3px;
    }

    .node-name {
      font-size: 16px;
      font-weight: 600;
      color: var(--omega-text-primary);
      margin: 0;
    }

    .node-badges {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .type-badge {
      font-size: 11px;
      font-weight: 500;
      color: var(--omega-text-muted);
    }

    .nat-badge {
      font-size: 10px;
      font-weight: 600;
      padding: 1px 7px;
      border-radius: var(--omega-radius-pill);
      background: rgba(48, 209, 88, 0.15);
      color: var(--omega-success);

      &--nat {
        background: rgba(255, 159, 10, 0.15);
        color: #ff9f0a;
      }
    }

    .node-status-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: var(--omega-radius-pill);
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;

      &--online {
        background: rgba(48, 209, 88, 0.15);
        color: var(--omega-success);
        .status-pulse-dot {
          background: var(--omega-success);
          box-shadow: 0 0 8px var(--omega-success);
        }
      }

      &--offline {
        background: rgba(255, 69, 58, 0.15);
        color: var(--omega-danger);
        .status-pulse-dot {
          background: var(--omega-danger);
        }
      }

      &--degraded, &--maintenance {
        background: rgba(255, 159, 10, 0.15);
        color: #ff9f0a;
        .status-pulse-dot {
          background: #ff9f0a;
        }
      }
    }

    .status-pulse-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
    }

    .storage-section {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 12px 14px;
      border-radius: var(--omega-radius-md);
      @include glass-sunken;
    }

    .storage-info {
      display: flex;
      flex-direction: column;
      gap: 4px;
      flex: 1;
    }

    .storage-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      font-size: 12px;
    }

    .storage-label {
      color: var(--omega-text-muted);
    }

    .storage-val {
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .telemetry-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 8px;
    }

    .metric-item {
      display: flex;
      flex-direction: column;
      gap: 3px;
      padding: 8px 10px;
      border-radius: var(--omega-radius-sm);
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid var(--omega-glass-border-subtle);
    }

    .metric-label {
      font-size: 10px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--omega-text-muted);
    }

    .metric-value {
      font-size: 12px;
      font-weight: 600;
      color: var(--omega-text-primary);
    }

    .metric-na {
      color: var(--omega-text-muted);
    }

    .ping-fresh {
      color: var(--omega-success);
    }

    .ping-unreachable {
      color: var(--omega-danger);
      font-weight: 600;
    }

    .status-good {
      color: var(--omega-success);
    }

    .node-endpoint {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 11px;
      color: var(--omega-text-muted);

      code {
        background: var(--omega-glass-bg-sunken);
        padding: 2px 8px;
        border-radius: 4px;
        font-family: monospace;
      }
    }

    .node-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding-top: 14px;
      border-top: 1px solid var(--omega-glass-border-subtle);
    }

    .footer-btn-group {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .icon-action-btn {
      width: 32px;
      height: 32px;
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

      &--danger:hover {
        background: rgba(255, 69, 58, 0.15);
        color: var(--omega-danger);
      }
    }

    .loading-state, .empty-state {
      padding: 60px;
      text-align: center;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 14px;
      color: var(--omega-text-muted);

      h3 {
        color: var(--omega-text-primary);
        font-size: 18px;
        font-weight: 600;
      }

      p {
        font-size: 14px;
        max-width: 400px;
      }
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

    // Modal forms & Token Display
    .modal-form {
      display: flex;
      flex-direction: column;
      gap: 12px;

      label {
        font-size: 13px;
        font-weight: 500;
        color: var(--omega-text-secondary);
      }

      input[type="text"] {
        width: 100%;
        height: 42px;
      }
    }

    .modal-note {
      font-size: 13px;
      color: var(--omega-text-secondary);
      line-height: 1.4;
      padding: 10px 14px;
      @include glass-sunken;
      border-radius: var(--omega-radius-md);
    }

    .modal-select {
      height: 42px;
      padding: 0 14px;
      border-radius: var(--omega-radius-md);
      background: var(--omega-glass-bg-sunken);
      border: 1px solid var(--omega-glass-border);
      color: var(--omega-text-primary);
      outline: none;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      font-size: 13px;
      color: var(--omega-text-primary);
      margin-top: 6px;

      input[type="checkbox"] {
        width: 18px;
        height: 18px;
        cursor: pointer;
      }
    }

    .token-display {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      text-align: center;

      h3 {
        font-size: 18px;
        font-weight: 700;
        color: var(--omega-text-primary);
      }

      p {
        font-size: 13px;
        color: var(--omega-text-secondary);
      }
    }

    .token-success-icon {
      width: 48px;
      height: 48px;
      border-radius: 50%;
      background: rgba(48, 209, 88, 0.15);
      color: var(--omega-success);
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .token-box {
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 10px 14px;
      border-radius: var(--omega-radius-md);
      @include glass-elevated;
      border: 1px solid var(--omega-accent);
      margin-top: 8px;

      code {
        font-family: monospace;
        font-size: 14px;
        font-weight: 600;
        color: var(--omega-accent);
        word-break: break-all;
      }
    }

    .copy-btn {
      padding: 6px 14px;
      border-radius: var(--omega-radius-pill);
      background: var(--omega-accent);
      color: #ffffff;
      font-size: 12px;
      font-weight: 600;
      flex-shrink: 0;
      transition: all 0.2s;

      &:hover {
        opacity: 0.9;
      }
    }

    .daemon-instructions {
      width: 100%;
      text-align: left;
      margin-top: 8px;

      h4 {
        font-size: 12px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--omega-text-muted);
        margin-bottom: 6px;
      }

      pre {
        padding: 12px;
        border-radius: var(--omega-radius-md);
        background: rgba(0, 0, 0, 0.4);
        color: #30d158;
        font-size: 11px;
        line-height: 1.5;
        overflow-x: auto;
      }
    }
  `]
})
export class StorageNodesComponent implements OnInit, OnDestroy {
  private readonly apiService = inject(ApiService);
  private readonly signalRService = inject(SignalRService);

  nodes = signal<StorageNode[]>([]);
  isLoading = signal<boolean>(false);
  isGeneratingToken = signal<boolean>(false);
  isSavingNode = signal<boolean>(false);

  // Ping tracking
  pingingNodes = signal<Record<string, boolean>>({});
  pingResults = signal<Record<string, NodePingResult>>({});

  // Modals
  generateTokenModalOpen = signal<boolean>(false);
  editNodeModalOpen = signal<boolean>(false);
  deleteNodeModalOpen = signal<boolean>(false);

  selectedNode = signal<StorageNode | null>(null);

  // Generate Token Form
  newNodeName = '';
  newNodeType = 'RemoteDaemon';
  newNodeDescription = '';
  generatedToken = signal<NodeRegistrationToken | null>(null);
  isCopied = signal<boolean>(false);

  // Edit Node Form
  editNodeName = '';
  editNodeBaseUrl = '';
  editNodeIsBehindNat = false;
  editNodeStatus = 'Online';

  private sub: Subscription | null = null;

  readonly onlineNodesCount = computed(() => this.nodes().filter(n => n.status.toLowerCase() === 'online').length);
  readonly offlineNodesCount = computed(() => this.nodes().filter(n => n.status.toLowerCase() === 'offline').length);
  readonly remoteNodesCount = computed(() => this.nodes().filter(n => n.type === 'RemoteDaemon').length);
  readonly localNodesCount = computed(() => this.nodes().filter(n => n.type === 'LocalDisk').length);

  readonly totalFleetCapacity = computed(() => this.nodes().reduce((acc, n) => acc + n.totalSpaceBytes, 0));
  readonly totalFleetAvailable = computed(() => this.nodes().reduce((acc, n) => acc + n.availableSpaceBytes, 0));
  readonly totalFleetUsed = computed(() => Math.max(0, this.totalFleetCapacity() - this.totalFleetAvailable()));
  readonly fleetUsagePercentage = computed(() => {
    const total = this.totalFleetCapacity();
    if (total <= 0) return 0;
    return Math.min(100, Math.round((this.totalFleetUsed() / total) * 1000) / 10);
  });

  ngOnInit(): void {
    this.loadNodes();

    // Listen to real-time node status events from SignalR
    this.sub = this.signalRService.nodeStatusChanged$.subscribe(evt => {
      this.nodes.update(currentList => {
        return currentList.map(node => {
          if (node.id.toLowerCase() === evt.nodeId.toLowerCase()) {
            return {
              ...node,
              status: evt.status,
              isOnline: evt.status.toLowerCase() === 'online',
              lastHeartbeatAt: evt.lastSeen
            };
          }
          return node;
        });
      });
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  loadNodes(): void {
    this.isLoading.set(true);
    this.apiService.getAdminNodes().subscribe({
      next: nodes => {
        this.nodes.set(nodes);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  getRingColor(percent: number): string {
    if (percent > 90) return 'var(--omega-danger)';
    if (percent > 75) return 'var(--omega-warning)';
    return 'var(--omega-accent)';
  }

  pingNode(node: StorageNode): void {
    this.pingingNodes.update(p => ({ ...p, [node.id]: true }));

    this.apiService.pingAdminNode(node.id).subscribe({
      next: res => {
        this.pingingNodes.update(p => ({ ...p, [node.id]: false }));
        this.pingResults.update(r => ({ ...r, [node.id]: res }));
        // Update node status if reachable
        this.nodes.update(list => list.map(n => n.id === node.id ? { ...n, latencyMs: res.latencyMs, status: res.status } : n));
      },
      error: () => {
        this.pingingNodes.update(p => ({ ...p, [node.id]: false }));
      }
    });
  }

  openGenerateTokenModal(): void {
    this.newNodeName = '';
    this.newNodeType = 'RemoteDaemon';
    this.newNodeDescription = '';
    this.generatedToken.set(null);
    this.isCopied.set(false);
    this.generateTokenModalOpen.set(true);
  }

  submitGenerateToken(): void {
    if (!this.newNodeName.trim()) return;

    this.isGeneratingToken.set(true);
    const req: GenerateNodeTokenRequest = {
      name: this.newNodeName.trim(),
      type: this.newNodeType,
      description: this.newNodeDescription.trim() || undefined
    };

    this.apiService.generateNodeToken(req).subscribe({
      next: token => {
        this.isGeneratingToken.set(false);
        this.generatedToken.set(token);
        this.loadNodes();
      },
      error: () => {
        this.isGeneratingToken.set(false);
      }
    });
  }

  copyToken(): void {
    const token = this.generatedToken()?.registrationKey;
    if (token) {
      navigator.clipboard.writeText(token);
      this.isCopied.set(true);
      setTimeout(() => this.isCopied.set(false), 2000);
    }
  }

  closeTokenModal(): void {
    this.generateTokenModalOpen.set(false);
    this.generatedToken.set(null);
  }

  openEditNodeModal(node: StorageNode): void {
    this.selectedNode.set(node);
    this.editNodeName = node.name;
    this.editNodeBaseUrl = node.baseUrl || '';
    this.editNodeIsBehindNat = node.isBehindNat;
    this.editNodeStatus = node.status;
    this.editNodeModalOpen.set(true);
  }

  submitEditNode(): void {
    const node = this.selectedNode();
    if (!node) return;

    this.isSavingNode.set(true);
    const req: UpdateStorageNodeRequest = {
      name: this.editNodeName.trim(),
      baseUrl: this.editNodeBaseUrl.trim() || undefined,
      isBehindNat: this.editNodeIsBehindNat,
      status: this.editNodeStatus
    };

    this.apiService.updateAdminNode(node.id, req).subscribe({
      next: () => {
        this.isSavingNode.set(false);
        this.editNodeModalOpen.set(false);
        this.loadNodes();
      },
      error: () => {
        this.isSavingNode.set(false);
      }
    });
  }

  openDeleteNodeModal(node: StorageNode): void {
    this.selectedNode.set(node);
    this.deleteNodeModalOpen.set(true);
  }

  submitDeleteNode(): void {
    const node = this.selectedNode();
    if (!node) return;

    this.isSavingNode.set(true);
    this.apiService.deleteAdminNode(node.id).subscribe({
      next: () => {
        this.isSavingNode.set(false);
        this.deleteNodeModalOpen.set(false);
        this.loadNodes();
      },
      error: () => {
        this.isSavingNode.set(false);
      }
    });
  }
}
