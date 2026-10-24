import type { EditorPageProps, ModelSnapshot } from './model';
import { editorValueLabel } from './editorLocalization';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** Selected-input header, including its editable source name and returned metadata. */
export function DetailedTerrainInputHeader({ language, input, canEdit, value, onNameChange, onRemove }: {
  language: 'zh' | 'en';
  input: TerrainInput;
  canEdit: boolean;
  value: string;
  onNameChange: (fieldID: string, value: string) => void;
  onRemove?: EditorPageProps['onTerrainTransform'];
}) {
  const fieldID = `${input.scope}/Name`;
  const extent = input.validExtent;
  return (
    <header className="editor-terrain-input-header">
      <div className="editor-terrain-table-heading">
        <div>
          <p className="editor-eyebrow">{language === 'zh' ? '当前输入' : 'Selected input'}</p>
          <h3>{value || input.name}</h3>
        </div>
        {onRemove && <button type="button" className="editor-button" disabled={!canEdit} onClick={() => void onRemove({ operation: 'remove', itemId: input.id })}>
          {language === 'zh' ? '从草稿移除' : 'Remove from draft'}
        </button>}
      </div>
      <div className="editor-field-grid">
        <label className="editor-field" htmlFor="terrain-input-name">
          <span>{language === 'zh' ? '输入名称' : 'Input name'}</span>
          <input id="terrain-input-name" type="text" value={value} disabled={!canEdit} onChange={(event) => onNameChange(fieldID, event.currentTarget.value)} />
        </label>
      </div>
      <dl className="editor-readonly-list">
        <dt>{language === 'zh' ? '输入类型' : 'Input type'}</dt><dd>{editorValueLabel(language, input.type)}</dd>
        <dt>{language === 'zh' ? '源文件' : 'Source file'}</dt><dd>{input.filename ?? '—'}</dd>
        <dt>{language === 'zh' ? '有效范围' : 'Valid extent'}</dt><dd>{extent ? `${extent.minX}, ${extent.minY} – ${extent.maxX}, ${extent.maxY}` : '—'}</dd>
        {input.error && <><dt>{language === 'zh' ? '状态' : 'Status'}</dt><dd className="editor-field-error"><details><summary>{language === 'zh' ? '错误详情' : 'Error details'}</summary><pre>{input.error}</pre></details></dd></>}
      </dl>
      {input.overviews?.length ? <details className="editor-source-details">
        <summary>{language === 'zh' ? `金字塔层级 (${input.overviewCount ?? input.overviews.length})` : `Raster overviews (${input.overviewCount ?? input.overviews.length})`}</summary>
        <ul>{input.overviews.map((overview) => <li key={overview}>{overview}</li>)}</ul>
      </details> : null}
    </header>
  );
}
