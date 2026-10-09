import { Injectable, signal, computed } from '@angular/core';
import { UserProfile, LoginResponse } from '../models/auth.models';

const ACCESS_TOKEN_KEY = 'omega_access_token';
const REFRESH_TOKEN_KEY = 'omega_refresh_token';
const USER_KEY = 'omega_user_profile';

@Injectable({
  providedIn: 'root'
})
export class AuthStore {
  readonly accessToken = signal<string | null>(this.getStoredItem(ACCESS_TOKEN_KEY));
  readonly refreshToken = signal<string | null>(this.getStoredItem(REFRESH_TOKEN_KEY));
  readonly currentUser = signal<UserProfile | null>(this.getStoredUser());

  readonly isAuthenticated = computed(() => !!this.accessToken());
  readonly isAdmin = computed(() => {
    const user = this.currentUser();
    return user?.role?.toLowerCase() === 'admin';
  });

  readonly quotaPercent = computed(() => {
    const user = this.currentUser();
    if (!user || !user.allocatedQuotaBytes || user.allocatedQuotaBytes <= 0) return 0;
    const used = Math.max(0, user.usedQuotaBytes || 0);
    const pct = (used / user.allocatedQuotaBytes) * 100;
    return Math.min(100, Math.max(0, Math.round(pct * 10) / 10));
  });

  setLoginResponse(response: LoginResponse): void {
    this.accessToken.set(response.accessToken);
    this.refreshToken.set(response.refreshToken);
    this.currentUser.set(response.user);

    this.persistItem(ACCESS_TOKEN_KEY, response.accessToken);
    this.persistItem(REFRESH_TOKEN_KEY, response.refreshToken);
    this.persistItem(USER_KEY, JSON.stringify(response.user));
  }

  setTokens(accessToken: string, refreshToken: string): void {
    this.accessToken.set(accessToken);
    this.refreshToken.set(refreshToken);
    this.persistItem(ACCESS_TOKEN_KEY, accessToken);
    this.persistItem(REFRESH_TOKEN_KEY, refreshToken);
  }

  setUser(user: UserProfile): void {
    this.currentUser.set(user);
    this.persistItem(USER_KEY, JSON.stringify(user));
  }

  updateQuota(usedBytes: number, allocatedBytes?: number): void {
    const current = this.currentUser();
    if (current) {
      const updated: UserProfile = {
        ...current,
        usedQuotaBytes: usedBytes,
        allocatedQuotaBytes: allocatedBytes ?? current.allocatedQuotaBytes
      };
      this.setUser(updated);
    }
  }

  logout(): void {
    this.accessToken.set(null);
    this.refreshToken.set(null);
    this.currentUser.set(null);

    this.removeItem(ACCESS_TOKEN_KEY);
    this.removeItem(REFRESH_TOKEN_KEY);
    this.removeItem(USER_KEY);
  }

  private getStoredItem(key: string): string | null {
    try {
      return typeof localStorage !== 'undefined' ? localStorage.getItem(key) : null;
    } catch {
      return null;
    }
  }

  private getStoredUser(): UserProfile | null {
    try {
      const json = typeof localStorage !== 'undefined' ? localStorage.getItem(USER_KEY) : null;
      return json ? JSON.parse(json) : null;
    } catch {
      return null;
    }
  }

  private persistItem(key: string, value: string): void {
    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(key, value);
      }
    } catch {
      // Ignore
    }
  }

  private removeItem(key: string): void {
    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.removeItem(key);
      }
    } catch {
      // Ignore
    }
  }
}
