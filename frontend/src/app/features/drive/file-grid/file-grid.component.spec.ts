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
});
