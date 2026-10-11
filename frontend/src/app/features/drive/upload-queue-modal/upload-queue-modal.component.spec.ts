import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { UploadQueueModalComponent } from './upload-queue-modal.component';
import { ApiService } from '../../../core/services/api.service';
import { I18nService } from '../../../core/services/i18n.service';

describe('UploadQueueModalComponent', () => {
  let component: UploadQueueModalComponent;
  let fixture: ComponentFixture<UploadQueueModalComponent>;
  let apiService: ApiService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UploadQueueModalComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService,
        I18nService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(UploadQueueModalComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);
  });

  it('should create modal', () => {
    expect(component).toBeTruthy();
  });

  it('should calculate total size and allow filtering', () => {
    const file1 = new File(['hello'], 'alpha.txt', { type: 'text/plain' });
    const file2 = new File(['world!'], 'beta.doc', { type: 'text/plain' });
    component.inputFiles = [file1, file2];

    expect(component.items().length).toBe(2);
    expect(component.totalSizeBytes()).toBe(file1.size + file2.size);

    component.filterQuery = 'alpha';
    expect(component.filteredItems().length).toBe(1);
    expect(component.filteredItems()[0].name).toBe('alpha.txt');
  });

  it('should detect conflicts and switch to conflicts step', () => {
    const file1 = new File(['data'], 'dup.txt');
    component.inputFiles = [file1];

    spyOn(apiService, 'checkConflicts').and.returnValue(of(['dup.txt']));

    component.proceedToUpload();

    expect(apiService.checkConflicts).toHaveBeenCalled();
    expect(component.step()).toBe('conflicts');
    expect(component.conflictNames()).toEqual(['dup.txt']);
    expect(component.resolutions['dup.txt']).toBe('Rename');
  });
});
