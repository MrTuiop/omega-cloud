import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProgressRingComponent } from './progress-ring.component';

describe('ProgressRingComponent', () => {
  let component: ProgressRingComponent;
  let fixture: ComponentFixture<ProgressRingComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProgressRingComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ProgressRingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and calculate dimensions', () => {
    component.size = 50;
    component.strokeWidth = 4;
    expect(component.center).toBe(25);
    expect(component.radius).toBe(23);
    expect(component.circumference).toBeCloseTo(2 * Math.PI * 23, 2);
  });

  it('should calculate dashOffset proportionally', () => {
    component.size = 100;
    component.strokeWidth = 10;
    component.progress = 50;

    const c = component.circumference;
    expect(component.dashOffset).toBeCloseTo(c * 0.5, 2);
  });

  it('should display text when showText is true', () => {
    component.showText = true;
    component.progress = 75;
    fixture.detectChanges();

    const text = fixture.nativeElement.querySelector('.progress-ring-text');
    expect(text).toBeTruthy();
    expect(text.textContent).toContain('75%');
  });
});
