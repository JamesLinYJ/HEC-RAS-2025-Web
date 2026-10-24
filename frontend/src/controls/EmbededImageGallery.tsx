import { useEffect, useId, useRef, useState } from 'react';
import type { ChangeEvent } from 'react';
import type { ModelImage } from '../features/editors/model';
import type { Language } from '../shared/workbench';
import { text } from '../shared/workbench';
import { EmbededImageControl, getEmbeddedImagePreviewSrc, looksLikeSvg, sniffImageMimeFromBytes } from './EmbededImageControl';

interface EmbededImageGalleryProps {
  fieldKey: string;
  images: ModelImage[];
  modelImageBytes: number;
  disabled: boolean;
  language: Language;
  onImagesChange?: (fieldID: string, images: ModelImage[]) => void;
}

const maxImageBytes = 2 * 1024 * 1024;
const maxModelImageBytes = 3 * 1024 * 1024;
const supportedMimeTypes = ['image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/bmp', 'image/avif'];

export function EmbededImageGallery({ fieldKey, images, modelImageBytes, disabled, language, onImagesChange }: EmbededImageGalleryProps) {
  const inputID = useId();
  const generation = useRef(0);
  const imagesRef = useRef(images);
  const fieldKeyRef = useRef(fieldKey);
  const modelImageBytesRef = useRef(modelImageBytes);
  const changeRef = useRef(onImagesChange);
  const disabledRef = useRef(disabled || !onImagesChange);
  const [error, setError] = useState<string | null>(null);
  const [previewID, setPreviewID] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  imagesRef.current = images;
  fieldKeyRef.current = fieldKey;
  modelImageBytesRef.current = modelImageBytes;
  changeRef.current = onImagesChange;
  disabledRef.current = disabled || !onImagesChange;
  const previewImage = images.find(image => image.id === previewID) ?? null;

  useEffect(() => {
    generation.current += 1;
    setError(null);
    setPreviewID(null);
    setBusy(false);
  }, [fieldKey]);

  useEffect(() => () => { generation.current += 1; }, []);

  const chooseFiles = async (event: ChangeEvent<HTMLInputElement>): Promise<void> => {
    const input = event.currentTarget;
    const requestGeneration = ++generation.current;
    const requestFieldKey = fieldKey;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    setError(null);
    setBusy(true);
    try {
      let totalBytes = 0;
      for (const file of files) {
        totalBytes += file.size;
        if (file.size <= 0) throw new Error(text(language, `“${file.name}”是空文件。`, `“${file.name}” is empty.`));
        if (file.size > maxImageBytes) throw new Error(text(language, `“${file.name}”超过单张图片 2 MiB 的上限。`, `“${file.name}” exceeds the 2 MiB per-image limit.`));
        if (file.type.toLowerCase() === 'image/svg+xml' || file.name.toLowerCase().endsWith('.svg')) {
          throw new Error(text(language, `“${file.name}”为 SVG。为避免脚本内容风险，只接受栅格图片。`, `“${file.name}” is SVG. Only raster images are accepted to avoid script content.`));
        }
      }
      if (modelImageBytesRef.current + totalBytes > maxModelImageBytes) {
        throw new Error(text(language, '加入所选图片后，模型内嵌图片总量将超过 3 MiB 原始数据。图片传输会膨胀为约 4 MiB base64，为 Host 8 Mi 字符协议行中的其他数据留出余量。现有超限图片仍可预览；请先移除部分图片或选择较小文件。', 'Adding these files would exceed the 3 MiB model-wide raw image budget. Base64 transport expands that to about 4 MiB, reserving room for other data in the Host 8 Mi-character protocol line. Existing over-budget images remain available to preview; remove some images or choose smaller files.'));
      }

      const added = await Promise.all(files.map(async file => {
        const header = new Uint8Array(await file.slice(0, 512).arrayBuffer());
        if (looksLikeSvg(header)) throw new Error(text(language, `“${file.name}”内容为 SVG。为避免脚本内容风险，只接受栅格图片。`, `“${file.name}” contains SVG data. Only raster images are accepted to avoid script content.`));
        const mime = sniffImageMimeFromBytes(header);
        if (!mime || !supportedMimeTypes.includes(mime)) {
          throw new Error(text(language, `“${file.name}”不是支持的 PNG、JPEG、GIF、WebP、BMP 或 AVIF 图片。`, `“${file.name}” is not a supported PNG, JPEG, GIF, WebP, BMP, or AVIF image.`));
        }
        const declaredMime = normalizeMimeType(file.type);
        if (declaredMime && declaredMime.startsWith('image/') && declaredMime !== mime) {
          // The bytes are authoritative; mismatched MIME declarations are rejected instead of relabeling the payload.
          throw new Error(text(language, `“${file.name}”的 MIME 类型与图片内容不一致。`, `“${file.name}” has a MIME type that does not match its image content.`));
        }
        const data = await fileAsBase64(file);
        return { id: newImageID(), name: file.name, data } satisfies ModelImage;
      }));

      if (requestGeneration !== generation.current || requestFieldKey !== fieldKeyRef.current || !input.isConnected || disabledRef.current) return;
      if (modelImageBytesRef.current + totalBytes > maxModelImageBytes) {
        throw new Error(text(language, '读取期间模型图片预算已变化，未添加所选图片。预算按原始字节计，并为 base64 与 Host 协议行中的其他数据留有余量；现有超限图片仍可预览。', 'The model image budget changed while these files were being read, so they were not added. The raw-byte budget reserves room for base64 expansion and other Host protocol data; existing over-budget images remain available to preview.'));
      }
      changeRef.current?.(requestFieldKey, [...imagesRef.current, ...added]);
    } catch (caught) {
      if (requestGeneration === generation.current && requestFieldKey === fieldKeyRef.current && input.isConnected && !disabledRef.current) {
        setError(caught instanceof Error ? caught.message : text(language, '读取图片失败。', 'Could not read the image files.'));
      }
    } finally {
      input.value = '';
      if (requestGeneration === generation.current) setBusy(false);
    }
  };

  const modelImageBudgetReached = modelImageBytes >= maxModelImageBytes;

  return <div className="editor-image-collection">
    {images.length > 0 ? <div className="editor-image-gallery-grid">
      {images.map(image => <EmbededImageControl key={image.id} image={image} language={language} canRemove={!disabled && Boolean(onImagesChange)}
        onPreview={() => setPreviewID(image.id)} onRemove={() => onImagesChange?.(fieldKey, images.filter(candidate => candidate.id !== image.id))} />)}
    </div> : <small className="editor-field-muted">{text(language, '没有嵌入图片。', 'No embedded images.')}</small>}
    {!disabled && onImagesChange && <label className="editor-file-button" htmlFor={inputID} aria-disabled={busy || modelImageBudgetReached || undefined}>
      {busy ? text(language, '正在读取…', 'Reading…') : modelImageBudgetReached ? text(language, '图片预算已满', 'Image budget reached') : text(language, '添加图片', 'Add images')}
      <input id={inputID} type="file" accept="image/png,image/jpeg,image/gif,image/webp,image/bmp,image/avif,image/*" multiple disabled={busy || modelImageBudgetReached}
        aria-label={text(language, '选择要嵌入的图片', 'Choose images to embed')} onChange={event => { void chooseFiles(event); }} />
    </label>}
    <small className="editor-field-muted">{text(language, '支持 PNG、JPEG、GIF、WebP、BMP、AVIF。新增图片单张不超过 2 MiB；模型全部图片（含现有图片）按原始字节计不超过 3 MiB，base64 传输约 4 MiB，为 Host 8 Mi 字符协议行中的其他数据留出余量。', 'PNG, JPEG, GIF, WebP, BMP, and AVIF. New images are limited to 2 MiB each; all model images, including existing ones, are limited to 3 MiB raw (about 4 MiB in base64), leaving room for other data in the Host 8 Mi-character protocol line.')}</small>
    {!disabled && onImagesChange && modelImageBudgetReached && <small className="editor-field-muted" role="status">{text(language, '当前模型图片已达到或超过新增预算；现有图片仍可预览，移除部分图片后可继续添加。', 'The current model has reached or exceeded the upload budget. Existing images remain available to preview; remove some images before adding more.')}</small>}
    {error && <p className="editor-inline-error" role="alert">{error}</p>}
    {previewImage && <div className="editor-image-preview-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) setPreviewID(null); }}>
      <section className="editor-image-preview-dialog" role="dialog" aria-modal="true" aria-label={previewImage.name}>
        <header><strong>{previewImage.name}</strong><button type="button" className="editor-button" onClick={() => setPreviewID(null)}>{text(language, '关闭', 'Close')}</button></header>
        {getEmbeddedImagePreviewSrc(previewImage.data)
          ? <img src={getEmbeddedImagePreviewSrc(previewImage.data) ?? undefined} alt={previewImage.name} />
          : <p>{text(language, '此图片格式无法在浏览器中预览。', 'This image format cannot be previewed in the browser.')}</p>}
      </section>
    </div>}
  </div>;
}

