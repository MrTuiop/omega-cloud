import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlassButtonComponent } from './glass-button.component';

describe('GlassButtonComponent', () => {
  let component: GlassButtonComponent;
  let fixture: ComponentFixture<GlassButtonComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GlassButtonComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(GlassButtonComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should emit click event when enabled', () => {
    spyOn(component.btnClick, 'emit');
    const button = fixture.nativeElement.querySelector('button');
    button.click();
    expect(component.btnClick.emit).toHaveBeenCalled();
  });

  it('should not emit click event when disabled', () => {
    component.disabled = true;
    fixture.detectChanges();
    spyOn(component.btnClick, 'emit');
    const button = fixture.nativeElement.querySelector('button');
    button.click();
    expect(component.btnClick.emit).not.toHaveBeenCalled();
  });

  it('should show spinner when loading', () => {
    component.loading = true;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.spinner')).toBeTruthy();
  });
});
