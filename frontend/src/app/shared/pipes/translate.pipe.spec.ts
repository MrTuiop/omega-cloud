import { TestBed } from '@angular/core/testing';
import { TranslatePipe } from './translate.pipe';
import { I18nService } from '../../core/services/i18n.service';

describe('TranslatePipe', () => {
  let pipe: TranslatePipe;
  let i18nService: I18nService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [I18nService, TranslatePipe]
    });
    i18nService = TestBed.inject(I18nService);
    pipe = TestBed.inject(TranslatePipe);
  });

  it('should transform key using active language', () => {
    i18nService.setLanguage('en');
    expect(pipe.transform('nav.drive')).toBe('My Drive');

    i18nService.setLanguage('ru');
    expect(pipe.transform('nav.drive')).toBe('Мой диск');
  });
});
