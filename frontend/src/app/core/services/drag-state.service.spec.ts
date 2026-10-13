import { TestBed } from '@angular/core/testing';
import { DragStateService } from './drag-state.service';

describe('DragStateService', () => {
  let service: DragStateService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DragStateService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
    expect(service.isDragging()).toBeFalse();
    expect(service.dragData()).toBeNull();
  });

  it('should start and end drag', () => {
    service.startDrag({ fileIds: ['file-1'], folderIds: ['folder-1'] });
    expect(service.isDragging()).toBeTrue();
    expect(service.dragData()?.fileIds).toEqual(['file-1']);
    expect(service.dragData()?.folderIds).toEqual(['folder-1']);

    service.endDrag();
    expect(service.isDragging()).toBeFalse();
    expect(service.dragData()).toBeNull();
  });

  it('should preserve single fileId when fileIds is an empty array', () => {
    service.startDrag({ fileIds: [], fileId: 'file-single' });
    expect(service.isDragging()).toBeTrue();
    expect(service.dragData()?.fileIds).toEqual(['file-single']);
    expect(service.dragData()?.fileId).toBe('file-single');
  });
});
