import type { EditorPageProps, TerrainTransformCommand } from './model';

const terrainInputKinds: Array<{ kind: NonNullable<TerrainTransformCommand['inputKind']>; title: [string, string] }> = [
  { kind: 'functionalSurface', title: ['函数地形', 'Functional surface'] },
  { kind: 'polygonVectorMod', title: ['多边形矢量修改层', 'Polygon vector modification layer'] },
  { kind: 'channelVectorMod', title: ['渠道矢量修改层', 'Channel vector modification layer'] },
  { kind: 'simpleCutFill', title: ['简单挖填层', 'Simple cut/fill layer'] },
];

/** Type-specific add actions supported by model.terrainTransform. */
export function TerrainAddControl({ language, canEdit, onTerrainTransform }: Pick<EditorPageProps, 'language' | 'onTerrainTransform'> & { canEdit: boolean }) {
  if (!onTerrainTransform) return <small className="editor-field-muted">{!canEdit
    ? language === 'zh' ? '当前模型快照为只读。' : 'The current model snapshot is read-only.'
    : language === 'zh' ? '当前编辑上下文未提供地形图层操作。' : 'Terrain layer operations are unavailable in the current editor context.'}</small>;
  return <div className="editor-field-sections editor-terrain-input-actions">
    <h4>{language === 'zh' ? '添加输入图层' : 'Add terrain input'}</h4>
    <div className="editor-button-row">
      {terrainInputKinds.map(({ kind, title }) => <button type="button" className="editor-button" key={kind} disabled={!canEdit} onClick={() => void onTerrainTransform({ operation: 'add', inputKind: kind })}>
        {title[language === 'zh' ? 0 : 1]}
      </button>)}
    </div>
    <small className="editor-field-muted">{language === 'zh' ? '文件和 USGS 导入尚未接入当前地形编辑回调。' : 'File and USGS imports are not connected to the current terrain editor callback.'}</small>
  </div>;
}
