import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { UsersManagementComponent } from './users-management.component';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { AdminUser } from '../../../core/models/admin.models';

describe('UsersManagementComponent', () => {
  let component: UsersManagementComponent;
  let fixture: ComponentFixture<UsersManagementComponent>;
  let apiService: ApiService;
  let authStore: AuthStore;

  const mockUsers: AdminUser[] = [
    {
      id: 'u-1',
      username: 'alice',
      email: 'alice@test.local',
      role: 'Admin',
      allocatedQuotaBytes: 100_000,
      usedQuotaBytes: 25_000,
      quotaPercent: 25,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      activeSessionsCount: 2
    },
    {
      id: 'u-2',
      username: 'bob',
      email: 'bob@test.local',
      role: 'User',
      allocatedQuotaBytes: 50_000,
      usedQuotaBytes: 40_000,
      quotaPercent: 80,
      isActive: false,
      createdAt: '2026-01-02T00:00:00Z',
      updatedAt: '2026-01-02T00:00:00Z',
      activeSessionsCount: 0
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UsersManagementComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService,
        AuthStore
      ]
    }).compileComponents();

    apiService = TestBed.inject(ApiService);
    authStore = TestBed.inject(AuthStore);
    spyOn(apiService, 'getAdminUsers').and.returnValue(of(mockUsers));

    fixture = TestBed.createComponent(UsersManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create users management component', () => {
    expect(component).toBeTruthy();
    expect(component.users().length).toBe(2);
  });

  it('should compute metrics correctly', () => {
    expect(component.totalUsersCount()).toBe(2);
    expect(component.activeUsersCount()).toBe(1);
    expect(component.totalSessionsCount()).toBe(2);
    expect(component.totalAllocatedQuota()).toBe(150_000);
    expect(component.totalUsedQuota()).toBe(65_000);
  });

  it('should filter users by search query and role', () => {
    component.searchQuery.set('alice');
    expect(component.filteredUsers().length).toBe(1);
    expect(component.filteredUsers()[0].username).toBe('alice');

    component.searchQuery.set('');
    component.roleFilter.set('User');
    expect(component.filteredUsers().length).toBe(1);
    expect(component.filteredUsers()[0].username).toBe('bob');

    component.roleFilter.set('All');
    component.statusFilter.set('Active');
    expect(component.filteredUsers().length).toBe(1);
    expect(component.filteredUsers()[0].username).toBe('alice');
  });

  it('should create user when form is submitted', () => {
    const newUser: AdminUser = {
      id: 'u-3',
      username: 'charlie',
      email: 'charlie@test.local',
      role: 'User',
      allocatedQuotaBytes: 107_374_182_400,
      usedQuotaBytes: 0,
      quotaPercent: 0,
      isActive: true,
      createdAt: '',
      updatedAt: '',
      activeSessionsCount: 0
    };
    spyOn(apiService, 'createAdminUser').and.returnValue(of(newUser));

    component.openCreateUserModal();
    component.newUsername = 'charlie';
    component.newEmail = 'charlie@test.local';
    component.newPassword = 'Password123!';
    component.newRole = 'User';

    component.createUser();

    expect(apiService.createAdminUser).toHaveBeenCalledWith({
      username: 'charlie',
      email: 'charlie@test.local',
      password: 'Password123!',
      role: 'User',
      allocatedQuotaBytes: 107_374_182_400
    });
    expect(component.createUserModalOpen()).toBeFalse();
  });

  it('should save user edit', () => {
    const updatedUser: AdminUser = {
      ...mockUsers[0],
      role: 'Admin'
    };
    spyOn(apiService, 'updateAdminUser').and.returnValue(of(updatedUser));

    component.openEditModal(mockUsers[0]);
    component.editRole = 'Admin';
    component.saveUserEdit();

    expect(apiService.updateAdminUser).toHaveBeenCalledWith('u-1', {
      role: 'Admin',
      allocatedQuotaBytes: mockUsers[0].allocatedQuotaBytes,
      isActive: true
    });
    expect(component.editUserModalOpen()).toBeFalse();
  });

  it('should reset user password', () => {
    spyOn(apiService, 'resetAdminUserPassword').and.returnValue(of({ message: 'Success' }));

    component.openResetPasswordModal(mockUsers[0]);
    component.resetPasswordValue = 'NewSecretPassword!';
    component.saveResetPassword();

    expect(apiService.resetAdminUserPassword).toHaveBeenCalledWith('u-1', {
      newPassword: 'NewSecretPassword!'
    });
    expect(component.resetPasswordModalOpen()).toBeFalse();
  });

  it('should revoke sessions', () => {
    spyOn(apiService, 'revokeAdminUserSessions').and.returnValue(of({ message: 'Success' }));

    component.openRevokeModal(mockUsers[0]);
    component.confirmRevokeSessions();

    expect(apiService.revokeAdminUserSessions).toHaveBeenCalledWith('u-1');
    expect(component.revokeModalOpen()).toBeFalse();
  });

  it('should inspect sessions and revoke a single session', () => {
    const detail = {
      ...mockUsers[0],
      sessions: [
        {
          id: 'sess-1',
          deviceName: 'MacBook Safari',
          ipAddress: '127.0.0.1',
          userAgent: 'Safari',
          createdAt: '2026-01-01T00:00:00Z',
          lastActiveAt: '2026-01-01T01:00:00Z',
          expiresAt: '2026-02-01T00:00:00Z',
          isCurrent: true
        },
        {
          id: 'sess-2',
          deviceName: 'iPhone Chrome',
          ipAddress: '192.168.1.5',
          userAgent: 'Chrome Mobile',
          createdAt: '2026-01-01T00:00:00Z',
          lastActiveAt: '2026-01-01T00:30:00Z',
          expiresAt: '2026-02-01T00:00:00Z',
          isCurrent: false
        }
      ]
    };
    spyOn(apiService, 'getAdminUser').and.returnValue(of(detail));
    spyOn(apiService, 'revokeAdminUserSession').and.returnValue(of({ message: 'Revoked' }));

    component.openSessionsModal(mockUsers[0]);
    expect(component.sessionsModalOpen()).toBeTrue();
    expect(apiService.getAdminUser).toHaveBeenCalledWith('u-1');
    expect(component.userSessions().length).toBe(2);

    component.revokeSingleSession('sess-2');
    expect(apiService.revokeAdminUserSession).toHaveBeenCalledWith('u-1', 'sess-2');
    expect(component.userSessions().length).toBe(1);
    expect(component.userSessions()[0].id).toBe('sess-1');
  });

  it('should delete user', () => {
    spyOn(apiService, 'deleteAdminUser').and.returnValue(of(void 0));

    component.openDeleteModal(mockUsers[1]);
    component.confirmDeleteUser();

    expect(apiService.deleteAdminUser).toHaveBeenCalledWith('u-2');
    expect(component.deleteModalOpen()).toBeFalse();
  });
});
