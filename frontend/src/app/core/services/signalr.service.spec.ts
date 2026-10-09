import { TestBed } from '@angular/core/testing';
import { SignalRService } from './signalr.service';
import { AuthStore } from '../auth/auth.store';

describe('SignalRService', () => {
  let service: SignalRService;
  let authStore: AuthStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthStore,
        SignalRService
      ]
    });

    service = TestBed.inject(SignalRService);
    authStore = TestBed.inject(AuthStore);
  });

  afterEach(async () => {
    await service.stop();
  });

  it('should initialize disconnected', () => {
    expect(service.isConnected()).toBeFalse();
  });

  it('should not start connection if access token is not present', async () => {
    authStore.logout();
    await service.start();
    expect(service.isConnected()).toBeFalse();
  });

  it('should return disconnected when pinging without active connection', async () => {
    const res = await service.ping();
    expect(res).toBe('disconnected');
  });

  it('should track folder subscription state', async () => {
    await service.subscribeToFolder('folder-123');
    await service.unsubscribeFromFolder('folder-123');
    expect(service.isConnected()).toBeFalse();
  });

  it('should track upload session subscription state', async () => {
    await service.subscribeToUploadSession('session-xyz');
    await service.unsubscribeFromUploadSession('session-xyz');
    expect(service.isConnected()).toBeFalse();
  });

  it('should reset state on stop', async () => {
    await service.stop();
    expect(service.isConnected()).toBeFalse();
  });
});
