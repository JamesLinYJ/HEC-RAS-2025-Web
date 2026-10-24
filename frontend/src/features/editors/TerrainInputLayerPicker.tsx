import type { EditorPageProps, ModelSnapshot } from './model';
import { editorValueLabel } from './editorLocalization';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** Input selection and ordering, backed by terrainTransform and its returned draft projection. */
export function TerrainInputLayerPicker({ language, inputs, selectedId, names, canEdit, onTerrainTransform }: {
  language: 'zh' | 'en';
  inputs: TerrainInput[];
  selectedId: string;
  names: Record<string, string>;
  canEdit: boolean;
  onTerrainTransform?: EditorPageProps['onTerrainTransform'];
}) {
  const zh = language === 'zh';
  const selectedIndex = inputs.findIndex((input) => input.id === selectedId);
  const run = (command: Parameters<NonNullable<EditorPageProps['onTerrainTransform']>>[0]) => { void onTerrainTransform?.(command); };
  return <>
    <nav className="editor-line-list" aria-label={zh ? '地形输入图层' : 'Terrain input layers'}>
      {inputs.map((input) => <button
        type="button"
        key={input.id}
        className={input.id === selectedId ? 'selected' : ''}
        aria-current={input.id === selectedId ? 'true' : undefined}
        disabled={!canEdit || !onTerrainTransform}
        onClick={() => run({ operation: 'selectInput', itemId: input.id })}
      >
        {names[`${input.scope}/Name`] ?? input.name}
        <small>{editorValueLabel(language, input.type)}{input.error ? (zh ? ' · 有错误' : ' · Error') : ''}</small>
      </button>)}
    </nav>
    {inputs.length > 1 && <div className="editor-button-row" aria-label={zh ? '输入图层顺序' : 'Input layer order'}>
      <button type="button" className="editor-button" disabled={!canEdit || !onTerrainTransform || selectedIndex <= 0} onClick={() => run({ operation: 'reorder', from: selectedIndex, to: selectedIndex - 1 })}>{zh ? '上移所选图层' : 'Move selected layer up'}</button>
      <button type="button" className="editor-button" disabled={!canEdit || !onTerrainTransform || selectedIndex < 0 || selectedIndex >= inputs.length - 1} onClick={() => run({ operation: 'reorder', from: selectedIndex, to: selectedIndex + 1 })}>{zh ? '下移所选图层' : 'Move selected layer down'}</button>
    </div>}
  </>;
}
