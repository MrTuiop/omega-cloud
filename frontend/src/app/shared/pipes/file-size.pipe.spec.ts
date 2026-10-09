import { FileSizePipe } from './file-size.pipe';

describe('FileSizePipe', () => {
  let pipe: FileSizePipe;

  beforeEach(() => {
    pipe = new FileSizePipe();
  });

  it('should format 0 and negative bytes correctly', () => {
    expect(pipe.transform(0)).toBe('0 B');
    expect(pipe.transform(-5)).toBe('0 B');
    expect(pipe.transform(null)).toBe('0 B');
    expect(pipe.transform(undefined)).toBe('0 B');
  });

  it('should format bytes under 1024', () => {
    expect(pipe.transform(512)).toBe('512 B');
  });

  it('should format kilobytes', () => {
    expect(pipe.transform(1024)).toBe('1 KB');
    expect(pipe.transform(2048)).toBe('2 KB');
  });

  it('should format megabytes', () => {
    expect(pipe.transform(1048576)).toBe('1 MB');
    expect(pipe.transform(1572864)).toBe('1.5 MB');
  });

  it('should format gigabytes and terabytes', () => {
    expect(pipe.transform(1073741824)).toBe('1 GB');
    expect(pipe.transform(1099511627776)).toBe('1 TB');
  });
});
