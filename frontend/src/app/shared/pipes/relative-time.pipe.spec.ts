import { RelativeTimePipe } from './relative-time.pipe';

describe('RelativeTimePipe', () => {
  let pipe: RelativeTimePipe;

  beforeEach(() => {
    pipe = new RelativeTimePipe();
  });

  it('should return empty string for null or undefined', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
  });

  it('should return "Just now" for dates within 30 seconds', () => {
    const now = new Date();
    expect(pipe.transform(now)).toBe('Just now');
  });

  it('should return minutes ago', () => {
    const date = new Date(Date.now() - 5 * 60 * 1000);
    expect(pipe.transform(date)).toBe('5m ago');
  });

  it('should return hours ago', () => {
    const date = new Date(Date.now() - 2 * 60 * 60 * 1000);
    expect(pipe.transform(date)).toBe('2h ago');
  });

  it('should return Yesterday for 1 day ago', () => {
    const date = new Date(Date.now() - 24 * 60 * 60 * 1000);
    expect(pipe.transform(date)).toBe('Yesterday');
  });
});
