import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { TrashBinComponent } from './trash-bin.component';
import { ApiService } from '../../../core/services/api.service';

describe('TrashBinComponent', () => {
  let component: TrashBinComponent;
  let fixture: ComponentFixture<TrashBinComponent>;
  let apiService: ApiService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TrashBinComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(TrashBinComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);

    spyOn(apiService, 'getTrash').and.returnValue(of({
      folders: [{ id: 'tf-1', userId: 'u-1', name: 'Old Folder', color: null, deletedAt: new Date().toISOString(), createdAt: '', updatedAt: '' }],
      files: [{ id: 'tfile-1', userId: 'u-1', storageBlobId: 'b-1', name: 'old.pdf', extension: 'pdf', mimeType: 'application/pdf', sizeBytes: 500, isFavorite: false, deletedAt: new Date().toISOString(), createdAt: '', updatedAt: '' }],
      totalTrashSizeBytes: 500,
      totalItemCount: 2,
      quota: { allocatedBytes: 1000, usedBytes: 500, availableBytes: 500 }
    }));
  });

  it('should create and load trash contents', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(component.content()?.totalItemCount).toBe(2);
    expect(component.content()?.folders.length).toBe(1);
    expect(component.content()?.files.length).toBe(1);
  });

  it('should call restoreTrashItem on restore click', () => {
    fixture.detectChanges();
    spyOn(apiService, 'restoreTrashItem').and.returnValue(of({ id: 'tfile-1', itemType: 'file', name: 'old.pdf', message: 'Restored' }));

    component.restoreItem('tfile-1');
    expect(apiService.restoreTrashItem).toHaveBeenCalledWith('tfile-1');
  });

  it('should call permanentlyDeleteTrashItem on delete click', () => {
    fixture.detectChanges();
    spyOn(apiService, 'permanentlyDeleteTrashItem').and.returnValue(of(undefined));

    component.deletePermanent('tfile-1');
    expect(apiService.permanentlyDeleteTrashItem).toHaveBeenCalledWith('tfile-1');
  });
});
