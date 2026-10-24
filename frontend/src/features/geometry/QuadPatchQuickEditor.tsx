import type { Language } from '../../shared/workbench';
import { QuadPatchQuickEditorDimensionEditor } from './QuadPatchQuickEditorDimensionEditor';
import type { GeometryMeshPatchMutation, GeometryMeshPatchSizing, GeometryMeshSnapshot } from './geometryTypes';

export interface QuadPatchQuickEditorProps {
  language: Language;
  snapshot: GeometryMeshSnapshot;
  sizing: GeometryMeshPatchSizing;
  canEdit: boolean;
  busy: boolean;
  onMutation: (mutation: GeometryMeshPatchMutation) => Promise<void>;
}

function sameIDs(left: number[], right: number[]): boolean {
  return left.length === right.length && left.every((id, index) => id === right[index]);
}

function isCurrentSideSelection(snapshot: GeometryMeshSnapshot, sizing: GeometryMeshPatchSizing, dimension: GeometryMeshPatchSizing['dimensions'][number]): boolean {
  const side = dimension.sideArcSelection;
  return !!side
    && side.sourceRevision === snapshot.revision
    && side.topologyFingerprint === snapshot.topologyFingerprint
    && side.axis === dimension.id
    && sameIDs(side.selectedRegionIDs, snapshot.selectedRegionIDs)
    && sameIDs(sizing.selectedRegionIDs, snapshot.selectedRegionIDs)
    && new Set(side.arcFeatureIDs).size === side.arcFeatureIDs.length
    && side.arcFeatureIDs.every((fid) => Number.isInteger(fid) && fid >= 0);
}

export function QuadPatchQuickEditor({ language, snapshot, sizing, canEdit, busy, onMutation }: QuadPatchQuickEditorProps) {
  const dimensions = sizing.dimensions.filter((dimension) => dimension.id === 'NorthSouth' || dimension.id === 'EastWest');
  return <section className="geometry-panel" aria-label={language === 'zh' ? '四边形网格分区尺寸编辑' : 'Quad patch sizing editor'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '四边形分区尺寸' : 'Quad patch sizing'}</h3>
      <p>{language === 'zh'
        ? `按所选源区域合并南北侧与东西侧弧段；${snapshot.selectedRegionIDs.length} 个区域`
        : `Side arcs are combined across ${snapshot.selectedRegionIDs.length} selected source region(s).`}</p>
    </div></header>
    {!dimensions.length ? <div className="geometry-empty"><strong>{language === 'zh' ? '当前快照没有可编辑的四边形尺寸。' : 'The current snapshot has no editable Quad dimensions.'}</strong></div> : (
      <div className="geometry-grid two">
        {dimensions.map((dimension) => <QuadPatchQuickEditorDimensionEditor key={dimension.id} language={language}
          dimension={dimension} selectedRegionIDs={snapshot.selectedRegionIDs} canEdit={canEdit} busy={busy}
          sideArcSelectionIsCurrent={isCurrentSideSelection(snapshot, sizing, dimension)} onMutation={onMutation} />)}
      </div>
    )}
  </section>;
}

export default QuadPatchQuickEditor;
