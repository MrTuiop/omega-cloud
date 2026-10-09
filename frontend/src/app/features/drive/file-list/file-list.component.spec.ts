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
      { id: 'f-1', userId: 'u-1', name: 'Work', color: null, isDeleted: false, createdAt: '', updatedAt: '' }
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
      { id: '1', userId: 'u', storageBlobId: 'b', name: 'Zebra.txt', extension: 'txt', mimeType: '', sizeBytes: 10, isDeleted: false, isFavorite: false, createdAt: '', updatedAt: '' },
      { id: '2', userId: 'u', storageBlobId: 'b', name: 'Alpha.txt', extension: 'txt', mimeType: '', sizeBytes: 20, isDeleted: false, isFavorite: false, createdAt: '', updatedAt: '' }
    ];

    expect(component.sortedFiles[0].name).toBe('Alpha.txt');
    expect(component.sortedFiles[1].name).toBe('Zebra.txt');

    component.setSort('name'); // switches to desc
    expect(component.sortedFiles[0].name).toBe('Zebra.txt');
    expect(component.sortedFiles[1].name).toBe('Alpha.txt');
  });
});
