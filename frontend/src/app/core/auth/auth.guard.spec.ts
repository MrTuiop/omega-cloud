import { TestBed } from '@angular/core/testing';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { authGuard } from './auth.guard';
import { adminGuard } from './admin.guard';
import { AuthStore } from './auth.store';

describe('Guards', () => {
  let authStore: AuthStore;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        AuthStore,
        {
          provide: Router,
          useValue: {
            createUrlTree: jasmine.createSpy('createUrlTree').and.callFake((path: any[]) => path.join('/'))
          }
        }
      ]
    });

    authStore = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('authGuard should allow access when authenticated', () => {
    authStore.accessToken.set('valid-token');
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/drive' } as RouterStateSnapshot)
    );
    expect(result).toBeTrue();
  });

  it('authGuard should redirect to /login when unauthenticated', () => {
    authStore.accessToken.set(null);
    TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/drive' } as RouterStateSnapshot)
    );
    expect(router.createUrlTree).toHaveBeenCalled();
  });

  it('adminGuard should allow access when user has Admin role', () => {
    authStore.accessToken.set('valid-token');
    authStore.currentUser.set({
      id: '1',
      username: 'admin',
      email: 'a@test.local',
      role: 'Admin',
      allocatedQuotaBytes: 100,
      usedQuotaBytes: 0,
      createdAt: ''
    });

    const result = TestBed.runInInjectionContext(() => adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
    expect(result).toBeTrue();
  });

  it('adminGuard should redirect non-admins to /drive', () => {
    authStore.accessToken.set('valid-token');
    authStore.currentUser.set({
      id: '2',
      username: 'user',
      email: 'u@test.local',
      role: 'User',
      allocatedQuotaBytes: 100,
      usedQuotaBytes: 0,
      createdAt: ''
    });

    TestBed.runInInjectionContext(() => adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
    expect(router.createUrlTree).toHaveBeenCalledWith(['/drive']);
  });
});
