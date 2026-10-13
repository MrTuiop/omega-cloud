import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ApiService } from '../../core/services/api.service';
import { AuthStore } from '../../core/auth/auth.store';
import { I18nService } from '../../core/services/i18n.service';
import { FileItem } from '../../core/models/file.models';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  let apiService: ApiService;
  let authStore: AuthStore;

  const mockFile: FileItem = {
    id: 'f-1',
    userId: 'u-1',
    storageBlobId: 'b-1',
    name: 'document.pdf',
    extension: 'pdf',
    mimeType: 'application/pdf',
    sizeBytes: 1024,
    isShared: false,
    isDeleted: false,
    isFavorite: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ApiService,
        AuthStore,
        I18nService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);
    authStore = TestBed.inject(AuthStore);

    authStore.setLoginResponse({
      accessToken: 'token',
      refreshToken: 'refresh',
      expiresInSeconds: 900,
      user: {
        id: 'u-1',
        username: 'alice',
        email: 'alice@test.local',
        role: 'User',
        allocatedQuotaBytes: 10000,
        usedQuotaBytes: 2500,
        createdAt: ''
      }
    });
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should create dashboard component and load recent files', () => {
    spyOn(apiService, 'getRecentFiles').and.returnValue(of([mockFile]));
    fixture.detectChanges();

    expect(component).toBeTruthy();
    expect(apiService.getRecentFiles).toHaveBeenCalled();
    expect(component.recentFiles().length).toBe(1);
    expect(component.recentFiles()[0].name).toBe('document.pdf');
  });

  it('should calculate available quota bytes correctly', () => {
    expect(component.availableQuotaBytes()).toBe(7500);
  });

  it('should provide language options for glass dropdown', () => {
    expect(component.languageOptions.length).toBeGreaterThan(0);
    expect(component.languageOptions.some(l => l.value === 'ru')).toBeTrue();
  });
});
