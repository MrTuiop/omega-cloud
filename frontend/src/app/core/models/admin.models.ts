import { UserSession } from './auth.models';

export interface AdminUser {
  id: string;
  username: string;
  email: string;
  role: string;
  allocatedQuotaBytes: number;
  usedQuotaBytes: number;
  quotaPercent: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  activeSessionsCount: number;
}

export interface AdminUserDetail extends AdminUser {
  sessions: UserSession[];
}

export interface CreateUserRequest {
  username: string;
  email: string;
  password: string;
  role: string;
  allocatedQuotaBytes?: number;
}

export interface UpdateUserRequest {
  role?: string;
  allocatedQuotaBytes?: number;
  isActive?: boolean;
}

export interface AdminResetPasswordRequest {
  newPassword: string;
}

export interface StorageNode {
  id: string;
  name: string;
  type: string; // 'LocalDisk' | 'RemoteDaemon'
  baseUrl?: string | null;
  isBehindNat: boolean;
  status: string; // 'Online' | 'Offline' | 'Degraded' | 'Maintenance'
  totalSpaceBytes: number;
  availableSpaceBytes: number;
  usedSpaceBytes: number;
  freeSpacePercentage: number;
  usedSpacePercentage: number;
  cpuLoad?: number | null;
  diskStatus?: string | null;
  latencyMs?: number | null;
  lastHeartbeatAt: string;
  secondsSinceLastHeartbeat: number;
  isOnline: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface UpdateStorageNodeRequest {
  name?: string;
  baseUrl?: string;
  isBehindNat?: boolean;
  status?: string;
}

export interface GenerateNodeTokenRequest {
  name?: string;
  type?: string;
  description?: string;
}

export interface NodeRegistrationToken {
  nodeId: string;
  name: string;
  registrationKey: string;
  createdAt: string;
}

export interface NodePingResult {
  nodeId: string;
  isReachable: boolean;
  latencyMs?: number | null;
  status: string;
}

export interface AuditLog {
  id: number;
  userId?: string | null;
  userName?: string | null;
  action: string;
  resource: string;
  resourceId?: string | null;
  ipAddress: string;
  detailsJson?: string | null;
  timestamp: string;
}

export interface SystemHealth {
  cpuLoadPercentage: number;
  usedMemoryBytes: number;
  totalMemoryBytes: number;
  memoryUsagePercentage: number;
  storageFreeBytes: number;
  storageTotalBytes: number;
  storageUsedBytes: number;
  storageUsagePercentage: number;
  databaseStatus: string;
  databaseLatencyMs: number;
  totalNodes: number;
  onlineNodes: number;
  offlineNodes: number;
  totalUsers: number;
  activeSessionsCount: number;
  serverUptimeSeconds: number;
  osDescription: string;
  frameworkDescription: string;
  startedAt: string;
}
