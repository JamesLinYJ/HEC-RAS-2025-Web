import { useEffect, useState } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import { message, text } from '../../shared/workbench';
import type { ProjectionEvaluation, ProjectionMatch } from './ProjectionOption';

type Props = FeaturePageProps & { onDirtyStateChange?: (owner: string, dirty: boolean) => void };

export function SelectProjectionPage({ language, backend, onDirtyStateChange }: Props) {
  const zh = language === 'zh';
  const [mode, setMode] = useState<'search' | 'location'>('search');
  const [query, setQuery] = useState('');
  const [longitude, setLongitude] = useState('');
  const [latitude, setLatitude] = useState('');
  const [projectionType, setProjectionType] = useState('EPSG');
  const [projectionValue, setProjectionValue] = useState('4326');
  const [matches, setMatches] = useState<ProjectionMatch[]>([]);
  const [evaluation, setEvaluation] = useState<ProjectionEvaluation>();
  const [busy, setBusy] = useState<'search' | 'evaluate' | undefined>();
  const [error, setError] = useState<string>();
  const dirty = query !== '' || longitude !== '' || latitude !== '' || projectionType !== 'EPSG' || projectionValue !== '4326';
  const backendFailure = (actionZh: string, actionEn: string, caught: unknown): string => {
    const detail = message(caught);
    return zh ? `${actionZh}后端原始错误：${detail}` : `${actionEn} Backend error: ${detail}`;
  };

  useEffect(() => { onDirtyStateChange?.('tools.projection', dirty); return () => onDirtyStateChange?.('tools.projection', false); }, [dirty, onDirtyStateChange]);

  const search = async (): Promise<void> => {
    setError(undefined); setEvaluation(undefined); setMatches([]);
    if (!backend.supports('projection.search')) { setError(text(language, '当前 C# 后端未提供投影搜索。', 'Projection search is not available in the C# backend.')); return; }
    const lon = Number(longitude), lat = Number(latitude);
    if (mode === 'location' && (!longitude.trim() || !latitude.trim() || !Number.isFinite(lon) || !Number.isFinite(lat) || lon < -180 || lon > 180 || lat < -90 || lat > 90)) {
      setError(text(language, '请输入有效的经度和纬度。', 'Enter a valid longitude and latitude.')); return;
    }
    setBusy('search');
    try {
      const result = await backend.request<ProjectionMatch[]>('projection.search', mode === 'location'
        ? { longitude: lon, latitude: lat, limit: 30 }
        : { query: query.trim(), limit: 30 });
      setMatches(result);
    } catch (caught) { setError(backendFailure('投影搜索失败。', 'Projection search failed.', caught)); }
    finally { setBusy(undefined); }
  };

  const evaluate = async (): Promise<void> => {
    setError(undefined); setEvaluation(undefined);
    if (!backend.supports('projection.evaluate')) { setError(text(language, '当前 C# 后端未提供投影检查。', 'Projection evaluation is not available in the C# backend.')); return; }
    if (!projectionType || !projectionValue.trim()) { setError(text(language, '请输入投影定义类型和值。', 'Enter a projection definition type and value.')); return; }
    setBusy('evaluate');
    try {
      const result = await backend.request<ProjectionEvaluation>('projection.evaluate', { type: projectionType, value: projectionValue });
      setEvaluation(result);
    } catch (caught) { setError(backendFailure('投影定义检查失败。', 'Projection definition evaluation failed.', caught)); }
    finally { setBusy(undefined); }
  };

  const chooseMatch = (match: ProjectionMatch): void => {
    if (match.authority.toLocaleUpperCase() !== 'EPSG') {
      setError(text(language,
        '此搜索结果使用非 EPSG 权威机构。后端只返回代码与名称，没有返回可用于定义的 PROJ/WKT 内容，因此不会猜测并填入错误的 EPSG 代码。',
        'This result uses a non-EPSG authority. The backend returned only its code and name, not a PROJ/WKT definition, so the page will not guess an EPSG code.'));
      return;
    }
    setError(undefined);
    setProjectionType('EPSG');
    setProjectionValue(String(match.code));
  };

  const copyWkt = async (): Promise<void> => {
    if (evaluation?.wkt) await navigator.clipboard.writeText(evaluation.wkt);
  };

  return (
    <div className="tools-area tools-projection-area">
      <section className="tools-hero tools-hero-compact">
        <div><span className="tools-kicker">{zh ? '坐标参考系' : 'COORDINATE REFERENCE SYSTEM'}</span><h2>{zh ? '投影定义' : 'Projection definition'}</h2>
          <p>{zh ? '按关键词或位置查询投影，并用 C# 检查 EPSG、PROJ、ESRI、WKT 或 Local 定义。' : 'Search projections by name or location, then validate EPSG, PROJ, ESRI, WKT, or Local definitions with C#.'}</p></div>
        <span className={`tools-runtime-pill ${backend.supports('projection.evaluate') ? 'is-connected' : 'is-unavailable'}`}>{backend.supports('projection.evaluate') ? 'C#' : (zh ? '未接通' : 'Unavailable')}</span>
      </section>

      <div className="tools-two-column">
        <section className="tools-subpanel">
          <div className="tools-subpanel-heading"><div><span className="tools-kicker">01 / {zh ? '搜索' : 'SEARCH'}</span><h3>{zh ? '查找投影' : 'Find a projection'}</h3></div><span className="tools-badge">projection.search</span></div>
          <div className="tools-segmented" role="tablist" aria-label={zh ? '投影搜索方式' : 'Projection search mode'}>
            <button type="button" className={mode === 'search' ? 'is-active' : ''} onClick={() => setMode('search')}>{zh ? '按名称 / EPSG' : 'Name / EPSG'}</button>
            <button type="button" className={mode === 'location' ? 'is-active' : ''} onClick={() => setMode('location')}>{zh ? '按位置' : 'By location'}</button>
          </div>
          {mode === 'search' ? <label className="tools-field"><span>{zh ? '名称、EPSG 代码或关键词' : 'Name, EPSG code, or keyword'}</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder={zh ? '例如 EPSG:4326' : 'For example, EPSG:4326'} onKeyDown={event => { if (event.key === 'Enter') void search(); }} /></label> : (
            <div className="tools-field-grid">
              <label className="tools-field"><span>{zh ? '经度' : 'Longitude'}</span><input inputMode="decimal" value={longitude} onChange={event => setLongitude(event.target.value)} /></label>
              <label className="tools-field"><span>{zh ? '纬度' : 'Latitude'}</span><input inputMode="decimal" value={latitude} onChange={event => setLatitude(event.target.value)} /></label>
            </div>
          )}
          <div className="tools-actions"><button className="tools-button tools-button-primary" type="button" onClick={() => void search()} disabled={busy !== undefined || !backend.supports('projection.search')}>{busy === 'search' ? (zh ? '正在搜索…' : 'Searching…') : (zh ? '搜索投影' : 'Search projections')}</button></div>
          {matches.length > 0 && <div className="tools-projection-results">{matches.map(match => <button type="button" className="tools-projection-result" key={match.id} onClick={() => chooseMatch(match)}>
            <span><strong>{match.name}</strong><small>{match.authority}:{match.code}{match.area ? ` · ${match.area}` : ''}</small></span><b>→</b>
          </button>)}</div>}
          {matches.length === 0 && busy !== 'search' && <p className="tools-muted">{zh ? '搜索结果会显示在此处；选择结果可填入定义。' : 'Search results appear here. Select one to fill the definition.'}</p>}
        </section>

        <section className="tools-subpanel">
          <div className="tools-subpanel-heading"><div><span className="tools-kicker">02 / {zh ? '检查' : 'EVALUATE'}</span><h3>{zh ? '检查投影定义' : 'Evaluate definition'}</h3></div><span className="tools-badge">projection.evaluate</span></div>
          <div className="tools-field-grid">
            <label className="tools-field"><span>{zh ? '类型' : 'Type'}</span><select value={projectionType} onChange={event => setProjectionType(event.target.value)}>{['Local', 'EPSG', 'PROJ', 'ESRI', 'WKT'].map(value => <option key={value} value={value}>{zh && value === 'Local' ? '本地' : value}</option>)}</select></label>
            <label className="tools-field"><span>{zh ? '定义值' : 'Definition value'}</span><input value={projectionValue} onChange={event => setProjectionValue(event.target.value)} placeholder={projectionType === 'EPSG' ? '4326' : ''} /></label>
          </div>
          <div className="tools-actions"><button className="tools-button tools-button-primary" type="button" onClick={() => void evaluate()} disabled={busy !== undefined || !backend.supports('projection.evaluate')}>{busy === 'evaluate' ? (zh ? '正在检查…' : 'Evaluating…') : (zh ? '检查定义' : 'Evaluate definition')}</button></div>
          {evaluation && <div className={`tools-evaluation ${evaluation.isValid ? 'is-valid' : 'is-invalid'}`}>
            <div className="tools-evaluation-title"><span className="tools-state-dot" /><strong>{evaluation.isValid ? (zh ? '定义有效' : 'Valid definition') : (zh ? '定义无效' : 'Invalid definition')}</strong></div>
            <dl className="tools-result-grid"><div><dt>{zh ? '名称' : 'Name'}</dt><dd>{evaluation.name || '—'}</dd></div><div><dt>{zh ? '单位' : 'Units'}</dt><dd>{evaluation.unitName ?? evaluation.units ?? '—'}</dd></div>
              <div><dt>EPSG</dt><dd>{evaluation.epsgCode ?? '—'}</dd></div><div><dt>{zh ? 'UTM 分区' : 'UTM zone'}</dt><dd>{evaluation.utmZone ?? '—'}</dd></div>
              <div><dt>{zh ? '投影坐标系' : 'Projected'}</dt><dd>{evaluation.isProjected == null ? '—' : zh ? (evaluation.isProjected ? '是' : '否') : evaluation.isProjected ? 'Yes' : 'No'}</dd></div><div><dt>{zh ? '垂直坐标系' : 'Vertical'}</dt><dd>{evaluation.isVertical == null ? '—' : zh ? (evaluation.isVertical ? '是' : '否') : evaluation.isVertical ? 'Yes' : 'No'}</dd></div></dl>
            {evaluation.error && <pre className="tools-inline-error">{zh ? '后端返回的检查信息：' : 'Backend evaluation message: '}{evaluation.error}</pre>}
            {evaluation.wkt && <><div className="tools-actions tools-actions-tight"><button className="tools-button tools-button-secondary" type="button" onClick={() => void copyWkt()}>{zh ? '复制 WKT' : 'Copy WKT'}</button></div><pre className="tools-output-preview">{evaluation.wkt}</pre></>}
          </div>}
          {!evaluation && <p className="tools-muted">{zh ? '结果由投影后端返回；此面板不会本地猜测 CRS。' : 'Results come from the projection backend; this panel does not guess a CRS locally.'}</p>}
        </section>
      </div>
      {error && <div className="tools-inline-error tools-page-error" role="alert">{error}</div>}
    </div>
  );
}
