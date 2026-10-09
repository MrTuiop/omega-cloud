export interface UserProfile {
  id: string;
  username: string;
  email: string;
  role: string;
  allocatedQuotaBytes: number;
  usedQuotaBytes: number;
  createdAt: string;
}

export interface UserSession {
  id: string;
  deviceName?: string;
  ipAddress?: string;
  userAgent?: string;
  createdAt: string;
  lastActiveAt: string;
  expiresAt: string;
  isCurrent: boolean;
}

export interface LoginRequest {
  usernameOrEmail: string;
  password: string;
  deviceName?: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number;
  user: UserProfile;
}

export interface RefreshTokenRequest {
  refreshToken?: string;
}

export interface RefreshTokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number;
}
