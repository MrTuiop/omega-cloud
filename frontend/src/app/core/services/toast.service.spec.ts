import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ToastService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should add a toast and auto-dismiss after duration', fakeAsync(() => {
    service.info('Test Info', 2000);
    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].message).toBe('Test Info');
    expect(service.toasts()[0].type).toBe('info');

    tick(2000);
    expect(service.toasts().length).toBe(0);
  }));

  it('should dismiss specific toast by id', () => {
    const id1 = service.success('Toast 1', 10000);
    service.error('Toast 2', 10000);

    expect(service.toasts().length).toBe(2);
    service.dismiss(id1);

    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].message).toBe('Toast 2');
  });

  it('should clear all toasts and cancel all pending timers', fakeAsync(() => {
    service.success('Msg 1', 3000);
    service.warning('Msg 2', 5000);
    service.error('Msg 3', 7000);

    expect(service.toasts().length).toBe(3);

    service.clearAll();

    expect(service.toasts().length).toBe(0);

    // Advancing time should not re-trigger or throw errors
    tick(10000);
    expect(service.toasts().length).toBe(0);
  }));
});
