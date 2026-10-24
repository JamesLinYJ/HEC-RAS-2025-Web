import { useMemo } from 'react';
import type { Language } from '../../../shared/workbench';
import type { WilcockChart, WilcockChartPoint, WilcockChartSeries, WilcockSourceTable, WilcockWorkspaceSnapshot } from './wilcockTypes';

const issueMessages: Record<string, string> = {
  'Enter a valid number.': '请输入有效数值。',
  'The input arrays do not have the required shape.': '输入数组形状不符合后端要求。',
  'The calculator could not produce a result from these inputs.': '当前输入未能生成计算结果。',
  'Some calculated values are outside the finite numeric range.': '计算结果超出有限数值范围。',
  'Valid BTM and STAB inputs are required to import a case.': '导入工况需要有效的 BTM 和 STAB 输入。',
  'The BTM does not contain grain diameter or surface transport data to import.': 'BTM 没有可导入的粒径或表面输沙数据。',
};

function titleText(value: string, language: Language): string {
  if (language !== 'zh') return value;
  const translations: Record<string, string> = {
    'SBTM distribution': 'SBTM 粒径分布输入',
    'ISBTM distribution': 'ISBTM 粒径分布输入',
    'STAB case inputs': 'STAB 工况输入',
    'STAB grain-size distributions': 'STAB 粒径分布输入',
    'STATE grain-size distributions': 'STATE 粒径分布输入',
    'STATE transport rates': 'STATE 输沙率输入',
    'STATE slopes': 'STATE 坡度输入',
    'SBTM output statistics': 'SBTM 后端计算结果',
    'ISBTM output and convergence': 'ISBTM 后端计算结果与收敛信息',
    'STAB output statistics': 'STAB 后端统计结果',
    'STATE output statistics': 'STATE 后端统计结果',
    'All STAB calculated values': 'STAB 全部后端计算值',
    'All STATE calculated values': 'STATE 全部后端计算值',
    'Grain size (mm)': '粒径 (mm)',
    'Cumulative finer (%)': '累积细粒含量 (%)',
    'Percent finer': '细粒百分比',
  };
  return translations[value] ?? value;
}

function colorStyle(colorARGB: string): { color: string; opacity: number } {
  const match = /^#([a-f\d]{8})$/i.exec(colorARGB);
  if (!match) return { color: colorARGB, opacity: 1 };
  const value = match[1]!;
  const alpha = Number.parseInt(value.slice(0, 2), 16) / 255;
  return { color: `#${value.slice(2)}`, opacity: alpha };
}

function pathFor(series: WilcockChartSeries, bounds: { minX: number; maxX: number; minY: number; maxY: number }): string {
  const points: string[] = [];
  let nextIsMove = true;
  for (const point of series.points) {
    if (typeof point.x !== 'number' || typeof point.y !== 'number' || !Number.isFinite(point.x) || !Number.isFinite(point.y)) {
      nextIsMove = true;
      continue;
    }
    const x = 24 + ((point.x - bounds.minX) / (bounds.maxX - bounds.minX)) * 592;
    const y = 178 - ((point.y - bounds.minY) / (bounds.maxY - bounds.minY)) * 154;
    points.push(`${nextIsMove ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`);
    nextIsMove = false;
  }
  return points.join(' ');
}

function ChartView({ chart, language }: { chart: WilcockChart; language: Language }) {
  const points = chart.series.flatMap(series => series.points).filter(
    (point): point is WilcockChartPoint & { x: number; y: number } =>
      typeof point.x === 'number' && Number.isFinite(point.x) && typeof point.y === 'number' && Number.isFinite(point.y),
  );
  const bounds = useMemo(() => {
    if (points.length === 0) return { minX: 0, maxX: 1, minY: 0, maxY: 1 };
    const xValues = points.map(point => point.x);
    const yValues = points.map(point => point.y);
    const minX = Math.min(...xValues);
    const maxX = Math.max(...xValues);
    const minY = Math.min(...yValues);
    const maxY = Math.max(...yValues);
    const xPadding = minX === maxX ? Math.max(Math.abs(minX) * 0.05, 1) : 0;
    const yPadding = minY === maxY ? Math.max(Math.abs(minY) * 0.05, 1) : 0;
    return { minX: minX - xPadding, maxX: maxX + xPadding, minY: minY - yPadding, maxY: maxY + yPadding };
  }, [points]);

  return <figure className="hydraulics-chart">
    <figcaption><strong>{titleText(chart.titleKey, language)}</strong><small>{chart.titleKey}</small></figcaption>
    {points.length === 0
      ? <p className="hydraulics-muted">没有可绘制的数据点。</p>
      : <div className="hydraulics-chart-scroll"><svg viewBox="0 0 640 220" role="img" aria-label={`${titleText(chart.titleKey, language)} · ${titleText(chart.xAxisKey, language)} · ${titleText(chart.yAxisKey, language)}`}>
        <line x1="24" y1="178" x2="616" y2="178" className="hydraulics-chart-axis" />
        <line x1="24" y1="24" x2="24" y2="178" className="hydraulics-chart-axis" />
        {chart.series.map((series, index) => {
          const stroke = colorStyle(series.colorARGB);
          const path = pathFor(series, bounds);
          return path ? <path key={`${series.labelKey}:${series.caseIndex ?? 'all'}:${index}`} d={path} fill="none" stroke={stroke.color} strokeOpacity={stroke.opacity} strokeWidth={Math.max(series.strokeWidth, 1)} /> : null;
        })}
        <text x="320" y="210" textAnchor="middle" className="hydraulics-chart-label">{titleText(chart.xAxisKey, language)}</text>
        <text x="26" y="16" className="hydraulics-chart-label">{titleText(chart.yAxisKey, language)}</text>
      </svg></div>}
    <ul className="hydraulics-chart-legend">{chart.series.map((series, index) => <li key={`${series.labelKey}:${series.caseIndex ?? 'all'}:${index}`}>
      <span style={{ background: colorStyle(series.colorARGB).color, opacity: colorStyle(series.colorARGB).opacity }} />
      {titleText(series.labelKey, language)}{series.caseIndex === null || series.caseIndex === undefined ? '' : ` · ${language === 'zh' ? '工况 ' : 'Case '}${series.caseIndex + 1}`}
    </li>)}</ul>
  </figure>;
}

