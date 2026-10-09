import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BreadcrumbsComponent } from './breadcrumbs.component';

describe('BreadcrumbsComponent', () => {
  let component: BreadcrumbsComponent;
  let fixture: ComponentFixture<BreadcrumbsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BreadcrumbsComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(BreadcrumbsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should emit null when root drive is clicked', () => {
    spyOn(component.navigate, 'emit');
    const rootBtn = fixture.nativeElement.querySelector('.breadcrumb-root');
    rootBtn.click();
    expect(component.navigate.emit).toHaveBeenCalledWith(null);
  });

  it('should render breadcrumb hierarchy and emit clicked folder id', () => {
    component.breadcrumbs = [
      { id: 'f-1', name: 'Work' },
      { id: 'f-2', name: 'Projects' }
    ];
    fixture.detectChanges();

    spyOn(component.navigate, 'emit');
    const buttons = fixture.nativeElement.querySelectorAll('.breadcrumb-btn');
    // First button is root Drive, second button is 'Work'
    expect(buttons.length).toBe(2);
    buttons[1].click();
    expect(component.navigate.emit).toHaveBeenCalledWith('f-1');

    // Last crumb 'Projects' is non-clickable span
    const current = fixture.nativeElement.querySelector('.breadcrumb-current');
    expect(current.textContent).toContain('Projects');
  });
});
