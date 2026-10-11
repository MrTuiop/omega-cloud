import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DriveLayoutComponent } from './drive-layout.component';
import { ApiService } from '../../../core/services/api.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { SignalRService } from '../../../core/services/signalr.service';
import { ResumableUploaderService } from '../../../core/services/resumable-uploader.service';

describe('DriveLayoutComponent', () => {
  let component: DriveLayoutComponent;
  let fixture: ComponentFixture<DriveLayoutComponent>;
  let apiService: ApiService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DriveLayoutComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ApiService,
        AuthStore,
        SignalRService,
        ResumableUploaderService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DriveLayoutComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);

    spyOn(apiService, 'getRootFolderContent').and.returnValue(of({
      folders: [{ id: 'f-1', userId: 'u-1', name: 'Folder 1', color: '#0071e3', isDeleted: false, isShared: false, createdAt: '', updatedAt: '' }],
      files: [{ id: 'file-1', userId: 'u-1', storageBlobId: 'b-1', name: 'sample.png', extension: 'png', mimeType: 'image/png', sizeBytes: 100, isDeleted: false, isFavorite: true, isShared: false, createdAt: '', updatedAt: '' }],
      breadcrumbs: [],
      quota: { allocatedBytes: 1000, usedBytes: 100, availableBytes: 900 }
    }));
  });

  it('should create and load root folder content', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(component.folderContent()?.folders.length).toBe(1);
    expect(component.folderContent()?.files.length).toBe(1);
  });

  it('should filter items by search query', () => {
    fixture.detectChanges();
    component.searchQuery.set('sample');
    expect(component.filteredFiles().length).toBe(1);
    expect(component.filteredFolders().length).toBe(0);

    component.searchQuery.set('nonexistent');
    expect(component.filteredFiles().length).toBe(0);
  });

  it('should perform server search with scope everywhere and current', () => {
    fixture.detectChanges();
    const searchMock = {
      folders: [{ id: 'f-found', userId: 'u-1', name: 'Found Folder', color: '#0071e3', isDeleted: false, isShared: false, createdAt: '', updatedAt: '' }],
      files: [{ id: 'file-found', userId: 'u-1', storageBlobId: 'b-2', name: 'found.doc', extension: 'doc', mimeType: 'application/msword', sizeBytes: 200, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' }],
      breadcrumbs: [],
      quota: { allocatedBytes: 1000, usedBytes: 300, availableBytes: 700 }
    };
    spyOn(apiService, 'searchFiles').and.returnValue(of(searchMock));

    // Search query change triggers search with scope everywhere
    component.onSearchQueryChange('found');
    expect(apiService.searchFiles).toHaveBeenCalledWith('found', null, 'everywhere');
    expect(component.filteredFolders().length).toBe(1);
    expect(component.filteredFiles().length).toBe(1);

    // Change scope to current
    component.onSearchScopeChange('current');
    expect(apiService.searchFiles).toHaveBeenCalledWith('found', null, 'current');

    // Clear search
    component.clearSearch();
    expect(component.searchQuery()).toBe('');
    expect(component.searchResults()).toBeNull();
  });

  it('should switch between Drive tabs', () => {
    fixture.detectChanges();
    component.selectTab('favorites');
    expect(component.currentTab()).toBe('favorites');

    component.selectTab('trash');
    expect(component.currentTab()).toBe('trash');
  });

  it('should distinguish single click (select only) from double click (open inspector)', () => {
    fixture.detectChanges();
    const mockFile = component.folderContent()!.files[0];

    component.onSelectFile(mockFile);
    expect(component.selectedFileIds()).toEqual(['file-1']);
    expect(component.inspectorFile()).toBeNull();

    component.onOpenFile(mockFile);
    expect(component.inspectorFile()?.id).toBe('file-1');
  });

  it('should navigate folder updating queryParams for browser history', () => {
    fixture.detectChanges();
    const router = (component as any).router;
    spyOn(router, 'navigate');

    component.onNavigateFolder('target-folder-id');
    expect(router.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
      queryParams: { folder: 'target-folder-id' },
      queryParamsHandling: 'merge'
    }));
  });

  it('should perform bulk delete and clear selection', () => {
    fixture.detectChanges();
    component.selectedFolderIds.set(['f-1']);
    component.selectedFileIds.set(['file-1']);
    expect(component.selectedCount()).toBe(2);

    spyOn(apiService, 'bulkDelete').and.returnValue(of(void 0));
    spyOn(component, 'loadCurrentFolder');

    component.applyBulkDelete();

    expect(apiService.bulkDelete).toHaveBeenCalledWith(['file-1'], ['f-1']);
    expect(component.selectedFolderIds().length).toBe(0);
    expect(component.selectedFileIds().length).toBe(0);
    expect(component.bulkDeleteModalOpen()).toBeFalse();
  });

  it('should perform bulk move and clear selection', () => {
    fixture.detectChanges();
    component.selectedFolderIds.set(['f-1']);
    component.selectedFileIds.set(['file-1']);
    component.bulkMoveTargetFolderId = 'dest-folder-id';

    spyOn(apiService, 'bulkMove').and.returnValue(of(void 0));
    spyOn(component, 'loadCurrentFolder');

    component.applyBulkMove();

    expect(apiService.bulkMove).toHaveBeenCalledWith(['file-1'], ['f-1'], 'dest-folder-id');
    expect(component.selectedFolderIds().length).toBe(0);
    expect(component.selectedFileIds().length).toBe(0);
    expect(component.bulkMoveModalOpen()).toBeFalse();
  });

  it('should call apiService.downloadZip on downloadSelectedZip', () => {
    fixture.detectChanges();
    component.selectedFileIds.set(['file-1']);
    const mockBlob = new Blob(['zip content'], { type: 'application/zip' });
    spyOn(apiService, 'downloadZip').and.returnValue(of(mockBlob));
    spyOn(window.URL, 'createObjectURL').and.returnValue('blob:test');
    spyOn(window.URL, 'revokeObjectURL');

    component.downloadSelectedZip();

    expect(apiService.downloadZip).toHaveBeenCalledWith(['file-1'], []);
  });

  it('should switch tab to files on switchFolder when user was on trash or favorites tab', () => {
    fixture.detectChanges();
    component.selectTab('trash');
    expect(component.currentTab()).toBe('trash');

    (component as any).switchFolder('folder-xyz');
    expect(component.currentTab()).toBe('files');
    expect(component.currentFolderId()).toBe('folder-xyz');
  });

  it('should prevent onFolderDrop when target folder is among the dragged folderIds', () => {
    fixture.detectChanges();
    spyOn(apiService, 'bulkMove');
    spyOn((component as any).toastService, 'warning');

    const targetFolder = { id: 'f-1', userId: 'u', name: 'Folder 1', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    component.onFolderDrop({
      targetFolder,
      folderIds: ['f-1'],
      fileIds: []
    });

    expect(apiService.bulkMove).not.toHaveBeenCalled();
    expect((component as any).toastService.warning).toHaveBeenCalledWith('Cannot move a folder into itself');
  });

  it('should prevent applyBulkMove when destination is the current folder', () => {
    fixture.detectChanges();
    component.currentFolderId.set('folder-curr');
    component.bulkMoveTargetFolderId = 'folder-curr';
    spyOn(apiService, 'bulkMove');
    spyOn((component as any).toastService, 'info');

    component.applyBulkMove();

    expect(apiService.bulkMove).not.toHaveBeenCalled();
    expect((component as any).toastService.info).toHaveBeenCalledWith('Items are already in this folder');
  });
});

