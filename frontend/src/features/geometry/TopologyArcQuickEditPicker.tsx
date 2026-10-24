import type { Language } from '../../shared/workbench';
import type { GeometryRow } from './geometryTypes';

export interface TopologyArcQuickEditPickerProps {
  language: Language;
  rows: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onResize: (row: GeometryRow, operation: 'halfSize' | 'doubleSize') => void;
}

export function TopologyArcQuickEditPicker({ language, rows, canEdit, busy, onResize }: TopologyArcQuickEditPickerProps) {
  const arc = rows.length === 1 && rows[0].fid != null && rows[0].fid >= 0 ? rows[0] : undefined;
  const sizeFunction = arc?.values.SizeFunction;
  const canResize = !!arc && (sizeFunction === 'Constant' || sizeFunction === 'CountWithBias');
  return (
    <section className="geometry-panel" aria-label={language === 'zh' ? '弧段快速编辑' : 'Arc quick edit'}>
      <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '弧段快速缩放' : 'Arc quick sizing'}</h3><p>{language === 'zh' ? '沿用源编辑器的 1/2 与 ×2 比例。' : 'Uses the source editor’s 1/2 and ×2 ratios.'}</p></div></header>
      {!arc ? <div className="geometry-empty"><strong>{language === 'zh' ? '请只选择一个源弧段。' : 'Select exactly one source arc.'}</strong></div> : !canResize ? (
        <div className="geometry-panel-body">{language === 'zh' ? `当前 SizeFunction 为 ${sizeFunction ?? '未知'}；源快速缩放只处理 Constant 或 CountWithBias。` : `SizeFunction is ${sizeFunction ?? 'unknown'}; source quick sizing handles Constant or CountWithBias.`}</div>
      ) : (
        <div className="geometry-panel-body geometry-toolbar-group">
          <button className="geometry-secondary" disabled={!canEdit || busy} onClick={() => onResize(arc, 'halfSize')}>{language === 'zh' ? '减半' : 'Half size'}</button>
          <button className="geometry-secondary" disabled={!canEdit || busy} onClick={() => onResize(arc, 'doubleSize')}>{language === 'zh' ? '加倍' : 'Double size'}</button>
        </div>
      )}
    </section>
  );
}

export default TopologyArcQuickEditPicker;
