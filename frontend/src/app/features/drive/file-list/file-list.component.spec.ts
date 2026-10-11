import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FileListComponent } from './file-list.component';
import { FileItem, FolderItem } from '../../../core/models/file.models';

describe('FileListComponent', () => {
  let component: FileListComponent;
  let fixture: ComponentFixture<FileListComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FileListComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(FileListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should render table rows for folders and files', () => {
    component.folders = [
      { id: 'f-1', userId: 'u-1', name: 'Work', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' }
    ];
    component.files = [
      {
        id: 'file-1',
        userId: 'u-1',
        storageBlobId: 'blob-1',
        name: 'notes.txt',
        extension: 'txt',
        mimeType: 'text/plain',
        sizeBytes: 200,
        isDeleted: false,
        isFavorite: false,
        isShared: false,
        createdAt: '',
        updatedAt: ''
      }
    ];
    fixture.detectChanges();

    const folderRows = fixture.nativeElement.querySelectorAll('.folder-row');
    const fileRows = fixture.nativeElement.querySelectorAll('.file-row');
    expect(folderRows.length).toBe(1);
    expect(fileRows.length).toBe(1);
  });

  it('should sort files by name asc and desc', () => {
    component.files = [
      { id: '1', userId: 'u', storageBlobId: 'b', name: 'Zebra.txt', extension: 'txt', mimeType: '', sizeBytes: 10, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' },
      { id: '2', userId: 'u', storageBlobId: 'b', name: 'Alpha.txt', extension: 'txt', mimeType: '', sizeBytes: 20, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' }
    ];

    expect(component.sortedFiles[0].name).toBe('Alpha.txt');
    expect(component.sortedFiles[1].name).toBe('Zebra.txt');

    component.setSort('name'); // switches to desc
    expect(component.sortedFiles[0].name).toBe('Zebra.txt');
    expect(component.sortedFiles[1].name).toBe('Alpha.txt');
  });

  it('should support Ctrl/Cmd click multi-selection on table rows and emit selectionChange', () => {
    component.files = [
      { id: 'file-1', userId: 'u', storageBlobId: 'b-1', name: 'f1.txt', extension: 'txt', mimeType: '', sizeBytes: 10, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' },
      { id: 'file-2', userId: 'u', storageBlobId: 'b-2', name: 'f2.txt', extension: 'txt', mimeType: '', sizeBytes: 20, isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: '' }
    ];
    fixture.detectChanges();

    spyOn(component.selectionChange, 'emit');
    const rows = fixture.nativeElement.querySelectorAll('.file-row');

    rows[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(component.selectedFileIds).toEqual(['file-1']);

    rows[1].dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true }));
    expect(component.selectedFileIds).toEqual(['file-1', 'file-2']);
    expect(component.selectionChange.emit).toHaveBeenCalledWith({
      folderIds: [],
      fileIds: ['file-1', 'file-2']
    });
  });

  it('should emit folderDrop when dropping onto folder row in table', () => {
    const targetFolder: FolderItem = { id: 'f-dest', userId: 'u', name: 'Dest', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    component.folders = [targetFolder];
    fixture.detectChanges();

    spyOn(component.folderDrop, 'emit');
    const folderRow = fixture.nativeElement.querySelector('.folder-row');

    const dataTransfer = new DataTransfer();
    dataTransfer.setData('application/json', JSON.stringify({ fileIds: ['file-moved'], folderIds: [] }));

    const dropEvent = new DragEvent('drop', {
      bubbles: true,
      cancelable: true,
      dataTransfer
    });
    folderRow.dispatchEvent(dropEvent);

    expect(component.folderDrop.emit).toHaveBeenCalledWith({
      targetFolder,
      fileId: 'file-moved',
      fileIds: ['file-moved'],
      folderIds: []
    });
  });

  it('should emit workspaceContextMenu when right-clicking table container', () => {
    spyOn(component.workspaceContextMenu, 'emit');
    const container = fixture.nativeElement.querySelector('.list-container');
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    container.dispatchEvent(event);

    expect(component.workspaceContextMenu.emit).toHaveBeenCalled();
  });

  it('should emit openFile and NOT emit downloadFile on double clicking file row', () => {
    const mockFile: FileItem = {
      id: 'file-1', userId: 'u-1', storageBlobId: 'b-1', name: 'report.pdf',
      extension: 'pdf', mimeType: 'application/pdf', sizeBytes: 100,
      isDeleted: false, isFavorite: false, isShared: false, createdAt: '', updatedAt: ''
    };
    component.files = [mockFile];
    fixture.detectChanges();

    spyOn(component.openFile, 'emit');
    spyOn(component.downloadFile, 'emit');
    const row = fixture.nativeElement.querySelector('.file-row');
    row.dispatchEvent(new MouseEvent('dblclick'));

    expect(component.openFile.emit).toHaveBeenCalledWith(mockFile);
    expect(component.downloadFile.emit).not.toHaveBeenCalled();
  });

  it('should NOT allow dragover on self when folder is selected in table', () => {
    const folder: FolderItem = { id: 'f-1', userId: 'u', name: 'F1', color: null, isDeleted: false, isShared: false, createdAt: '', updatedAt: '' };
    component.folders = [folder];
    component.selectedFolderIds = ['f-1'];
    fixture.detectChanges();

    const folderRow = fixture.nativeElement.querySelector('.folder-row');
    const dragEvent = new DragEvent('dragover', { bubbles: true, cancelable: true });
    folderRow.dispatchEvent(dragEvent);

    expect(component.dragOverFolderId).toBeNull();
  });
});

