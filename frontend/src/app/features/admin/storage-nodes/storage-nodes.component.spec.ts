import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject } from 'rxjs';
import { StorageNodesComponent } from './storage-nodes.component';
import { ApiService } from '../../../core/services/api.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { StorageNode, NodeRegistrationToken, NodePingResult } from '../../../core/models/admin.models';
import { NodeStatusChangedEvent } from '../../../core/models/realtime.models';

describe('StorageNodesComponent', () => {
  let component: StorageNodesComponent;
  let fixture: ComponentFixture<StorageNodesComponent>;
  let apiService: ApiService;
  let signalRService: SignalRService;
  let nodeStatusSubject: Subject<NodeStatusChangedEvent>;

  const mockNodes: StorageNode[] = [
    {
      id: 'node-1',
      name: 'Master-Local',
      type: 'LocalDisk',
      baseUrl: null,
      isBehindNat: false,
      status: 'Online',
      totalSpaceBytes: 1_000_000,
      availableSpaceBytes: 800_000,
      usedSpaceBytes: 200_000,
      freeSpacePercentage: 80,
      usedSpacePercentage: 20,
      cpuLoad: 12.5,
      diskStatus: 'Healthy',
      latencyMs: 1,
      lastHeartbeatAt: '2026-01-01T00:00:00Z',
      secondsSinceLastHeartbeat: 5,
      isOnline: true,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z'
    },
    {
      id: 'node-2',
      name: 'Home-NAS',
      type: 'RemoteDaemon',
      baseUrl: 'https://nas.local:5001',
      isBehindNat: true,
      status: 'Offline',
      totalSpaceBytes: 2_000_000,
      availableSpaceBytes: 1_000_000,
      usedSpaceBytes: 1_000_000,
      freeSpacePercentage: 50,
      usedSpacePercentage: 50,
      cpuLoad: null,
      diskStatus: 'Degraded',
      latencyMs: null,
      lastHeartbeatAt: '2026-01-01T00:00:00Z',
      secondsSinceLastHeartbeat: 120,
      isOnline: false,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z'
    }
  ];

  beforeEach(async () => {
    nodeStatusSubject = new Subject<NodeStatusChangedEvent>();

    await TestBed.configureTestingModule({
      imports: [StorageNodesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService,
        {
          provide: SignalRService,
          useValue: {
            nodeStatusChanged$: nodeStatusSubject.asObservable()
          }
        }
      ]
    }).compileComponents();

    apiService = TestBed.inject(ApiService);
    signalRService = TestBed.inject(SignalRService);
    spyOn(apiService, 'getAdminNodes').and.returnValue(of(mockNodes));

    fixture = TestBed.createComponent(StorageNodesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create storage nodes component', () => {
    expect(component).toBeTruthy();
    expect(component.nodes().length).toBe(2);
  });

  it('should compute fleet metrics correctly', () => {
    expect(component.onlineNodesCount()).toBe(1);
    expect(component.offlineNodesCount()).toBe(1);
    expect(component.remoteNodesCount()).toBe(1);
    expect(component.localNodesCount()).toBe(1);
    expect(component.totalFleetCapacity()).toBe(3_000_000);
    expect(component.totalFleetAvailable()).toBe(1_800_000);
    expect(component.totalFleetUsed()).toBe(1_200_000);
    expect(component.fleetUsagePercentage()).toBe(40);
  });

  it('should ping node and update status', () => {
    const pingResult: NodePingResult = {
      nodeId: 'node-2',
      isReachable: true,
      latencyMs: 18,
      status: 'Online'
    };
    spyOn(apiService, 'pingAdminNode').and.returnValue(of(pingResult));

    component.pingNode(mockNodes[1]);

    expect(apiService.pingAdminNode).toHaveBeenCalledWith('node-2');
    expect(component.pingResults()['node-2']).toEqual(pingResult);
    expect(component.nodes().find(n => n.id === 'node-2')?.status).toBe('Online');
  });

  it('should update node status via SignalR in real time', () => {
    nodeStatusSubject.next({
      nodeId: 'node-2',
      status: 'Online',
      lastSeen: '2026-01-01T12:00:00Z'
    });

    const updatedNode = component.nodes().find(n => n.id === 'node-2');
    expect(updatedNode?.status).toBe('Online');
    expect(updatedNode?.isOnline).toBeTrue();
    expect(updatedNode?.lastHeartbeatAt).toBe('2026-01-01T12:00:00Z');
  });

  it('should generate node token', () => {
    const mockToken: NodeRegistrationToken = {
      nodeId: 'node-3',
      name: 'Backup-Server',
      registrationKey: 'key_sec_12345',
      createdAt: '2026-01-01T00:00:00Z'
    };
    spyOn(apiService, 'generateNodeToken').and.returnValue(of(mockToken));

    component.openGenerateTokenModal();
    component.newNodeName = 'Backup-Server';
    component.submitGenerateToken();

    expect(apiService.generateNodeToken).toHaveBeenCalledWith({
      name: 'Backup-Server',
      type: 'RemoteDaemon',
      description: undefined
    });
    expect(component.generatedToken()).toEqual(mockToken);
  });

  it('should edit storage node', () => {
    const updated: StorageNode = {
      ...mockNodes[1],
      name: 'Home-NAS-Updated',
      status: 'Online'
    };
    spyOn(apiService, 'updateAdminNode').and.returnValue(of(updated));

    component.openEditNodeModal(mockNodes[1]);
    component.editNodeName = 'Home-NAS-Updated';
    component.submitEditNode();

    expect(apiService.updateAdminNode).toHaveBeenCalledWith('node-2', {
      name: 'Home-NAS-Updated',
      baseUrl: 'https://nas.local:5001',
      isBehindNat: true,
      status: 'Offline'
    });
    expect(component.editNodeModalOpen()).toBeFalse();
  });

  it('should delete storage node', () => {
    spyOn(apiService, 'deleteAdminNode').and.returnValue(of(void 0));

    component.openDeleteNodeModal(mockNodes[1]);
    component.submitDeleteNode();

    expect(apiService.deleteAdminNode).toHaveBeenCalledWith('node-2');
    expect(component.deleteNodeModalOpen()).toBeFalse();
  });
});
