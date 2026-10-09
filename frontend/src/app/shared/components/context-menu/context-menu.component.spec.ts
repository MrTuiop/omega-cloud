import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContextMenuComponent } from './context-menu.component';

describe('ContextMenuComponent', () => {
  let component: ContextMenuComponent;
  let fixture: ComponentFixture<ContextMenuComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContextMenuComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ContextMenuComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should not render menu when isOpen is false', () => {
    component.isOpen = false;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.context-menu')).toBeNull();
  });

  it('should render items when isOpen is true and emit itemClick on select', () => {
    component.isOpen = true;
    component.items = [
      { id: 'download', label: 'Download' },
      { id: 'delete', label: 'Delete', danger: true }
    ];
    fixture.detectChanges();

    spyOn(component.itemClick, 'emit');
    const items = fixture.nativeElement.querySelectorAll('.menu-item');
    expect(items.length).toBe(2);

    items[0].click();
    expect(component.itemClick.emit).toHaveBeenCalledWith('download');
  });

  it('should emit close when escape key is pressed', () => {
    component.isOpen = true;
    fixture.detectChanges();

    spyOn(component.close, 'emit');
    component.onEscape();
    expect(component.close.emit).toHaveBeenCalled();
  });
});
