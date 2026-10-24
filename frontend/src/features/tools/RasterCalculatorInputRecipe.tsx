import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { RasterCalculatorInputDraft, RasterCalculatorSourceChoice } from './RasterCalculatorRecipe';

interface Props {
  language: Language;
  value: RasterCalculatorInputDraft;
  sources: readonly RasterCalculatorSourceChoice[];
  onChange: (value: RasterCalculatorInputDraft, preferKeyFrame?: boolean) => void;
  onRemove: () => void;
}

export function RasterCalculatorInputRecipe({ language, value, sources, onChange, onRemove }: Props) {
  const source = sources.find(candidate => candidate.id === value.sourceID);
  const sourceRevisionChanged = Boolean(source && source.revision !== value.sourceRevision);
  const sourceMissing = Boolean(value.sourceID && !source);
  const resultInput = source?.kind === 'resultMap' || value.kind === 'resultMap';
  const updateSource = (sourceID: string): void => {
    const selected = sources.find(candidate => candidate.id === sourceID);
    if (!selected) {
      onChange({ ...value, sourceID: '', sourceRevision: '', kind: '', mapType: null, forceHorizontal: false, isKeyFrame: false });
      return;
    }
    onChange({
      ...value,
      sourceID: selected.id,
      sourceRevision: selected.revision,
      kind: selected.kind,
      mapType: selected.kind === 'resultMap' ? selected.mapTypes[0] ?? null : null,
      forceHorizontal: false,
      isKeyFrame: selected.kind === 'resultMap',
    });
  };

  return <article className="tools-constant-row tools-calculator-input-row" data-testid="raster-calculator-input-row">
    <label className="tools-field"><span>{text(language, '变量', 'Variable')}</span>
      <input aria-label={text(language, '输入变量名', 'Input variable name')} value={value.variableName}
        onChange={event => onChange({ ...value, variableName: event.target.value })} spellCheck={false} />
    </label>
    <label className="tools-field"><span>{text(language, '真实栅格源', 'Raster source')}</span>
      <select aria-label={text(language, '选择计算输入源', 'Choose calculator input source')} value={value.sourceID}
        onChange={event => updateSource(event.target.value)}>
        <option value="">{text(language, '请选择已检查的源', 'Choose an inspected source')}</option>
        {sources.map(candidate => <option key={candidate.id} value={candidate.id}>
          {candidate.kind === 'terrain' ? text(language, '地形', 'Terrain') : text(language, '结果地图', 'Result map')} · {candidate.name}
          {!candidate.isAvailable ? text(language, '（不可用）', ' (unavailable)') : ''}
        </option>)}
      </select>
    </label>
    <button className="tools-remove-button" type="button" aria-label={text(language, '移除输入源', 'Remove input source')} onClick={onRemove}>×</button>

    {source && <dl className="tools-source-details" style={{ gridColumn: '1 / -1' }}>
      <div><dt>{text(language, '来源路径', 'Source path')}</dt><dd><code>{source.assetPath}</code></dd></div>
      <div><dt>{text(language, '来源修订', 'Source revision')}</dt><dd><code>{source.revision}</code></dd></div>
      <div><dt>{text(language, '来源范围', 'Source extent')}</dt><dd><code>{source.minX}, {source.minY} → {source.maxX}, {source.maxY}</code></dd></div>
      <div><dt>NoData</dt><dd><code>{source.noDataValue ?? '—'}{source.noDataValue !== null && source.noDataValue !== -9999 ? ` · ${value.variableName.trim()}_ndv` : ''}</code></dd></div>
      {source.files.map(file => <div key={`${file.role}:${file.path}`}>
        <dt>{file.role}</dt><dd><code>{file.path}</code><small>{file.exists ? file.revision ?? text(language, '修订未捕获', 'revision unavailable') : text(language, '文件缺失', 'missing')}</small></dd>
      </div>)}
      {!source.isAvailable && <div role="status"><dt>{text(language, '状态', 'Status')}</dt><dd>{source.unavailableReason || text(language, 'Host 标记此来源不可用。', 'The Host marked this source unavailable.')}</dd></div>}
      {sourceRevisionChanged && <div role="alert" style={{ gridColumn: '1 / -1' }}><dt>{text(language, '修订变化', 'Revision changed')}</dt><dd>
        {text(language, '来源文件修订已变化。刷新后请显式重新绑定到当前版本。', 'The source revision changed. Refresh and explicitly rebind to the current version.')}
        <button className="tools-text-button" type="button" disabled={!source.isAvailable}
          onClick={() => onChange({ ...value, sourceRevision: source.revision, kind: source.kind,
            mapType: source.kind === 'resultMap' ? (value.mapType && source.mapTypes.includes(value.mapType) ? value.mapType : source.mapTypes[0] ?? null) : null,
            forceHorizontal: source.kind === 'resultMap' && value.forceHorizontal,
            isKeyFrame: source.kind === 'resultMap' && value.isKeyFrame })}>
          {text(language, '重新绑定当前修订', 'Rebind current revision')}
        </button>
      </dd></div>}
    </dl>}
    {sourceMissing && <p className="tools-notice" role="alert" style={{ gridColumn: '1 / -1' }}>{text(language, '此来源已不在当前 Host 清单中。请选择新源。', 'This source is absent from the current Host catalog. Choose a new source.')}</p>}

    {resultInput && <div className="tools-field-grid tools-calculator-result-options" style={{ gridColumn: '1 / -1' }}>
      <label className="tools-field"><span>{text(language, '结果地图类型', 'Result map type')}</span>
        <select aria-label={text(language, '结果地图类型', 'Result map type')} value={value.mapType ?? ''}
          onChange={event => onChange({ ...value, mapType: event.target.value || null })}>
          <option value="">{text(language, '选择 Host 提供的地图类型', 'Choose a Host-provided map type')}</option>
          {(source?.mapTypes ?? []).map(mapType => <option key={mapType} value={mapType}>{mapType}</option>)}
        </select>
      </label>
      <label className="tools-calculator-check"><input type="checkbox" checked={value.forceHorizontal}
        onChange={event => onChange({ ...value, forceHorizontal: event.target.checked })} />
        <span>{text(language, '按水平几何采样', 'Force horizontal geometry sampling')}</span>
      </label>
      <label className="tools-calculator-check"><input type="radio" name="raster-calculator-keyframe"
        checked={value.isKeyFrame} onChange={() => onChange({ ...value, isKeyFrame: true }, true)} />
        <span>{text(language, '以此结果驱动计算时刻', 'Drive calculator timesteps from this Result')}</span>
      </label>
      {source && <p className="tools-field-help">{text(language, 'Host 返回', 'Host returned')} {source.profileCount} {text(language, '个时刻', 'profiles')}
        {source.profileTimes.length > 0 ? ` · ${source.profileTimes[0]} → ${source.profileTimes[source.profileTimes.length - 1]}` : ''}</p>}
    </div>}
  </article>;
}
