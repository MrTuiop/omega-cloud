import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let service: ThemeService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [ThemeService]
    });
    service = TestBed.inject(ThemeService);
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should initialize with default dark theme', () => {
    expect(service.theme()).toBe('dark');
    expect(service.isDark()).toBeTrue();
  });

  it('should toggle theme between light and dark', () => {
    service.setTheme('dark');
    expect(service.isDark()).toBeTrue();

    service.toggleTheme();
    expect(service.theme()).toBe('light');
    expect(service.isDark()).toBeFalse();

    service.toggleTheme();
    expect(service.theme()).toBe('dark');
    expect(service.isDark()).toBeTrue();
  });

  it('should persist theme to localStorage', () => {
    service.setTheme('light');
    expect(localStorage.getItem('omega_theme_preference')).toBe('light');
  });
});
