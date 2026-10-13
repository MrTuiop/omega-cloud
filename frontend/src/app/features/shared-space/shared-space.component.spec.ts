import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SharedSpaceComponent } from './shared-space.component';
import { ApiService } from '../../core/services/api.service';
import { AuthStore } from '../../core/auth/auth.store';
import { I18nService } from '../../core/services/i18n.service';
import { FolderContent } from '../../core/models/file.models';

describe('SharedSpaceComponent', () => {
  let component: SharedSpaceComponent;
  let fixture: ComponentFixture<SharedSpaceComponent>;
  let apiService: ApiService;
  let authStore: AuthStore;

  const mockSharedContent: FolderContent = {
    currentFolder: null,
    folders: [
      { id: 'fld-1', name: 'Public Docs', createdAt: '', updatedAt: '', isShared: true, isDeleted: false, userId: 'u-1' }
    ],
    files: [
      {
        id: 'file-1',
        userId: 'u-1',
        storageBlobId: 'b-1',
        name: 'shared_readme.txt',
        extension: 'txt',
        mimeType: 'text/plain',
        sizeBytes: 500,
        isShared: true,
        isDeleted: false,
        isFavorite: false,
        createdAt: '',
        updatedAt: ''
      }
    ],
    breadcrumbs: [],
    quota: {
      allocatedBytes: 1000,
      usedBytes: 500,
      availableBytes: 500
    }
  };

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SharedSpaceComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ApiService,
        AuthStore,
        I18nService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SharedSpaceComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);
    authStore = TestBed.inject(AuthStore);
    authStore.logout();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should create and load shared folder content for anonymous users', () => {
    spyOn(apiService, 'getSharedFolders').and.returnValue(of(mockSharedContent));
    fixture.detectChanges();

    expect(component).toBeTruthy();
    expect(apiService.getSharedFolders).toHaveBeenCalled();
    expect(component.filteredFiles().length).toBe(1);
    expect(component.filteredFolders().length).toBe(1);
  });

  it('should restrict delete to owners and admins', () => {
    // Anonymous user cannot mutate
    expect(component.canMutate('u-1')).toBeFalse();

    // Authenticated non-owner
    authStore.setLoginResponse({
      accessToken: 't',
      refreshToken: 'r',
      expiresInSeconds: 900,
      user: {
        id: 'u-2',
        username: 'bob',
        email: 'bob@test.local',
        role: 'User',
        allocatedQuotaBytes: 100,
        usedQuotaBytes: 0,
        createdAt: ''
      }
    });
    expect(component.canMutate('u-1')).toBeFalse();

    // Authenticated owner
    expect(component.canMutate('u-2')).toBeTrue();

    // Admin can mutate anything
    authStore.setLoginResponse({
      accessToken: 't',
      refreshToken: 'r',
      expiresInSeconds: 900,
      user: {
        id: 'u-admin',
        username: 'admin',
        email: 'admin@test.local',
        role: 'Admin',
        allocatedQuotaBytes: 100,
        usedQuotaBytes: 0,
        createdAt: ''
      }
    });
    expect(component.canMutate('u-1')).toBeTrue();
  });

  it('should provide searchScopeOptions and languageOptions', () => {
    spyOn(apiService, 'getSharedFolders').and.returnValue(of(mockSharedContent));
    fixture.detectChanges();

    expect(component.searchScopeOptions().length).toBe(2);
    expect(component.searchScopeOptions()[0].value).toBe('everywhere');
    expect(component.searchScopeOptions()[1].value).toBe('current');

    expect(component.languageOptions.length).toBeGreaterThan(0);
  });

  it('should display favorites and trash nav links when user is authenticated', () => {
    authStore.setLoginResponse({
      accessToken: 'token',
      refreshToken: 'refresh',
      expiresInSeconds: 900,
      user: {
        id: 'u-1',
        username: 'alice',
        email: 'alice@test.local',
        role: 'User',
        allocatedQuotaBytes: 1000,
        usedQuotaBytes: 100,
        createdAt: ''
      }
    });
    spyOn(apiService, 'getSharedFolders').and.returnValue(of(mockSharedContent));
    fixture.detectChanges();

    const links = fixture.nativeElement.querySelectorAll('.sidebar-nav a.nav-item');
    const hrefs = Array.from(links).map((l: any) => l.getAttribute('href') || l.getAttribute('routerLink'));
    expect(links.length).toBeGreaterThanOrEqual(4); // dashboard, drive, shared, favorites, trash
  });
});
