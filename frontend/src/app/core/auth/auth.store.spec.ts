import { TestBed } from '@angular/core/testing';
import { AuthStore } from './auth.store';
import { LoginResponse } from '../models/auth.models';

describe('AuthStore', () => {
  let store: AuthStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [AuthStore]
    });
    store = TestBed.inject(AuthStore);
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should initialize with unauthenticated state', () => {
    expect(store.isAuthenticated()).toBeFalse();
    expect(store.currentUser()).toBeNull();
    expect(store.isAdmin()).toBeFalse();
    expect(store.quotaPercent()).toBe(0);
  });

  it('should set login response correctly and compute isAuthenticated & isAdmin', () => {
    const loginRes: LoginResponse = {
      accessToken: 'access-123',
      refreshToken: 'refresh-456',
      expiresInSeconds: 900,
      user: {
        id: 'user-1',
        username: 'admin',
        email: 'admin@test.local',
        role: 'Admin',
        allocatedQuotaBytes: 107374182400, // 100 GB
        usedQuotaBytes: 21474836480, // 20 GB
        createdAt: new Date().toISOString()
      }
    };

    store.setLoginResponse(loginRes);

    expect(store.isAuthenticated()).toBeTrue();
    expect(store.isAdmin()).toBeTrue();
    expect(store.accessToken()).toBe('access-123');
    expect(store.refreshToken()).toBe('refresh-456');
    expect(store.currentUser()?.username).toBe('admin');
    expect(store.quotaPercent()).toBe(20);
  });

  it('should update quota and percentage reactively', () => {
    store.setLoginResponse({
      accessToken: 'tok',
      refreshToken: 'ref',
      expiresInSeconds: 900,
      user: {
        id: 'u1',
        username: 'user1',
        email: 'u1@test.local',
        role: 'User',
        allocatedQuotaBytes: 1000,
        usedQuotaBytes: 250,
        createdAt: ''
      }
    });

    expect(store.quotaPercent()).toBe(25);

    store.updateQuota(750);
    expect(store.quotaPercent()).toBe(75);
    expect(store.currentUser()?.usedQuotaBytes).toBe(750);
  });

  it('should clear all state on logout', () => {
    store.setLoginResponse({
      accessToken: 'tok',
      refreshToken: 'ref',
      expiresInSeconds: 900,
      user: {
        id: 'u1',
        username: 'user1',
        email: 'u1@test.local',
        role: 'User',
        allocatedQuotaBytes: 1000,
        usedQuotaBytes: 250,
        createdAt: ''
      }
    });

    store.logout();

    expect(store.isAuthenticated()).toBeFalse();
    expect(store.accessToken()).toBeNull();
    expect(store.refreshToken()).toBeNull();
    expect(store.currentUser()).toBeNull();
    expect(store.isAdmin()).toBeFalse();
  });
});