function ResultTable({ table, language }: { table: WilcockSourceTable; language: Language }) {
  return <details className="hydraulics-result-table" open>
    <summary><span>{titleText(table.titleKey, language)}</span><code>{table.id}</code></summary>
    <div className="hydraulics-table-scroll"><table className="hydraulics-table">
      <thead><tr>{table.columns.map(column => <th key={column.id}>{titleText(column.titleKey, language)} <code>{column.id}</code></th>)}</tr></thead>
      <tbody>{table.rows.map(row => <tr key={row.id}>
        {row.cells.map((cell, index) => {
          const rowTitle = index === 0 && row.titleKey ? titleText(row.titleKey, language) : undefined;
          return <td key={`${row.id}:${table.columns[index]?.id ?? index}`}>
            {cell || rowTitle || ''}
            {cell && rowTitle && <small className="hydraulics-row-title"> · {rowTitle}</small>}
            {rowTitle && rowTitle !== row.titleKey && <code className="hydraulics-source-title">{row.titleKey}</code>}
          </td>;
        })}
      </tr>)}</tbody>
    </table></div>
  </details>;
}

export function WilcockResults({ snapshot, section, language, stale, chartID }: {
  snapshot: WilcockWorkspaceSnapshot | null;
  section: 'btm' | 'stab' | 'state';
  language: Language;
  stale: boolean;
  chartID?: string;
}) {
  if (!snapshot) return <section className="hydraulics-results"><h3>计算结果</h3><p className="hydraulics-muted">尚未加载结果。</p></section>;
  const modeSection = section === 'btm' ? (snapshot.btmMode === 'ISBTM' ? 'isbtm' : 'sbtm') : section;
  const errorKey = section === 'btm'
    ? snapshot.btmMode === 'ISBTM' ? snapshot.isbtmErrorKey : snapshot.sbtmErrorKey
    : section === 'stab' ? snapshot.stabErrorKey : snapshot.stateErrorKey;
  const resultObject = section === 'btm'
    ? snapshot.btmMode === 'ISBTM' ? snapshot.isbtmResults : snapshot.sbtmResults
    : section === 'stab' ? snapshot.stabResults : snapshot.stateResults;
  const tables = snapshot.tables.filter(table => table.section === section && table.role === 'output');
  const charts = snapshot.charts.filter(chart => chart.section === section && (!chartID || chart.id === chartID));

  return <section className="hydraulics-results" aria-label={language === 'zh' ? '计算结果' : 'Calculation results'}>
    <div className="hydraulics-section-title"><div><h3>后端计算结果</h3><p>{section.toUpperCase()} · {modeSection.toUpperCase()}</p></div>
      {stale && <span className="hydraulics-stale-tag">结果对应上一次已完成的输入</span>}
    </div>
    {errorKey && <p className="hydraulics-warning" role="status">{issueMessages[errorKey] ?? errorKey} <code>{errorKey}</code></p>}
    {snapshot.inputIssues.filter(issue => issue.section === (section === 'btm' ? 'btm' : section)).map((issue, index) => <p className="hydraulics-warning" role="status" key={`${issue.path}:${index}`}>
      {issue.path}：{issueMessages[issue.messageKey] ?? issue.messageKey} <code>{issue.messageKey}</code>
    </p>)}
    {resultObject && !errorKey && <p className="hydraulics-success" role="status">C# 服务已返回 {section === 'btm' ? modeSection.toUpperCase() : section.toUpperCase()} 计算数据。</p>}
    {!resultObject && !errorKey && <p className="hydraulics-muted">后端尚未返回该区段的结果对象。</p>}
    {charts.map(chart => <ChartView key={chart.id} chart={chart} language={language} />)}
    {tables.map(table => <ResultTable key={table.id} table={table} language={language} />)}
    <details className="hydraulics-raw-receipt">
      <summary>计算详情</summary>
      <pre>{JSON.stringify(snapshot, null, 2)}</pre>
    </details>
  </section>;
}
