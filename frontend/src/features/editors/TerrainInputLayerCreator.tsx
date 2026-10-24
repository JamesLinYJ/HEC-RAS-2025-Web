import type { EditorPageProps, TerrainTransformCommand } from './model';

const terrainInputKinds: Array<{ kind: NonNullable<TerrainTransformCommand['inputKind']>; title: [string, string]; detail: [string, string] }> = [
  { kind: 'functionalSurface', title: ['函数地形', 'Functional surface'], detail: ['由表达式生成的地形输入', 'Terrain input generated from an expression'] },
  { kind: 'polygonVectorMod', title: ['多边形矢量修改层', 'Polygon vector modification layer'], detail: ['承载源矢量多边形的修改层', 'A modification layer for source vector polygons'] },
  { kind: 'channelVectorMod', title: ['渠道矢量修改层', 'Channel vector modification layer'], detail: ['带渠道线和剖面表的修改层', 'A modification layer with channel lines and profile tables'] },
  { kind: 'simpleCutFill', title: ['简单挖填层', 'Simple cut/fill layer'], detail: ['承载挖填线和属性集的修改层', 'A modification layer for cut/fill lines and property sets'] },
];

/** Creates only input kinds handled by the current C# model.terrainTransform operation. */
export function TerrainInputLayerCreator({ language, canEdit, onTerrainTransform }: Pick<EditorPageProps, 'language' | 'onTerrainTransform'> & { canEdit: boolean }) {
  const zh = language === 'zh';
  return <section className="editor-field-sections editor-terrain-input-actions" aria-label={zh ? '创建地形输入' : 'Create terrain input'}>
    <h4>{zh ? '添加输入图层' : 'Add terrain input'}</h4>
    {onTerrainTransform ? <div className="editor-field-grid">
      {terrainInputKinds.map(({ kind, title, detail }) => <button
        type="button"
        className="editor-button"
        key={kind}
        disabled={!canEdit}
        title={detail[zh ? 0 : 1]}
        onClick={() => void onTerrainTransform({ operation: 'add', inputKind: kind })}
      >{title[zh ? 0 : 1]}</button>)}
    </div> : <small className="editor-field-muted">{!canEdit
      ? zh ? '当前模型快照为只读。' : 'The current model snapshot is read-only.'
      : zh ? '当前编辑上下文未提供地形图层操作。' : 'Terrain layer operations are unavailable in the current editor context.'}</small>}
    <small className="editor-field-muted">{zh
      ? '创建动作只会把 Host 返回的新输入加入工作草稿；文件导入和 USGS 下载尚未接入此编辑回调。'
      : 'Creation adds a Host-returned input to the working draft. File import and USGS download are not connected to this editor callback.'}</small>
  </section>;
}
