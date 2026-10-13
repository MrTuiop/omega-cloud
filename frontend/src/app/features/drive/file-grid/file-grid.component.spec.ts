import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FileGridComponent } from './file-grid.component';
import { FileItem, FolderItem } from '../../../core/models/file.models';

describe('FileGridComponent', () => {
  let component: FileGridComponent;
  let fixture: ComponentFixture<FileGridComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FileGridComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(FileGridComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should show empty state when folders and files are empty', () => {
    component.folders = [];
    component.files = [];
    fixture.detectChanges();

    const empty = fixture.nativeElement.querySelector('.empty-state');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('This folder is empty');
  });

  it('should render folders and emit openFolder on double click', () => {
    const mockFolder: FolderItem = {
      id: 'f-1',
      userId: 'u-1',
      name: 'Documents',
      color: '#0071e3',
      isDeleted: false,
      isShared: false,
      createdAt: '',
      updatedAt: new Date().toISOString()
    };
    component.folders = [mockFolder];
    fixture.detectChanges();

    spyOn(component.openFolder, 'emit');
    const card = fixture.nativeElement.querySelector('.folder-card');
    card.dispatchEvent(new MouseEvent('dblclick'));

    expect(component.openFolder.emit).toHaveBeenCalledWith(mockFolder);
  });

  it('should render files and emit selectFile on click', () => {
    const mockFile: FileItem = {
      id: 'file-1',
      userId: 'u-1',
      storageBlobId: 'blob-1',
      name: 'report.pdf',
      extension: 'pdf',
      mimeType: 'application/pdf',
      sizeBytes: 1048576,
      isDeleted: false,
      isFavorite: false,
      isShared: false,
      createdAt: '',
      updatedAt: new Date().toISOString()
    };
    component.files = [mockFile];
    fixture.detectChanges();

    spyOn(component.selectFile, 'emit');
    const card = fixture.nativeElement.querySelector('.file-card');
    card.click();

    expect(component.selectFile.emit).toHaveBeenCalledWith(mockFile);
  });

  it('should support multi-selection with Ctrl/Cmd click and emit selectionChange', () => {
    const file1: FileItem = {
      id: 'file-1', userId: 'u-1', storageBlobId: 'b-1', name: 'f1.txt', extension: 'txt',
      mimeType: 'text/plain', sizeBytes: 10, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: ''
    };
    const file2: FileItem = {
      id: 'file-2', userId: 'u-1', storageBlobId: 'b-2', name: 'f2.txt', extension: 'txt',
      mimeType: 'text/plain', sizeBytes: 20, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: ''
    };
    component.files = [file1, file2];
    fixture.detectChanges();

    spyOn(component.selectionChange, 'emit');

    // Click file 1 normally
    const cards = fixture.nativeElement.querySelectorAll('.file-card');
    cards[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(component.selectedFileIds).toEqual(['file-1']);

    // Ctrl+click file 2
    cards[1].dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true }));
    expect(component.selectedFileIds).toEqual(['file-1', 'file-2']);
    expect(component.selectionChange.emit).toHaveBeenCalledWith({
      folderIds: [],
      fileIds: ['file-1', 'file-2']
    });

    // Ctrl+click file 1 again to toggle off
    cards[0].dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true }));
    expect(component.selectedFileIds).toEqual(['file-2']);
  });

  it('should support Shift+click range selection', () => {
    const folder1: FolderItem = { id: 'f-1', userId: 'u', name: 'Folder 1', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    const file1: FileItem = { id: 'file-1', userId: 'u', storageBlobId: 'b', name: 'f1.txt', extension: 'txt', mimeType: '', sizeBytes: 1, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' };
    const file2: FileItem = { id: 'file-2', userId: 'u', storageBlobId: 'b', name: 'f2.txt', extension: 'txt', mimeType: '', sizeBytes: 2, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' };

    component.folders = [folder1];
    component.files = [file1, file2];
    fixture.detectChanges();

    // Click folder1 (index 0)
    const folderCard = fixture.nativeElement.querySelector('.folder-card');
    folderCard.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    // Shift+click file2 (index 2)
    const fileCards = fixture.nativeElement.querySelectorAll('.file-card');
    fileCards[1].dispatchEvent(new MouseEvent('click', { bubbles: true, shiftKey: true }));

    expect(component.selectedFolderIds).toContain('f-1');
    expect(component.selectedFileIds).toContain('file-1');
    expect(component.selectedFileIds).toContain('file-2');
  });

  it('should emit workspaceContextMenu when right-clicking grid background', () => {
    spyOn(component.workspaceContextMenu, 'emit');
    const container = fixture.nativeElement.querySelector('.grid-container');
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    container.dispatchEvent(event);

    expect(component.workspaceContextMenu.emit).toHaveBeenCalled();
  });

  it('should emit folderDrop on dropping item onto folder card', () => {
    const targetFolder: FolderItem = { id: 'target-f', userId: 'u', name: 'Target', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    component.folders = [targetFolder];
    fixture.detectChanges();

    spyOn(component.folderDrop, 'emit');
    const folderCard = fixture.nativeElement.querySelector('.folder-card');

    const dataTransfer = new DataTransfer();
    dataTransfer.setData('application/json', JSON.stringify({ fileIds: ['dragged-file-1'], folderIds: [] }));

    const dropEvent = new DragEvent('drop', {
      bubbles: true,
      cancelable: true,
      dataTransfer
    });
    folderCard.dispatchEvent(dropEvent);

    expect(component.folderDrop.emit).toHaveBeenCalledWith({
      targetFolder,
      fileId: 'dragged-file-1',
      fileIds: ['dragged-file-1'],
      folderIds: []
    });
  });

  it('should emit openFile and NOT emit downloadFile on double clicking file card', () => {
    const mockFile: FileItem = {
      id: 'file-1', userId: 'u-1', storageBlobId: 'b-1', name: 'report.pdf',
      extension: 'pdf', mimeType: 'application/pdf', sizeBytes: 100,
      isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: ''
    };
    component.files = [mockFile];
    fixture.detectChanges();

    spyOn(component.openFile, 'emit');
    spyOn(component.downloadFile, 'emit');
    const card = fixture.nativeElement.querySelector('.file-card');
    card.dispatchEvent(new MouseEvent('dblclick'));

    expect(component.openFile.emit).toHaveBeenCalledWith(mockFile);
    expect(component.downloadFile.emit).not.toHaveBeenCalled();
  });

  it('should toggle selection on checkbox click without deselecting other items', () => {
    const f1: FolderItem = { id: 'f-1', userId: 'u', name: 'Folder 1', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    const file1: FileItem = { id: 'file-1', userId: 'u', storageBlobId: 'b-1', name: 'f1.txt', extension: 'txt', mimeType: 'text/plain', sizeBytes: 10, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' };
    component.folders = [f1];
    component.files = [file1];
    component.selectedFolderIds = ['f-1'];
    fixture.detectChanges();

    spyOn(component.selectionChange, 'emit');

    const fileCheckbox = fixture.nativeElement.querySelector('.file-card .item-checkbox');
    fileCheckbox.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(component.selectedFolderIds).toEqual(['f-1']);
    expect(component.selectedFileIds).toEqual(['file-1']);
    expect(component.selectionChange.emit).toHaveBeenCalledWith({
      folderIds: ['f-1'],
      fileIds: ['file-1']
    });

    // Click again to unselect file1
    fileCheckbox.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(component.selectedFolderIds).toEqual(['f-1']);
    expect(component.selectedFileIds).toEqual([]);
  });

  it('should use DragStateService during drag start and drop', () => {
    const targetFolder: FolderItem = { id: 'f-2', userId: 'u', name: 'Folder 2', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    const file1: FileItem = { id: 'file-1', userId: 'u', storageBlobId: 'b-1', name: 'f1.txt', extension: 'txt', mimeType: 'text/plain', sizeBytes: 10, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' };
    component.folders = [targetFolder];
    component.files = [file1];
    fixture.detectChanges();

    spyOn(component.folderDrop, 'emit');

    const fileCard = fixture.nativeElement.querySelector('.file-card');
    const dragStartEvent = new DragEvent('dragstart', { bubbles: true, cancelable: true });
    fileCard.dispatchEvent(dragStartEvent);

    expect(component.dragState.isDragging()).toBeTrue();
    expect(component.dragState.getDragData()?.fileIds).toEqual(['file-1']);

    const folderCard = fixture.nativeElement.querySelector('.folder-card');
    const dropEvent = new DragEvent('drop', { bubbles: true, cancelable: true });
    folderCard.dispatchEvent(dropEvent);

    expect(component.folderDrop.emit).toHaveBeenCalledWith({
      targetFolder,
      fileId: 'file-1',
      fileIds: ['file-1'],
      folderIds: []
    });
    expect(component.dragState.isDragging()).toBeFalse();
  });

  it('should NOT allow dragover on self when folder is selected', () => {
    const folder: FolderItem = { id: 'f-1', userId: 'u', name: 'F1', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    component.folders = [folder];
    component.selectedFolderIds = ['f-1'];
    fixture.detectChanges();

    const folderCard = fixture.nativeElement.querySelector('.folder-card');
    const dragEvent = new DragEvent('dragover', { bubbles: true, cancelable: true });
    folderCard.dispatchEvent(dragEvent);

    expect(component.dragOverFolderId).toBeNull();
  });

  it('should attach listeners on mousedown and clean up on mouseup', () => {
    spyOn(document, 'addEventListener').and.callThrough();
    spyOn(document, 'removeEventListener').and.callThrough();
    spyOn(component.selectionChange, 'emit');

    const container = fixture.nativeElement.querySelector('.grid-container');
    const mouseDownEvent = new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0 });
    container.dispatchEvent(mouseDownEvent);

    expect(component.isMarqueeActive).toBeTrue();
    expect(document.addEventListener).toHaveBeenCalledWith('mousemove', jasmine.any(Function), jasmine.any(Object));
    expect(document.addEventListener).toHaveBeenCalledWith('mouseup', jasmine.any(Function));

    const mouseUpEvent = new MouseEvent('mouseup', { bubbles: true, cancelable: true });
    document.dispatchEvent(mouseUpEvent);

    expect(component.isMarqueeActive).toBeFalse();
    expect(document.removeEventListener).toHaveBeenCalledWith('mousemove', jasmine.any(Function));
    expect(document.removeEventListener).toHaveBeenCalledWith('mouseup', jasmine.any(Function));
    expect(component.selectionChange.emit).toHaveBeenCalled();
  });

  it('should clean up marquee listeners on component destroy', () => {
    spyOn(document, 'removeEventListener').and.callThrough();

    const container = fixture.nativeElement.querySelector('.grid-container');
    const mouseDownEvent = new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0 });
    container.dispatchEvent(mouseDownEvent);

    expect(component.isMarqueeActive).toBeTrue();
    fixture.destroy();

    expect(document.removeEventListener).toHaveBeenCalledWith('mousemove', jasmine.any(Function));
    expect(document.removeEventListener).toHaveBeenCalledWith('mouseup', jasmine.any(Function));
  });
});

