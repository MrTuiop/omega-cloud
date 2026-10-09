import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { LoginComponent } from './login.component';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let apiService: ApiService;
  let authStore: AuthStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ApiService,
        AuthStore
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);
    authStore = TestBed.inject(AuthStore);
    fixture.detectChanges();
  });

  it('should create login component', () => {
    expect(component).toBeTruthy();
  });

  it('should fill default admin credentials on quick fill click', () => {
    component.fillDefaults();
    expect(component.username).toBe('admin');
    expect(component.password).toBe('OmegaAdmin2026!');
  });

  it('should call apiService login on submit and set authStore', () => {
    const mockRes = {
      accessToken: 'token-abc',
      refreshToken: 'refresh-xyz',
      expiresInSeconds: 900,
      user: {
        id: 'u-1',
        username: 'admin',
        email: 'admin@omega.local',
        role: 'Admin',
        allocatedQuotaBytes: 100,
        usedQuotaBytes: 0,
        createdAt: ''
      }
    };
    spyOn(apiService, 'login').and.returnValue(of(mockRes));
    spyOn(authStore, 'setLoginResponse');

    component.username = 'admin';
    component.password = 'pass';
    component.onSubmit();

    expect(apiService.login).toHaveBeenCalled();
    expect(authStore.setLoginResponse).toHaveBeenCalledWith(mockRes);
  });
});
