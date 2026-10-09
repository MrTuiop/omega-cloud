import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { SystemHealthComponent } from './system-health.component';
import { ApiService } from '../../../core/services/api.service';
import { SystemHealth } from '../../../core/models/admin.models';

describe('SystemHealthComponent', () => {
  let component: SystemHealthComponent;
  let fixture: ComponentFixture<SystemHealthComponent>;
  let apiService: ApiService;

  const mockHealth: SystemHealth = {
    cpuLoadPercentage: 14.5,
    usedMemoryBytes: 250_000_000,
    totalMemoryBytes: 16_000_000_000,
    memoryUsagePercentage: 15.6,
    storageFreeBytes: 800_000_000_000,
    storageTotalBytes: 1_000_000_000_000,
    storageUsedBytes: 200_000_000_000,
    storageUsagePercentage: 20,
    databaseStatus: 'Healthy',
    databaseLatencyMs: 3,
    totalNodes: 3,
    onlineNodes: 2,
    offlineNodes: 1,
    totalUsers: 10,
    activeSessionsCount: 4,
    serverUptimeSeconds: 90060, // 1d 1h 1m
    osDescription: 'Microsoft Windows 10.0.19045',
    frameworkDescription: '.NET 10.0.0',
    startedAt: '2026-01-01T00:00:00Z'
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SystemHealthComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService
      ]
    }).compileComponents();

    apiService = TestBed.inject(ApiService);
    spyOn(apiService, 'getSystemHealth').and.returnValue(of(mockHealth));

    fixture = TestBed.createComponent(SystemHealthComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    component.ngOnDestroy();
  });

  it('should create system health component', () => {
    expect(component).toBeTruthy();
    expect(component.health()).toEqual(mockHealth);
  });

  it('should format uptime correctly', () => {
    expect(component.formattedUptime()).toBe('1d 1h 1m');
  });

  it('should toggle auto-refresh', () => {
    expect(component.autoRefresh()).toBeTrue();
    component.toggleAutoRefresh();
    expect(component.autoRefresh()).toBeFalse();
    component.toggleAutoRefresh();
    expect(component.autoRefresh()).toBeTrue();
  });

  it('should compute status colors correctly', () => {
    expect(component.getColorForPercent(95)).toBe('var(--omega-danger)');
    expect(component.getColorForPercent(75)).toBe('var(--omega-warning)');
    expect(component.getColorForPercent(20)).toBe('var(--omega-accent)');
  });
});
