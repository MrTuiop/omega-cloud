import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlassCardComponent } from './glass-card.component';

describe('GlassCardComponent', () => {
  let component: GlassCardComponent;
  let fixture: ComponentFixture<GlassCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GlassCardComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(GlassCardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and apply default padding', () => {
    expect(component).toBeTruthy();
    const cardEl = fixture.nativeElement.querySelector('.glass-card') as HTMLElement;
    expect(cardEl.style.padding).toBe('20px');
  });

  it('should toggle interactive and elevated classes based on inputs', () => {
    component.interactive = true;
    component.elevated = true;
    fixture.detectChanges();

    const cardEl = fixture.nativeElement.querySelector('.glass-card') as HTMLElement;
    expect(cardEl.classList.contains('glass-card--interactive')).toBeTrue();
    expect(cardEl.classList.contains('glass-card--elevated')).toBeTrue();
  });
});
