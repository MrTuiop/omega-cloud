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
      folders: [{ id: 'f-1', userId: 'u-1', name: 'Folder 1', color: '#0071e3', isDeleted: false, createdAt: '', updatedAt: '' }],
      files: [{ id: 'file-1', userId: 'u-1', storageBlobId: 'b-1', name: 'sample.png', extension: 'png', mimeType: 'image/png', sizeBytes: 100, isDeleted: false, isFavorite: true, createdAt: '', updatedAt: '' }],
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

  it('should switch between Drive tabs', () => {
    fixture.detectChanges();
    component.selectTab('favorites');
    expect(component.currentTab()).toBe('favorites');

    component.selectTab('trash');
    expect(component.currentTab()).toBe('trash');
  });
});
