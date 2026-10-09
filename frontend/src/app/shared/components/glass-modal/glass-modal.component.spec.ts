import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlassModalComponent } from './glass-modal.component';

describe('GlassModalComponent', () => {
  let component: GlassModalComponent;
  let fixture: ComponentFixture<GlassModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GlassModalComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(GlassModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should not render dialog when isOpen is false', () => {
    component.isOpen = false;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.modal-backdrop')).toBeNull();
  });

  it('should render dialog with title when isOpen is true', () => {
    component.isOpen = true;
    component.title = 'Test Title';
    fixture.detectChanges();

    const backdrop = fixture.nativeElement.querySelector('.modal-backdrop');
    expect(backdrop).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.modal-title').textContent).toContain('Test Title');
  });

  it('should emit close on close button click', () => {
    component.isOpen = true;
    fixture.detectChanges();
    spyOn(component.close, 'emit');

    const closeBtn = fixture.nativeElement.querySelector('.modal-close');
    closeBtn.click();
    expect(component.close.emit).toHaveBeenCalled();
  });
});
