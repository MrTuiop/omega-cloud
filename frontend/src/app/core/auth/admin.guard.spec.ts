import { TestBed } from '@angular/core/testing';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { adminGuard } from './admin.guard';
import { AuthStore } from './auth.store';

describe('adminGuard', () => {
  let authStore: AuthStore;
  let router: jasmine.SpyObj<Router>;

  beforeEach(() => {
    const routerSpy = jasmine.createSpyObj('Router', ['createUrlTree']);

    TestBed.configureTestingModule({
      providers: [
        AuthStore,
        { provide: Router, useValue: routerSpy }
      ]
    });

    authStore = TestBed.inject(AuthStore);
    router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
  });

  it('should allow access for authenticated Admin user', () => {
    authStore.setTokens('access-token', 'refresh-token');
    authStore.setUser({
      id: 'admin-1',
      username: 'admin',
      email: 'admin@omega.local',
      role: 'Admin',
      allocatedQuotaBytes: 1000,
      usedQuotaBytes: 100,
      createdAt: ''
    });

    const route = {} as ActivatedRouteSnapshot;
    const state = {} as RouterStateSnapshot;

    const result = TestBed.runInInjectionContext(() => adminGuard(route, state));
    expect(result).toBeTrue();
  });

  it('should redirect non-admin user to /drive', () => {
    authStore.setTokens('access-token', 'refresh-token');
    authStore.setUser({
      id: 'user-1',
      username: 'john',
      email: 'john@omega.local',
      role: 'User',
      allocatedQuotaBytes: 1000,
      usedQuotaBytes: 100,
      createdAt: ''
    });

    const treeMock = {} as any;
    router.createUrlTree.and.returnValue(treeMock);

    const route = {} as ActivatedRouteSnapshot;
    const state = {} as RouterStateSnapshot;

    const result = TestBed.runInInjectionContext(() => adminGuard(route, state));
    expect(router.createUrlTree).toHaveBeenCalledWith(['/drive']);
    expect(result).toBe(treeMock);
  });

  it('should redirect unauthenticated user to /drive', () => {
    authStore.logout();

    const treeMock = {} as any;
    router.createUrlTree.and.returnValue(treeMock);

    const route = {} as ActivatedRouteSnapshot;
    const state = {} as RouterStateSnapshot;

    const result = TestBed.runInInjectionContext(() => adminGuard(route, state));
    expect(router.createUrlTree).toHaveBeenCalledWith(['/drive']);
    expect(result).toBe(treeMock);
  });
});
