import { Injectable, inject, signal } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthStore } from '../auth/auth.store';
import {
  DirectoryChangedEvent,
  FileProgressEvent,
  ForceLogoutEvent,
  NodeStatusChangedEvent,
  QuotaUpdatedEvent
} from '../models/realtime.models';

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private readonly authStore = inject(AuthStore);
  private connection: signalR.HubConnection | null = null;

  readonly isConnected = signal<boolean>(false);

  private currentSubscribedFolderId: string | null | undefined = undefined;
  private readonly activeUploadSessions = new Set<string>();

  private readonly directoryChangedSubject = new Subject<DirectoryChangedEvent>();
  private readonly fileProgressSubject = new Subject<FileProgressEvent>();
  private readonly quotaUpdatedSubject = new Subject<QuotaUpdatedEvent>();
  private readonly nodeStatusChangedSubject = new Subject<NodeStatusChangedEvent>();
  private readonly forceLogoutSubject = new Subject<ForceLogoutEvent>();

  readonly directoryChanged$: Observable<DirectoryChangedEvent> = this.directoryChangedSubject.asObservable();
  readonly fileProgress$: Observable<FileProgressEvent> = this.fileProgressSubject.asObservable();
  readonly quotaUpdated$: Observable<QuotaUpdatedEvent> = this.quotaUpdatedSubject.asObservable();
  readonly nodeStatusChanged$: Observable<NodeStatusChangedEvent> = this.nodeStatusChangedSubject.asObservable();
  readonly forceLogout$: Observable<ForceLogoutEvent> = this.forceLogoutSubject.asObservable();

  async start(): Promise<void> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      return;
    }

    const token = this.authStore.accessToken();
    if (!token) {
      return;
    }

    try {
      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(environment.hubUrl, {
          accessTokenFactory: () => this.authStore.accessToken() ?? '',
          skipNegotiation: false,
          transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      this.registerHandlers();

      await this.connection.start();
      this.isConnected.set(true);

      this.connection.onreconnected(async () => {
        this.isConnected.set(true);

        // Auto resubscribe to the active folder
        if (this.currentSubscribedFolderId !== undefined) {
          await this.invokeSubscribeToFolder(this.currentSubscribedFolderId);
        }

        // Auto resubscribe to all active upload sessions
        for (const sessionId of this.activeUploadSessions) {
          await this.invokeSubscribeToUploadSession(sessionId);
        }
      });

      this.connection.onreconnecting(() => {
        this.isConnected.set(false);
      });

      this.connection.onclose(() => {
        this.isConnected.set(false);
      });
    } catch {
      this.isConnected.set(false);
    }
  }

  async stop(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
      } catch {
        // Ignore
      } finally {
        this.connection = null;
        this.isConnected.set(false);
        this.currentSubscribedFolderId = undefined;
        this.activeUploadSessions.clear();
      }
    }
  }

  private registerHandlers(): void {
    if (!this.connection) return;

    this.connection.on('DirectoryChanged', (folderId: string | null, changeType: string, item: any) => {
      this.directoryChangedSubject.next({ folderId, changeType, item });
    });

    this.connection.on('FileProgress', (sessionId: string, percent: number, speedBytesPerSec: number) => {
      this.fileProgressSubject.next({ sessionId, percent, speedBytesPerSec });
    });

    this.connection.on('QuotaUpdated', (usedBytes: number, totalBytes: number) => {
      this.authStore.updateQuota(usedBytes, totalBytes);
      this.quotaUpdatedSubject.next({ usedBytes, totalBytes });
    });

    this.connection.on('NodeStatusChanged', (nodeId: string, status: string, lastSeen: string) => {
      this.nodeStatusChangedSubject.next({ nodeId, status, lastSeen });
    });

    this.connection.on('ForceLogout', (reason: string) => {
      this.forceLogoutSubject.next({ reason });
      this.authStore.logout();
    });
  }

  async subscribeToFolder(folderId?: string | null): Promise<void> {
    this.currentSubscribedFolderId = folderId ?? null;
    await this.invokeSubscribeToFolder(folderId ?? null);
  }

  async unsubscribeFromFolder(folderId?: string | null): Promise<void> {
    if (this.currentSubscribedFolderId === (folderId ?? null)) {
      this.currentSubscribedFolderId = undefined;
    }
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.invoke('UnsubscribeFromFolder', folderId ?? null);
      } catch {
        // Hub invoke error handling
      }
    }
  }

  async subscribeToUploadSession(sessionId: string): Promise<void> {
    this.activeUploadSessions.add(sessionId);
    await this.invokeSubscribeToUploadSession(sessionId);
  }

  async unsubscribeFromUploadSession(sessionId: string): Promise<void> {
    this.activeUploadSessions.delete(sessionId);
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.invoke('UnsubscribeFromUploadSession', sessionId);
      } catch {
        // Hub invoke error handling
      }
    }
  }

  private async invokeSubscribeToFolder(folderId: string | null): Promise<void> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.invoke('SubscribeToFolder', folderId);
      } catch {
        // Hub invoke error handling
      }
    }
  }

  private async invokeSubscribeToUploadSession(sessionId: string): Promise<void> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.invoke('SubscribeToUploadSession', sessionId);
      } catch {
        // Hub invoke error handling
      }
    }
  }

  async ping(): Promise<string> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      return await this.connection.invoke<string>('Ping');
    }
    return 'disconnected';
  }
}
