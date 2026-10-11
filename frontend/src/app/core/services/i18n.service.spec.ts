import { TestBed } from '@angular/core/testing';
import { I18nService } from './i18n.service';

describe('I18nService', () => {
  let service: I18nService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [I18nService]
    });
    service = TestBed.inject(I18nService);
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should provide translations for ru, en and zh', () => {
    service.setLanguage('en');
    expect(service.t('nav.drive')).toBe('My Drive');

    service.setLanguage('ru');
    expect(service.t('nav.drive')).toBe('Мой диск');

    service.setLanguage('zh');
    expect(service.t('nav.drive')).toBe('我的网盘');
  });

  it('should fallback to english or key if key is unknown', () => {
    service.setLanguage('ru');
    expect(service.t('unknown.key.123')).toBe('unknown.key.123');
  });

  it('should persist selected language to localStorage', () => {
    service.setLanguage('zh');
    expect(localStorage.getItem('omega_cloud_lang')).toBe('zh');
    expect(service.currentLang()).toBe('zh');
  });
});
