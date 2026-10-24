import type { EditorPageProps } from './model';

/** Source equivalent of SmallBoundaryConditionsSummary, limited to values the Host snapshot supplies. */
export function SmallBoundaryConditionsSummary(props: EditorPageProps) {
  const { asset, snapshot, draft, language } = props;
  const descriptionID = 'BoundaryConditionsSummary/BoundaryCondition_Description';
  const description = draft.fields[descriptionID] ?? snapshot.fields[descriptionID] ?? '';
  const spatial = snapshot.boundary?.spatial;
  const spatialLayers = [
    ['precipitation', language === 'zh' ? '降水' : 'Precipitation'],
    ['evapotranspiration', language === 'zh' ? '蒸散发' : 'Evapotranspiration'],
    ['wind', language === 'zh' ? '风' : 'Wind'],
    ['airPressure', language === 'zh' ? '气压' : 'Air pressure'],
    ['airDensity', language === 'zh' ? '空气密度' : 'Air density'],
  ] as const;
  const reportedLayers = spatialLayers.flatMap(([id, title]) => {
    const layer = spatial?.[id];
    return layer ? [{ id, title, stations: layer.stations.length, inputs: layer.inputs.length }] : [];
  });

  return <aside className="editor-table-card" aria-label={language === 'zh' ? '边界条件文件摘要' : 'Boundary condition file summary'}>
    <div className="editor-terrain-table-heading">
      <h3>{language === 'zh' ? '边界条件文件' : 'Boundary condition file'}</h3>
      <span className="editor-source-chip">{language === 'zh' ? '服务端快照' : 'Host snapshot'}</span>
    </div>
    <dl className="editor-readonly-list">
      <div><dt>{language === 'zh' ? '文件名' : 'Filename'}</dt><dd>{asset.name}</dd></div>
      <div><dt>{language === 'zh' ? '文件路径' : 'File path'}</dt><dd><code>{asset.path}</code></dd></div>
      <div><dt>{language === 'zh' ? '快照修订' : 'Snapshot revision'}</dt><dd><code>{snapshot.revision}</code></dd></div>
      <div><dt>{language === 'zh' ? '描述' : 'Description'}</dt><dd>{description || (language === 'zh' ? '未填写' : 'Not provided')}</dd></div>
    </dl>
    <p className="editor-field-muted">
      {language === 'zh'
        ? '当前快照未提供文件大小；此摘要不估算文件长度。'
        : 'The current snapshot does not include file size; this summary does not estimate it.'}
    </p>
    <section aria-label={language === 'zh' ? '快照中的空间气象数据' : 'Spatial meteorological data in snapshot'}>
      <strong>{language === 'zh' ? '空间气象数据（快照）' : 'Spatial meteorological data (snapshot)'}</strong>
      {reportedLayers.length > 0
        ? <ul>{reportedLayers.map((layer) => <li key={layer.id}>{layer.title}：{language === 'zh' ? `${layer.stations} 个站点，${layer.inputs} 个输入` : `${layer.stations} stations, ${layer.inputs} inputs`}</li>)}</ul>
        : <p className="editor-field-muted">{language === 'zh' ? '快照未提供空间气象输入明细。' : 'The snapshot contains no spatial meteorological input details.'}</p>}
    </section>
  </aside>;
}
