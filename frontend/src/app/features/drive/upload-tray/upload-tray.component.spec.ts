import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { UploadTrayComponent } from './upload-tray.component';
import { ResumableUploaderService } from '../../../core/services/resumable-uploader.service';
import { ApiService } from '../../../core/services/api.service';
import { SignalRService } from '../../../core/services/signalr.service';

describe('UploadTrayComponent', () => {
  let component: UploadTrayComponent;
  let fixture: ComponentFixture<UploadTrayComponent>;
  let uploader: ResumableUploaderService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UploadTrayComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ApiService,
        SignalRService,
        ResumableUploaderService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(UploadTrayComponent);
    component = fixture.componentInstance;
    uploader = TestBed.inject(ResumableUploaderService);
  });

  it('should not render anything when tasks list is empty', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.tray-wrapper')).toBeNull();
  });

  it('should render collapsed capsule when tray is closed', () => {
    const file = new File(['test'], 'test.txt');
    uploader.enqueue([file]);
    uploader.setTrayOpen(false);
    fixture.detectChanges();

    const capsule = fixture.nativeElement.querySelector('.capsule-btn');
    expect(capsule).toBeTruthy();
    expect(capsule.textContent).toContain('Uploading 1 item(s)');
  });

  it('should render expanded panel when tray is open', () => {
    const file = new File(['test'], 'test.txt');
    uploader.enqueue([file]);
    uploader.setTrayOpen(true);
    fixture.detectChanges();

    const panel = fixture.nativeElement.querySelector('.expanded-panel');
    expect(panel).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.task-name').textContent).toContain('test.txt');
  });
});
