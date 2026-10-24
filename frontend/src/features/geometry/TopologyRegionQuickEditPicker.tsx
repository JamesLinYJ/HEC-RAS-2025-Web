import type { Language } from '../../shared/workbench';
import type { GeometryRow } from './geometryTypes';
import { QuickRatioCellSizeButton } from './QuickRatioCellSizeButton';

export interface TopologyRegionQuickEditPickerProps {
  language: Language;
  rows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onResize: (row: GeometryRow, operation: 'halfSize' | 'doubleSize', axis?: 'NorthSouth' | 'EastWest') => void;
}

function patchName(language: Language, type: string): string {
  if (language !== 'zh') return type;
  const names: Record<string, string> = { Quad: '四边形', TrianglePaver: '三角网格', Cartesian: '笛卡尔网格', OneD: '一维', Empty: '空区域' };
  return names[type] ?? type;
}

export function TopologyRegionQuickEditPicker({ language, rows, canEdit, busy, onResize }: TopologyRegionQuickEditPickerProps) {
  const region = rows.length === 1 && rows[0].fid != null && rows[0].fid >= 0 ? rows[0] : undefined;
  const patchType = region?.values.PatchType ?? '';
  const simple = patchType === 'TrianglePaver' || patchType === 'Cartesian';
  const quad = patchType === 'Quad';
  const canResize = !!region && (simple || quad);
  const resize = (factor: 0.5 | 2, axis?: 'NorthSouth' | 'EastWest'): void =>
    onResize(region!, factor === 0.5 ? 'halfSize' : 'doubleSize', axis);

  return (
    <section className="geometry-panel" aria-label={language === 'zh' ? '区域快速编辑' : 'Region quick edit'}>
      <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '区域快速缩放' : 'Region quick sizing'}</h3><p>{region ? `${language === 'zh' ? '网格类型' : 'Patch type'}: ${patchName(language, patchType)}` : (language === 'zh' ? '请只选择一个源区域。' : 'Select exactly one source region.')}</p></div></header>
      {!region ? <div className="geometry-empty"><strong>{language === 'zh' ? '快速缩放需要一个源区域。' : 'Quick sizing requires one source region.'}</strong></div> : !canResize ? (
        <div className="geometry-panel-body">{language === 'zh' ? '源快速尺寸命令只支持 TrianglePaver、Cartesian 或 Quad 区域。' : 'Source quick sizing supports TrianglePaver, Cartesian, and Quad patches.'}</div>
      ) : (
        <div className="geometry-panel-body">
          {quad ? <div className="geometry-toolbar-group" style={{ marginBottom: 10 }}>
            <QuickRatioCellSizeButton language={language} factor={0.5} axis="NorthSouth" disabled={!canEdit || busy} onResize={(factor) => resize(factor, 'NorthSouth')} />
            <QuickRatioCellSizeButton language={language} factor={2} axis="NorthSouth" disabled={!canEdit || busy} onResize={(factor) => resize(factor, 'NorthSouth')} />
          </div> : null}
          {!quad ? <div className="geometry-toolbar-group" style={{ width: '100%' }}>
            <QuickRatioCellSizeButton language={language} factor={0.5} disabled={!canEdit || busy} onResize={(factor) => resize(factor)} />
            <QuickRatioCellSizeButton language={language} factor={2} disabled={!canEdit || busy} onResize={(factor) => resize(factor)} />
          </div> : null}
          {quad ? <div className="geometry-toolbar-group" style={{ marginTop: 10 }}>
            <QuickRatioCellSizeButton language={language} factor={0.5} axis="EastWest" disabled={!canEdit || busy} onResize={(factor) => resize(factor, 'EastWest')} />
            <QuickRatioCellSizeButton language={language} factor={2} axis="EastWest" disabled={!canEdit || busy} onResize={(factor) => resize(factor, 'EastWest')} />
          </div> : null}
        </div>
      )}
    </section>
  );
}

export default TopologyRegionQuickEditPicker;
