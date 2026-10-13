import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SearchScopeModalComponent } from './search-scope-modal.component';
import { ApiService } from '../../../core/services/api.service';
import { I18nService } from '../../../core/services/i18n.service';
import { of } from 'rxjs';
import { FolderItem } from '../../../core/models/file.models';

describe('SearchScopeModalComponent', () => {
  let component: SearchScopeModalComponent;
  let fixture: ComponentFixture<SearchScopeModalComponent>;
  let apiServiceSpy: jasmine.SpyObj<ApiService>;

  const mockPersonalFolder: FolderItem = {
    id: 'p-1',
    userId: 'u-1',
    name: 'Personal Docs',
    color: '#0071e3',
    isDeleted: false,
    isShared: false,
    createdAt: '',
    updatedAt: ''
  };

  const mockSharedFolder: FolderItem = {
    id: 's-1',
    userId: 'u-2',
    name: 'Shared Projects',
    color: '#34c759',
    isDeleted: false,
    isShared: true,
    createdAt: '',
    updatedAt: ''
  };

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['getFolderTree']);
    apiServiceSpy.getFolderTree.and.returnValue(of({
      personalFolders: [mockPersonalFolder],
      sharedFolders: [mockSharedFolder]
    }));

    await TestBed.configureTestingModule({
      imports: [SearchScopeModalComponent],
      providers: [
        { provide: ApiService, useValue: apiServiceSpy },
        I18nService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SearchScopeModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load tree data when opened', () => {
    component.isOpen = true;
    component.ngOnChanges({
      isOpen: {
        currentValue: true,
        previousValue: false,
        firstChange: true,
        isFirstChange: () => true
      }
    });

    expect(apiServiceSpy.getFolderTree).toHaveBeenCalled();
    expect(component.personalTreeNodes.length).toBe(1);
    expect(component.sharedTreeNodes.length).toBe(1);
  });

  it('should change active scope when clicking presets', () => {
    component.selectPreset('shared');
    expect(component.activeScope).toBe('shared');
    expect(component.tempSelectedFolderIds.size).toBe(0);

    component.selectPreset('personal');
    expect(component.activeScope).toBe('personal');
  });

  it('should toggle folder checkboxes and emit custom scope on apply', () => {
    component.isOpen = true;
    component.ngOnChanges({
      isOpen: {
        currentValue: true,
        previousValue: false,
        firstChange: true,
        isFirstChange: () => true
      }
    });

    spyOn(component.applyScope, 'emit');

    const fakeEvent = { target: { checked: true } } as unknown as Event;
    component.onFolderToggle('p-1', fakeEvent);

    expect(component.activeScope).toBe('custom');
    expect(component.isFolderChecked('p-1')).toBeTrue();

    component.onApply();

    expect(component.applyScope.emit).toHaveBeenCalledWith({
      scope: 'custom',
      folderIds: ['p-1'],
      label: '[Personal Docs]'
    });
  });
});
