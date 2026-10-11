import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastContainerComponent } from './toast-container.component';
import { ToastService } from '../../../core/services/toast.service';

describe('ToastContainerComponent', () => {
  let component: ToastContainerComponent;
  let fixture: ComponentFixture<ToastContainerComponent>;
  let toastService: ToastService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ToastContainerComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ToastContainerComponent);
    component = fixture.componentInstance;
    toastService = TestBed.inject(ToastService);
    toastService.clearAll();
    fixture.detectChanges();
  });

  afterEach(() => {
    toastService.clearAll();
  });

  it('should not render anything when there are no toasts', () => {
    expect(fixture.nativeElement.querySelector('.toast-stack')).toBeNull();
  });

  it('should render toast cards when toasts are added', () => {
    toastService.success('File uploaded successfully');
    fixture.detectChanges();

    const stack = fixture.nativeElement.querySelector('.toast-stack');
    expect(stack).toBeTruthy();
    expect(fixture.nativeElement.querySelectorAll('.toast-card').length).toBe(1);
    expect(fixture.nativeElement.querySelector('.toast-message').textContent).toContain('File uploaded successfully');
  });

  it('should show "Очистить все" button when more than 1 toast is present and clear all on click', () => {
    toastService.info('Notice 1');
    toastService.warning('Notice 2');
    fixture.detectChanges();

    const clearAllBtn = fixture.nativeElement.querySelector('.clear-all-btn');
    expect(clearAllBtn).toBeTruthy();
    expect(clearAllBtn.textContent).toContain('Очистить все');

    spyOn(toastService, 'clearAll').and.callThrough();
    clearAllBtn.click();
    fixture.detectChanges();

    expect(toastService.clearAll).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.toast-stack')).toBeNull();
  });

  it('should dismiss individual toast on close button click', () => {
    toastService.error('Some error');
    fixture.detectChanges();

    const closeBtn = fixture.nativeElement.querySelector('.toast-close');
    closeBtn.click();
    fixture.detectChanges();

    expect(toastService.toasts().length).toBe(0);
  });
});
