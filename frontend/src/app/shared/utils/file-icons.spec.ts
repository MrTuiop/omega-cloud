import { getFileTypeInfo } from './file-icons';

describe('getFileTypeInfo', () => {
  it('should identify image files', () => {
    const png = getFileTypeInfo('png', 'image/png');
    expect(png.category).toBe('image');
    expect(png.badge).toBe('PNG');

    const webp = getFileTypeInfo('.webp', '');
    expect(webp.category).toBe('image');
    expect(webp.badge).toBe('WEBP');
  });

  it('should identify pdf files', () => {
    const pdf = getFileTypeInfo('pdf', 'application/pdf');
    expect(pdf.category).toBe('pdf');
    expect(pdf.badge).toBe('PDF');
  });

  it('should identify video and audio files', () => {
    const video = getFileTypeInfo('mp4', 'video/mp4');
    expect(video.category).toBe('video');

    const audio = getFileTypeInfo('mp3', 'audio/mpeg');
    expect(audio.category).toBe('audio');
  });

  it('should identify code and archive files', () => {
    const code = getFileTypeInfo('ts', 'application/typescript');
    expect(code.category).toBe('code');

    const zip = getFileTypeInfo('zip', 'application/zip');
    expect(zip.category).toBe('archive');
  });

  it('should fallback to generic for unknown extensions', () => {
    const unknown = getFileTypeInfo('xyz123', 'application/octet-stream');
    expect(unknown.category).toBe('generic');
    expect(unknown.badge).toBe('XYZ123');
  });
});
