import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { FileDetailsSheetComponent } from './file-details-sheet.component';
import { ApiService } from '../../../core/services/api.service';
import { FileItem, FileMetadata } from '../../../core/models/file.models';

describe('FileDetailsSheetComponent', () => {
  let component: FileDetailsSheetComponent;
  let fixture: ComponentFixture<FileDetailsSheetComponent>;
  let apiService: ApiService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FileDetailsSheetComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(FileDetailsSheetComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);
  });

  it('should not render anything when file is null', () => {
    component.file = null;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.sheet-container')).toBeNull();
  });

  it('should render file details and load metadata', () => {
    const mockFile: FileItem = {
      id: 'file-10',
      userId: 'user-1',
      storageBlobId: 'blob-1',
      name: 'document.pdf',
      extension: 'pdf',
      mimeType: 'application/pdf',
      sizeBytes: 2048,
      isDeleted: false,
      isFavorite: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };

    const mockMeta: FileMetadata = {
      ...mockFile,
      folderName: 'Root',
      sha256Hash: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
      storageNodeId: 'node-1',
      storageNodeName: 'Local-NVMe',
      storageNodeType: 'LocalDisk'
    };

    spyOn(apiService, 'getFileMetadata').and.returnValue(of(mockMeta));

    component.file = mockFile;
    component.loadMetadata(mockFile.id);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.sheet-title').textContent).toContain('document.pdf');
    expect(fixture.nativeElement.querySelector('.hash-text').textContent).toContain('e3b0c442');
    expect(fixture.nativeElement.querySelector('.node-badge').textContent).toContain('Local-NVMe');
  });
});
