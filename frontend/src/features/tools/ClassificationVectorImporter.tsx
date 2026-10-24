import type { Language } from '../../shared/workbench';
import type { ClassificationImportFile, ClassificationImportItem } from './coreImportWorkflow';

interface Props {
  language: Language;
  item: ClassificationImportItem;
  selection: ClassificationImportFile;
  included: boolean;
  busy: boolean;
  onIncludeChange: (included: boolean) => void;
  onSelectionChange: (selection: ClassificationImportFile) => void;
}

function label(language: Language, zh: string, en: string): string { return language === 'zh' ? zh : en; }

export function ClassificationVectorImporter({ language, item, selection, included, busy, onIncludeChange, onSelectionChange }: Props) {
  return (
    <article className="ci-import-row" aria-label={label(language, '分类矢量来源', 'Classification vector source')}>
      <label className="ci-check-line"><input type="checkbox" checked={included} disabled={busy}
        onChange={event => onIncludeChange(event.target.checked)} /><strong>{selection.name || item.selection.name || item.selection.file}</strong></label>
      <code>{item.selection.file}</code>
      <code>{item.selection.revision}</code>
      <div className="ci-inline-meta">{label(language, '多边形要素', 'Polygon features')}: {item.featureCount} · {label(language, '预览要素', 'Preview features')}: {item.previewFeatureCount}</div>
      <label className="ci-field"><span>{label(language, '图层名称', 'Layer name')}</span><input value={selection.name ?? ''} disabled={busy}
        onChange={event => onSelectionChange({ ...selection, name: event.target.value })} /></label>
      <label className="ci-field"><span>{label(language, '分类字段（字符串列）', 'Classification field (string column)')}</span>
        <select value={selection.column ?? ''} disabled={busy} onChange={event => onSelectionChange({ ...selection, column: event.target.value || null })}>
          <option value="">{label(language, '不使用字段（NoData）', 'No field (NoData)')}</option>
          {item.columns.map(column => <option key={column} value={column}>{column}</option>)}
        </select>
      </label>
      {item.geoJson && <details><summary>{label(language, '查看服务端矢量预览数据', 'View server vector preview data')}</summary><pre className="ci-preview-data">{item.geoJson}</pre></details>}
    </article>
  );
}
