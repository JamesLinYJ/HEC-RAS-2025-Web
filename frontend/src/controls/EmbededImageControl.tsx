import type { ModelImage } from '../features/editors/model';
import type { Language } from '../shared/workbench';
import { text } from '../shared/workbench';

interface EmbededImageControlProps {
  image: ModelImage;
  language: Language;
  canRemove: boolean;
  onPreview: () => void;
  onRemove: () => void;
}

export function EmbededImageControl({ image, language, canRemove, onPreview, onRemove }: EmbededImageControlProps) {
  const src = getEmbeddedImagePreviewSrc(image.data);
  return <article className="editor-image-card">
    <button type="button" className="editor-image-preview-button" aria-label={text(language, `预览图片 ${image.name}`, `Preview image ${image.name}`)} onClick={onPreview}>
      {src ? <img src={src} alt={image.name} loading="lazy" /> : <span className="editor-image-unavailable">{text(language, '此图片格式无法在浏览器中预览', 'This image format cannot be previewed in the browser')}</span>}
    </button>
    <div className="editor-image-card-footer">
      <span title={image.name}>{image.name}</span>
      {canRemove && <button type="button" className="editor-icon-button" aria-label={text(language, `移除 ${image.name}`, `Remove ${image.name}`)} onClick={onRemove}>×</button>}
    </div>
  </article>;
}

/** Builds a data URI only for formats whose bytes have a recognized raster signature. */
export function getEmbeddedImagePreviewSrc(base64: string): string | null {
  const mime = sniffEmbeddedImageMime(base64);
  return mime && /^[A-Za-z0-9+/]*={0,2}$/.test(base64) ? `data:${mime};base64,${base64}` : null;
}

export function sniffImageMimeFromBytes(bytes: Uint8Array): string | null {
  if (bytes.length >= 8 && bytes[0] === 0x89 && ascii(bytes, 1, 3) === 'PNG' && bytes[4] === 0x0d && bytes[5] === 0x0a && bytes[6] === 0x1a && bytes[7] === 0x0a) return 'image/png';
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return 'image/jpeg';
  if (bytes.length >= 6 && (ascii(bytes, 0, 6) === 'GIF87a' || ascii(bytes, 0, 6) === 'GIF89a')) return 'image/gif';
  if (bytes.length >= 12 && ascii(bytes, 0, 4) === 'RIFF' && ascii(bytes, 8, 4) === 'WEBP') return 'image/webp';
  if (bytes.length >= 2 && ascii(bytes, 0, 2) === 'BM') return 'image/bmp';
  if (bytes.length >= 12 && ascii(bytes, 4, 4) === 'ftyp' && ['avif', 'avis'].includes(ascii(bytes, 8, 4))) return 'image/avif';
  return null;
}

export function looksLikeSvg(bytes: Uint8Array): boolean {
  const prefix = new TextDecoder().decode(bytes).replace(/^\uFEFF/, '').trimStart().slice(0, 512);
  return /^(?:<\?xml\b[^>]*>\s*)?(?:<!doctype\s+svg\b[^>]*>\s*)?<svg\b/i.test(prefix)
    || (/^<\?xml\b/i.test(prefix) && /<svg\b/i.test(prefix));
}

function sniffEmbeddedImageMime(base64: string): string | null {
  if (!/^[A-Za-z0-9+/]*={0,2}$/.test(base64) || base64.length < 4 || base64.length % 4 === 1) return null;
  const unpadded = base64.slice(0, 64).replace(/=+$/, '');
  const aligned = unpadded.slice(0, unpadded.length - (unpadded.length % 4));
  if (!aligned) return null;
  try {
    const binary = atob(aligned);
    return sniffImageMimeFromBytes(Uint8Array.from(binary, character => character.charCodeAt(0)));
  } catch {
    return null;
  }
}

function ascii(bytes: Uint8Array, offset: number, length: number): string {
  return String.fromCharCode(...bytes.subarray(offset, offset + length));
}
