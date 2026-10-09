import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { of, throwError, Subject } from 'rxjs';
import { authInterceptor, resetInterceptorState } from './auth.interceptor';
import { AuthStore } from './auth.store';
import { ApiService } from '../services/api.service';
import { environment } from '../../../environments/environment';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let authStore: AuthStore;
  let apiService: jasmine.SpyObj<ApiService>;
  let router: jasmine.SpyObj<Router>;
  const baseUrl = environment.apiUrl;

  beforeEach(() => {
    resetInterceptorState();

    const apiSpy = jasmine.createSpyObj('ApiService', ['refreshToken']);
    const routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      providers: [
        AuthStore,
        { provide: ApiService, useValue: apiSpy },
        { provide: Router, useValue: routerSpy },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    authStore = TestBed.inject(AuthStore);
    apiService = TestBed.inject(ApiService) as jasmine.SpyObj<ApiService>;
    router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
  });

  afterEach(() => {
    httpMock.verify();
    resetInterceptorState();
  });

  it('should attach Bearer token to regular requests when token is present', () => {
    authStore.setTokens('valid-token-123', 'refresh-token-456');

    httpClient.get(`${baseUrl}/folders`).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/folders`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer valid-token-123');
    req.flush({ folders: [] });
  });

  it('should not attach Bearer token to /auth/login or /auth/refresh', () => {
    authStore.setTokens('valid-token-123', 'refresh-token-456');

    httpClient.post(`${baseUrl}/auth/login`, {}).subscribe();
    const loginReq = httpMock.expectOne(`${baseUrl}/auth/login`);
    expect(loginReq.request.headers.has('Authorization')).toBeFalse();
    loginReq.flush({});

    httpClient.post(`${baseUrl}/auth/refresh`, {}).subscribe();
    const refreshReq = httpMock.expectOne(`${baseUrl}/auth/refresh`);
    expect(refreshReq.request.headers.has('Authorization')).toBeFalse();
    refreshReq.flush({});
  });

  it('should refresh token and retry request when receiving 401', () => {
    authStore.setTokens('expired-access-token', 'valid-refresh-token');

    apiService.refreshToken.and.returnValue(of({
      accessToken: 'new-access-token',
      refreshToken: 'new-refresh-token',
      expiresInSeconds: 900
    }));

    let responseData: any;
    httpClient.get(`${baseUrl}/folders`).subscribe(res => {
      responseData = res;
    });

    // Initial request fails with 401
    const initialReq = httpMock.expectOne(`${baseUrl}/folders`);
    expect(initialReq.request.headers.get('Authorization')).toBe('Bearer expired-access-token');
    initialReq.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    // Verify refreshToken was called with correct payload
    expect(apiService.refreshToken).toHaveBeenCalledWith({ refreshToken: 'valid-refresh-token' });

    // The retry request should be made with the new access token
    const retriedReq = httpMock.expectOne(`${baseUrl}/folders`);
    expect(retriedReq.request.headers.get('Authorization')).toBe('Bearer new-access-token');
    retriedReq.flush({ folders: [{ id: 'f1', name: 'Docs' }] });

    expect(responseData).toEqual({ folders: [{ id: 'f1', name: 'Docs' }] });
    expect(authStore.accessToken()).toBe('new-access-token');
    expect(authStore.refreshToken()).toBe('new-refresh-token');
  });

  it('should queue concurrent 401 requests and retry them all once refreshed', () => {
    authStore.setTokens('expired-access-token', 'valid-refresh-token');

    const refreshSubject = new Subject<any>();
    apiService.refreshToken.and.returnValue(refreshSubject.asObservable());

    let res1: any, res2: any;
    httpClient.get(`${baseUrl}/folders`).subscribe(r => (res1 = r));
    httpClient.get(`${baseUrl}/files/123/metadata`).subscribe(r => (res2 = r));

    // Both fail with 401
    const req1 = httpMock.expectOne(`${baseUrl}/folders`);
    const req2 = httpMock.expectOne(`${baseUrl}/files/123/metadata`);

    req1.flush({}, { status: 401, statusText: 'Unauthorized' });
    req2.flush({}, { status: 401, statusText: 'Unauthorized' });

    // Refresh was invoked only once
    expect(apiService.refreshToken).toHaveBeenCalledTimes(1);

    // Emit the refreshed token
    refreshSubject.next({
      accessToken: 'brand-new-token',
      refreshToken: 'brand-new-refresh',
      expiresInSeconds: 900
    });
    refreshSubject.complete();

    // Both requests are retried with the new token
    const retry1 = httpMock.expectOne(`${baseUrl}/folders`);
    const retry2 = httpMock.expectOne(`${baseUrl}/files/123/metadata`);

    expect(retry1.request.headers.get('Authorization')).toBe('Bearer brand-new-token');
    expect(retry2.request.headers.get('Authorization')).toBe('Bearer brand-new-token');

    retry1.flush({ folders: [] });
    retry2.flush({ id: '123', name: 'test.pdf' });

    expect(res1).toEqual({ folders: [] });
    expect(res2).toEqual({ id: '123', name: 'test.pdf' });
  });

  it('should logout and redirect to /login when refresh token fails', () => {
    authStore.setTokens('expired-access-token', 'dead-refresh-token');

    apiService.refreshToken.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 401, statusText: 'Invalid refresh token' }))
    );

    let errorOccurred = false;
    httpClient.get(`${baseUrl}/folders`).subscribe({
      error: () => {
        errorOccurred = true;
      }
    });

    const req = httpMock.expectOne(`${baseUrl}/folders`);
    req.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(errorOccurred).toBeTrue();
    expect(authStore.accessToken()).toBeNull();
    expect(authStore.refreshToken()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('should immediately logout and navigate to /login on 401 if no refresh token is present', () => {
    authStore.setTokens('temp-token', '');
    authStore.refreshToken.set(null);

    let errorOccurred = false;
    httpClient.get(`${baseUrl}/folders`).subscribe({
      error: () => {
        errorOccurred = true;
      }
    });

    const req = httpMock.expectOne(`${baseUrl}/folders`);
    req.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(errorOccurred).toBeTrue();
    expect(apiService.refreshToken).not.toHaveBeenCalled();
    expect(authStore.accessToken()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
