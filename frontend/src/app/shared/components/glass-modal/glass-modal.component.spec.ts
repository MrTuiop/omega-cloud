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

  it('should emit close when both mousedown and mouseup are on backdrop', () => {
    component.isOpen = true;
    fixture.detectChanges();
    spyOn(component.close, 'emit');

    const backdrop = fixture.nativeElement.querySelector('.modal-backdrop');
    backdrop.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    backdrop.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));

    expect(component.close.emit).toHaveBeenCalledTimes(1);
  });

  it('should NOT emit close when mousedown started inside dialog and mouseup was on backdrop', () => {
    component.isOpen = true;
    fixture.detectChanges();
    spyOn(component.close, 'emit');

    const dialog = fixture.nativeElement.querySelector('.modal-dialog');
    const backdrop = fixture.nativeElement.querySelector('.modal-backdrop');

    // Mousedown on dialog (e.g. text selection)
    dialog.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    // Mouseup released on backdrop
    backdrop.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));

    expect(component.close.emit).not.toHaveBeenCalled();
  });

  it('should NOT emit close when closeOnBackdrop is false', () => {
    component.isOpen = true;
    component.closeOnBackdrop = false;
    fixture.detectChanges();
    spyOn(component.close, 'emit');

    const backdrop = fixture.nativeElement.querySelector('.modal-backdrop');
    backdrop.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    backdrop.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    expect(component.close.emit).not.toHaveBeenCalled();
  });

  it('should NOT emit close when prior mousedown on backdrop was aborted inside dialog, followed by drag-selection from dialog to backdrop', () => {
    component.isOpen = true;
    fixture.detectChanges();
    spyOn(component.close, 'emit');

    const dialog = fixture.nativeElement.querySelector('.modal-dialog');
    const backdrop = fixture.nativeElement.querySelector('.modal-backdrop');

    // Aborted backdrop mousedown: starts on backdrop, released inside dialog
    backdrop.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    dialog.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));

    // Subsequent text selection drag: starts on dialog, released on backdrop
    dialog.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    backdrop.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));

    expect(component.close.emit).not.toHaveBeenCalled();
  });
});
