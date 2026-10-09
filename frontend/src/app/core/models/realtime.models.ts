export interface NodeStatusChangedEvent {
  nodeId: string;
  status: string;
  lastSeen: string;
}

export interface FileProgressEvent {
  sessionId: string;
  percent: number;
  speedBytesPerSec: number;
}

export interface DirectoryChangedEvent {
  folderId?: string | null;
  changeType: string;
  item?: any;
}

export interface QuotaUpdatedEvent {
  usedBytes: number;
  totalBytes: number;
}

export interface ForceLogoutEvent {
  reason: string;
}
