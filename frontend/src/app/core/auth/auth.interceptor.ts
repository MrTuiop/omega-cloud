import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError, BehaviorSubject, filter, take } from 'rxjs';
import { AuthStore } from './auth.store';
import { ApiService } from '../services/api.service';

let isRefreshing = false;
let refreshTokenSubject = new BehaviorSubject<string | null>(null);

export function resetInterceptorState(): void {
  isRefreshing = false;
  refreshTokenSubject = new BehaviorSubject<string | null>(null);
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);
  const apiService = inject(ApiService);
  const token = authStore.accessToken();

  // Do not add token to login or refresh endpoints
  const isAuthRequest = req.url.includes('/auth/login') || req.url.includes('/auth/refresh');

  let authReq = req;
  if (token && !isAuthRequest) {
    authReq = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !isAuthRequest) {
        const refreshToken = authStore.refreshToken();
        if (refreshToken) {
          if (!isRefreshing) {
            isRefreshing = true;
            refreshTokenSubject.next(null);

            return apiService.refreshToken({ refreshToken }).pipe(
              switchMap(res => {
                isRefreshing = false;
                authStore.setTokens(res.accessToken, res.refreshToken);
                refreshTokenSubject.next(res.accessToken);
                const retryReq = req.clone({
                  setHeaders: {
                    Authorization: `Bearer ${res.accessToken}`
                  }
                });
                return next(retryReq);
              }),
              catchError(refreshErr => {
                isRefreshing = false;
                refreshTokenSubject.next(null);
                authStore.logout();
                router.navigate(['/login']);
                return throwError(() => refreshErr);
              })
            );
          } else {
            // A refresh is already in progress: queue this request until the new token arrives
            return refreshTokenSubject.pipe(
              filter((newToken): newToken is string => newToken !== null),
              take(1),
              switchMap(newToken => {
                const retryReq = req.clone({
                  setHeaders: {
                    Authorization: `Bearer ${newToken}`
                  }
                });
                return next(retryReq);
              })
            );
          }
        } else {
          authStore.logout();
          router.navigate(['/login']);
        }
      }
      return throwError(() => error);
    })
  );
};
