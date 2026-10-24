import type { Language } from '../../../shared/workbench';
import type { TerrainUSGSProduct } from './usgsTypes';

interface Props { language: Language; product: TerrainUSGSProduct; selected: boolean; importing: boolean; onToggle: () => void }
function formatBytes(value: number | null | undefined, language: Language): string {
  if (value == null || !Number.isFinite(value) || value < 0) return language === 'zh' ? '未提供' : 'Not provided';
  if (value < 1024) return `${value} B`;
  const units = ['KiB', 'MiB', 'GiB', 'TiB'];
  let amount = value / 1024, index = 0;
  while (amount >= 1024 && index < units.length - 1) { amount /= 1024; index++; }
  return `${amount.toFixed(1)} ${units[index]}`;
}
function safeHttpUrl(value: string | null | undefined): string | undefined {
  if (!value) return undefined;
  try {
    const parsed = new URL(value);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.href : undefined;
  } catch { return undefined; }
}

export function USGSSummary({ language, product, selected, importing, onToggle }: Props) {
  const zh = language === 'zh';
  const metadataUrl = safeHttpUrl(product.metadataUrl);
  return <article className={`usgs-product-card${selected ? ' is-selected' : ''}`}>
    <label className="usgs-product-select"><input type="checkbox" checked={selected} disabled={importing} onChange={onToggle} />
      <strong title={product.title}>{product.title}</strong></label>
    <dl><div><dt>{zh ? '分辨率' : 'Resolution'}</dt><dd>{product.resolution || '—'}</dd></div>
      <div><dt>{zh ? '发布日期' : 'Publication date'}</dt><dd>{product.publicationDate || '—'}</dd></div>
      <div><dt>{zh ? '数据大小' : 'Data size'}</dt><dd>{formatBytes(product.sizeInBytes, language)}</dd></div></dl>
    {importing && <span className="usgs-importing-status" role="status">{zh ? '正在导入' : 'Importing'}</span>}
    <details><summary>{zh ? '来源与边界详情' : 'Source and boundary details'}</summary>
      <p><span>{zh ? '产品 ID' : 'Product ID'}：</span><code>{product.id}</code></p>
      {product.boundingBox && <p><span>{zh ? '地理范围' : 'Geographic bounds'}：</span><code>{product.boundingBox.minX}, {product.boundingBox.minY} – {product.boundingBox.maxX}, {product.boundingBox.maxY}</code></p>}
      {metadataUrl && <p><a href={metadataUrl} target="_blank" rel="noreferrer">{zh ? 'USGS 元数据' : 'USGS metadata'}</a></p>}
    </details>
  </article>;
}
