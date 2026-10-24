import type { GeometryVectorImportInspection } from './vectorImportTypes';

interface ImportingPreviewListProps {
  language: 'zh' | 'en';
  inspection: GeometryVectorImportInspection;
  selectedPaths: ReadonlySet<string>;
  disabled?: boolean;
  onSelectedPathsChange: (paths: Set<string>) => void;
}

function hashForSource(source: GeometryVectorImportInspection['sources'][number]): string {
  return source.family.files.find(file => file.role === 'source')?.revision ?? '—';
}

function pointExtent(points: Array<{ x: number; y: number }>): string {
  if (points.length === 0) return '—';
  let minX = points[0].x; let minY = points[0].y; let maxX = points[0].x; let maxY = points[0].y;
  for (const point of points.slice(1)) {
    minX = Math.min(minX, point.x); minY = Math.min(minY, point.y);
    maxX = Math.max(maxX, point.x); maxY = Math.max(maxY, point.y);
  }
  return `${minX.toPrecision(7)}, ${minY.toPrecision(7)} — ${maxX.toPrecision(7)}, ${maxY.toPrecision(7)}`;
}

export function ImportingPreviewList({ language, inspection, selectedPaths, disabled = false, onSelectedPathsChange }: ImportingPreviewListProps) {
  const zh = language === 'zh';
  const allPaths = inspection.sources.map(source => source.path);
  const allSelected = allPaths.length > 0 && allPaths.every(path => selectedPaths.has(path));

  const toggle = (path: string): void => {
    const next = new Set(selectedPaths);
    if (next.has(path)) next.delete(path); else next.add(path);
    onSelectedPathsChange(next);
  };

  return <section className="geometry-import-preview" aria-label={zh ? '几何导入预览' : 'Geometry import preview'}>
    <header className="geometry-import-preview-heading"><div><h3>{zh ? '来源检查与几何预览' : 'Source inspection and geometry preview'}</h3>
      <p>{zh ? '以下数量、类型、顶点和文件身份由几何服务读取回传。' : 'Counts, types, vertices, and file identities below came from the geometry service.'}</p></div>
      <label className="geometry-import-select-all"><input type="checkbox" checked={allSelected} disabled={disabled || allPaths.length === 0}
        onChange={() => onSelectedPathsChange(allSelected ? new Set() : new Set(allPaths))} />{zh ? '全选有效来源' : 'Select all valid sources'}</label>
    </header>
    {inspection.sources.length === 0 && <p className="geometry-import-empty">{zh ? '服务没有返回可导入的线或面来源。' : 'The service returned no importable line or polygon sources.'}</p>}
    <ul className="geometry-import-source-list">{inspection.sources.map(source => {
      const selected = selectedPaths.has(source.path);
      const available = source.featureCount > 0;
      return <li key={`${source.path}:${source.family.revision}`} className="geometry-import-source-item">
        <label className="geometry-import-source-check"><input type="checkbox" checked={selected} disabled={disabled || !available}
          onChange={() => toggle(source.path)} />
          <span><strong>{source.name}</strong><small>{source.shapeType} · {source.featureCount.toLocaleString()} {zh ? '个要素' : 'features'}</small></span>
        </label>
        <dl className="geometry-import-source-identity"><div><dt>{zh ? '服务器路径' : 'Server path'}</dt><dd><code>{source.path}</code></dd></div>
          <div><dt>SHA-256</dt><dd><code>{hashForSource(source)}</code></dd></div>
          <div><dt>{zh ? '文件族修订' : 'Family revision'}</dt><dd><code>{source.family.revision}</code></dd></div></dl>
        <details className="geometry-import-source-details"><summary>{zh ? '查看读取到的要素坐标摘要与文件族' : 'View feature coordinate summary and family'}</summary>
          <div className="geometry-import-feature-list">{source.features.slice(0, 12).map(feature => {
            const bounds = pointExtent(feature.points);
            return <div className="geometry-import-feature-row" key={`${feature.sourceOrdinal}:${feature.geometryType}`}>
              <span>{zh ? '源序号' : 'Source ordinal'} {feature.sourceOrdinal}</span><span>{feature.geometryType}</span>
              <span>{feature.points.length.toLocaleString()} {zh ? '个顶点' : 'vertices'}</span><code>{bounds}</code>
            </div>;
          })}
          {source.features.length > 12 && <p className="geometry-import-preview-limit">{zh ? `已列出前 12 个要素；服务读取并回传 ${source.features.length.toLocaleString()} 个。` : `Showing the first 12 of ${source.features.length.toLocaleString()} service-read features.`}</p>}
          </div>
          <ul className="geometry-import-family-list">{source.family.files.map(file => <li key={file.role}>
            <span>{file.role} · {file.required ? (zh ? '必需' : 'required') : (zh ? '可选' : 'optional')}</span>
            <code>{file.path}</code><code>{file.revision ?? (zh ? '不存在' : 'Missing')}</code>
          </li>)}</ul>
        </details>
      </li>;
    })}</ul>
    {inspection.issues.length > 0 && <section className="geometry-import-issues" aria-label={zh ? '来源检查问题' : 'Source inspection issues'}>
      <h4>{zh ? '未能读取的来源' : 'Sources that could not be read'}</h4>
      <ul>{inspection.issues.map((issue, index) => <li key={`${issue.path}:${index}`}><code>{issue.path}</code><span>{issue.message}</span></li>)}</ul>
    </section>}
  </section>;
}
