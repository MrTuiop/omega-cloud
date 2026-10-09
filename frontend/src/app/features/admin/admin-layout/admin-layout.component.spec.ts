import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AdminLayoutComponent } from './admin-layout.component';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';

describe('AdminLayoutComponent', () => {
  let component: AdminLayoutComponent;
  let fixture: ComponentFixture<AdminLayoutComponent>;
  let apiService: ApiService;
  let authStore: AuthStore;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminLayoutComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ApiService,
        AuthStore
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminLayoutComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);
    authStore = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);

    authStore.setUser({
      id: 'admin-id',
      username: 'rootadmin',
      email: 'root@omega.local',
      role: 'Admin',
      allocatedQuotaBytes: 1000,
      usedQuotaBytes: 100,
      createdAt: ''
    });

    fixture.detectChanges();
  });

  it('should create admin layout component', () => {
    expect(component).toBeTruthy();
  });

  it('should compute user initials accurately', () => {
    expect(component.userInitials()).toBe('RO');
  });

  it('should logout and navigate to login', () => {
    spyOn(apiService, 'logout').and.returnValue(of({ message: 'Logged out' }));
    spyOn(authStore, 'logout');
    spyOn(router, 'navigate');

    component.logout();

    expect(apiService.logout).toHaveBeenCalled();
    expect(authStore.logout).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
