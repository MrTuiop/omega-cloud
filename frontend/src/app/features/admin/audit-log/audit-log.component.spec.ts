import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AuditLogComponent } from './audit-log.component';
import { ApiService } from '../../../core/services/api.service';
import { AuditLog } from '../../../core/models/admin.models';

describe('AuditLogComponent', () => {
  let component: AuditLogComponent;
  let fixture: ComponentFixture<AuditLogComponent>;
  let apiService: ApiService;

  const mockLogs: AuditLog[] = [
    {
      id: 1,
      userId: 'u-1',
      userName: 'alice',
      action: 'UserLogin',
      resource: 'Auth',
      resourceId: 'u-1',
      ipAddress: '192.168.1.10',
      detailsJson: '{"browser":"Chrome"}',
      timestamp: '2026-01-01T10:00:00Z'
    },
    {
      id: 2,
      userId: 'u-1',
      userName: 'alice',
      action: 'UserUpdated',
      resource: 'Users',
      resourceId: 'u-2',
      ipAddress: '192.168.1.10',
      detailsJson: '{"role":"Admin"}',
      timestamp: '2026-01-01T10:05:00Z'
    },
    {
      id: 3,
      userId: 'u-1',
      userName: 'alice',
      action: 'NodePinged',
      resource: 'StorageNodes',
      resourceId: 'node-1',
      ipAddress: '192.168.1.10',
      detailsJson: '{"latencyMs":15}',
      timestamp: '2026-01-01T10:10:00Z'
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AuditLogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService
      ]
    }).compileComponents();

    apiService = TestBed.inject(ApiService);
    spyOn(apiService, 'getAuditLogs').and.returnValue(of(mockLogs));

    fixture = TestBed.createComponent(AuditLogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create audit log component', () => {
    expect(component).toBeTruthy();
    expect(component.logs().length).toBe(3);
  });

  it('should compute event categories correctly', () => {
    expect(component.authEventsCount()).toBe(1);
    expect(component.adminActionsCount()).toBe(1);
    expect(component.nodeEventsCount()).toBe(1);
  });

  it('should trigger search and action filter changes', () => {
    (apiService.getAuditLogs as jasmine.Spy).calls.reset();

    component.onActionChange('UserLogin');
    expect(apiService.getAuditLogs).toHaveBeenCalledWith({
      action: 'UserLogin',
      search: undefined,
      limit: 100
    });

    component.onSearchChange('192.168.1.10');
    expect(apiService.getAuditLogs).toHaveBeenCalledWith({
      action: 'UserLogin',
      search: '192.168.1.10',
      limit: 100
    });
  });

  it('should format details preview and JSON correctly', () => {
    const preview = component.formatDetailsPreview('{"key":"value"}');
    expect(preview).toBe('key: value');

    const formatted = component.formatJson('{"key":"value"}');
    expect(formatted).toContain('"key": "value"');
  });

  it('should open inspect modal with selected log', () => {
    component.openInspectModal(mockLogs[0]);
    expect(component.selectedLog()).toEqual(mockLogs[0]);
    expect(component.inspectModalOpen()).toBeTrue();
  });
});