/** Counts the raw byte size represented by the base64 ModelSnapshot payloads. */
export function embeddedImageCollectionBytes(collections: Record<string, ModelImage[]>): number {
  let total = 0;
  for (const images of Object.values(collections)) {
    for (const image of images) total += embeddedImageBase64Bytes(image.data);
  }
  return total;
}

function embeddedImageBase64Bytes(data: string): number {
  if (data === '') return 0;
  if (data.length % 4 !== 0 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(data)) return Number.POSITIVE_INFINITY;
  const padding = data.endsWith('==') ? 2 : data.endsWith('=') ? 1 : 0;
  return data.length / 4 * 3 - padding;
}

function fileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('Could not read image file.'));
    reader.onload = () => {
      const value = reader.result;
      if (typeof value !== 'string') return reject(new Error('Could not encode image file.'));
      const comma = value.indexOf(',');
      if (comma < 0) return reject(new Error('Could not encode image file.'));
      resolve(value.slice(comma + 1));
    };
    reader.readAsDataURL(file);
  });
}

function newImageID(): string {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  const bytes = new Uint8Array(16);
  if (globalThis.crypto?.getRandomValues) globalThis.crypto.getRandomValues(bytes);
  else bytes.forEach((_value, index) => { bytes[index] = Math.floor(Math.random() * 256); });
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function normalizeMimeType(value: string): string {
  const type = value.trim().toLowerCase();
  if (type === 'image/jpg' || type === 'image/pjpeg') return 'image/jpeg';
  if (type === 'image/x-png') return 'image/png';
  return type;
}
