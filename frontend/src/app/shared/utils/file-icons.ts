export interface FileTypeInfo {
  category: 'image' | 'video' | 'audio' | 'pdf' | 'document' | 'code' | 'archive' | 'generic';
  color: string;
  badge: string;
}

export function getFileTypeInfo(extension: string, mimeType: string): FileTypeInfo {
  const ext = (extension || '').toLowerCase().replace(/^\./, '');
  const mime = (mimeType || '').toLowerCase();

  if (mime.startsWith('image/') || ['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg', 'heic', 'bmp', 'ico'].includes(ext)) {
    return { category: 'image', color: '#bf5af2', badge: ext.toUpperCase() || 'IMG' };
  }

  if (mime.startsWith('video/') || ['mp4', 'mov', 'mkv', 'avi', 'webm', 'wmv'].includes(ext)) {
    return { category: 'video', color: '#0a84ff', badge: ext.toUpperCase() || 'MOV' };
  }

  if (mime.startsWith('audio/') || ['mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg'].includes(ext)) {
    return { category: 'audio', color: '#ff375f', badge: ext.toUpperCase() || 'AUD' };
  }

  if (ext === 'pdf' || mime === 'application/pdf') {
    return { category: 'pdf', color: '#ff453a', badge: 'PDF' };
  }

  if (['zip', 'rar', '7z', 'tar', 'gz', 'bz2'].includes(ext)) {
    return { category: 'archive', color: '#ff9f0a', badge: ext.toUpperCase() || 'ZIP' };
  }

  if (['ts', 'js', 'json', 'cs', 'html', 'css', 'scss', 'py', 'go', 'rs', 'c', 'cpp', 'java', 'xml', 'yaml', 'yml'].includes(ext)) {
    return { category: 'code', color: '#30d158', badge: ext.toUpperCase() || 'CODE' };
  }

  if (['doc', 'docx', 'txt', 'rtf', 'md', 'xls', 'xlsx', 'ppt', 'pptx', 'csv'].includes(ext)) {
    return { category: 'document', color: '#64d2ff', badge: ext.toUpperCase() || 'DOC' };
  }

  return { category: 'generic', color: '#8e8e93', badge: ext.toUpperCase() || 'FILE' };
}
