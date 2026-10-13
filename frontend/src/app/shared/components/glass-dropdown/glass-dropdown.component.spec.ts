import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GlassDropdownComponent, DropdownOption } from './glass-dropdown.component';

describe('GlassDropdownComponent', () => {
  let component: GlassDropdownComponent;
  let fixture: ComponentFixture<GlassDropdownComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GlassDropdownComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(GlassDropdownComponent);
    component = fixture.componentInstance;
  });

  it('should create component', () => {
    expect(component).toBeTruthy();
  });

  it('should normalize string options correctly', () => {
    component.options = ['Option 1', 'Option 2'];
    fixture.detectChanges();
    expect(component.normalizedOptions().length).toBe(2);
    expect(component.normalizedOptions()[0]).toEqual({ value: 'Option 1', label: 'Option 1' });
  });

  it('should normalize DropdownOption objects correctly', () => {
    const opts: DropdownOption[] = [
      { value: 'ru', label: 'Русский', icon: 'svg...' },
      { value: 'en', label: 'English', badge: 'EN' }
    ];
    component.options = opts;
    fixture.detectChanges();
    expect(component.normalizedOptions().length).toBe(2);
    expect(component.normalizedOptions()[0].label).toBe('Русский');
  });

  it('should toggle open/close on trigger click', () => {
    expect(component.isOpen()).toBeFalse();
    component.toggleOpen();
    expect(component.isOpen()).toBeTrue();
    component.toggleOpen();
    expect(component.isOpen()).toBeFalse();
  });

  it('should select an option and emit valueChange and update model', () => {
    component.options = [
      { value: 'apple', label: 'Apple' },
      { value: 'banana', label: 'Banana' }
    ];
    fixture.detectChanges();

    let emittedValue: any = null;
    component.valueChange.subscribe(v => emittedValue = v);

    component.selectOption({ value: 'banana', label: 'Banana' });
    expect(emittedValue).toBe('banana');
    expect(component.currentValue()).toBe('banana');
    expect(component.selectedLabel()).toBe('Banana');
    expect(component.isOpen()).toBeFalse();
  });

  it('should support ControlValueAccessor writeValue', () => {
    component.options = [
      { value: 1, label: 'One' },
      { value: 2, label: 'Two' }
    ];
    component.writeValue(2);
    fixture.detectChanges();
    expect(component.currentValue()).toBe(2);
    expect(component.selectedLabel()).toBe('Two');
  });

  it('should close on Escape keydown', () => {
    component.toggleOpen();
    expect(component.isOpen()).toBeTrue();
    const event = new KeyboardEvent('keydown', { key: 'Escape' });
    component.onKeydown(event);
    expect(component.isOpen()).toBeFalse();
  });

  it('should navigate options with ArrowDown and select on Enter', () => {
    component.options = [
      { value: 'a', label: 'A' },
      { value: 'b', label: 'B' }
    ];
    component.toggleOpen();
    expect(component.highlightedIndex()).toBe(0);

    const downEvent = new KeyboardEvent('keydown', { key: 'ArrowDown' });
    component.onKeydown(downEvent);
    expect(component.highlightedIndex()).toBe(1);

    const enterEvent = new KeyboardEvent('keydown', { key: 'Enter' });
    component.onKeydown(enterEvent);
    expect(component.currentValue()).toBe('b');
    expect(component.isOpen()).toBeFalse();
  });
});
