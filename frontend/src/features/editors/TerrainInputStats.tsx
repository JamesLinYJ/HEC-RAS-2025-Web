import type { ModelSnapshot } from './model';
import { editorValueLabel } from './editorLocalization';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** Read-only facts returned by the terrain Host snapshot; no derived raster statistics are invented. */
export function TerrainInputStats({ language, inputs, input }: { language: 'zh' | 'en'; inputs: TerrainInput[]; input: TerrainInput }) {
  const zh = language === 'zh';
  const extent = input.validExtent;
  const failedCount = inputs.filter((candidate) => !!candidate.error).length;
  return <section className="editor-field-sections editor-terrain-stats" aria-label={zh ? '地形输入统计' : 'Terrain input statistics'}>
    <h4>{zh ? '地形输入信息' : 'Terrain input information'}</h4>
    <dl className="editor-readonly-list">
      <dt>{zh ? '当前输入类型' : 'Selected input type'}</dt><dd>{editorValueLabel(language, input.type)}</dd>
      <dt>{zh ? '输入图层总数' : 'Input layer count'}</dt><dd>{inputs.length}</dd>
      <dt>{zh ? '包含诊断错误' : 'Inputs with diagnostics'}</dt><dd>{failedCount}</dd>
      <dt>{zh ? '源文件路径' : 'Source file path'}</dt><dd><code>{input.filename ?? '—'}</code></dd>
      <dt>{zh ? '有效范围' : 'Valid extent'}</dt><dd>{extent ? `${extent.minX}, ${extent.minY} – ${extent.maxX}, ${extent.maxY}` : '—'}</dd>
      {input.overviewCount != null && <><dt>{zh ? '金字塔层级数' : 'Overview count'}</dt><dd>{input.overviewCount}</dd></>}
    </dl>
    {input.overviews?.length ? <details className="editor-source-details">
      <summary>{zh ? '查看 Host 返回的金字塔层级' : 'Show Host-returned overview levels'}</summary>
      <ul>{input.overviews.map((overview, index) => <li key={`${index}:${overview}`}><code>{overview}</code></li>)}</ul>
    </details> : null}
    {input.error && <details className="editor-source-details editor-field-error" open>
      <summary>{zh ? 'Host 原始诊断' : 'Host diagnostic'}</summary>
      <pre>{input.error}</pre>
    </details>}
  </section>;
}
