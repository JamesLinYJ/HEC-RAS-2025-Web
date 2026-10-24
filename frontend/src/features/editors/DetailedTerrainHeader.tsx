import type { EditorPageProps } from './model';

const terrainNameField = 'TerrainProperties/Terrain_Name';

/** Name and context header bound to the selected source terrain. */
export function DetailedTerrainHeader({ language, snapshot, draft }: Pick<EditorPageProps, 'language' | 'snapshot' | 'draft'>) {
  const name = draft.fields[terrainNameField] ?? snapshot.fields[terrainNameField] ?? '';
  return (
    <div className="editor-schema-heading editor-terrain-header">
      <div>
        <p className="editor-eyebrow">{language === 'zh' ? '地形模型' : 'Terrain model'}</p>
        <h2>{name || (language === 'zh' ? '未命名地形' : 'Unnamed terrain')}</h2>
        <span className="editor-field-muted">{language === 'zh' ? '输入图层' : 'Input layers'}</span>
      </div>
      <details className="editor-source-details">
        <summary>{language === 'zh' ? '来源控件' : 'Source control'}</summary>
        <code>DetailedTerrainHeader · TerrainLayersEditor</code>
      </details>
    </div>
  );
}
