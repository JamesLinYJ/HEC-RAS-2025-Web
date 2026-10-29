import { useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { topologySourceRows } from './geometryTopologySelection';
import { QuickChoiceField, QuickNumberField } from './TopologyQuickFields';
import type { GeometryRow, GeometryTopologyQuickEdit, GeometryTopologySnapshot } from './geometryTypes';

export interface TopologyArcQuickEditPickerProps {
  language: Language;
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onEdit?: (row: GeometryRow, edit: GeometryTopologyQuickEdit) => Promise<void>;
}

export function TopologyArcQuickEditPicker({ language, rows, snapshot, canEdit, busy, onEdit }: TopologyArcQuickEditPickerProps) {
  const [pending, setPending] = useState(false);
  const inFlight = useRef(false);
  const sourceRows = snapshot && rows.length === 1 ? topologySourceRows(snapshot, 'arc', rows) : [];
  const arc = sourceRows.length === 1 ? snapshot?.arcs.rows.find((row) => row.id === sourceRows[0].id && row.fid === sourceRows[0].fid) : undefined;
  const fields = snapshot?.arcs.editableFields ?? [];
  const field = (id: string) => fields.find((item) => item.id === id);
  const sizeFunction = arc?.values.SizeFunction ?? '';
  const canResize = sizeFunction === 'Constant' || sizeFunction === 'CountWithBias';
  const sizingField = field(sizeFunction === 'Constant' ? 'ConstantSize' : 'PointCount');
  const disabled = !arc || !canEdit || !snapshot?.canEdit || busy || pending || !onEdit;
  const run = async (edit: GeometryTopologyQuickEdit): Promise<void> => {
    if (!arc || !onEdit || disabled || inFlight.current) return;
    inFlight.current = true; setPending(true);
    try { await onEdit(arc, edit); } finally { inFlight.current = false; setPending(false); }
  };

  return <section className="geometry-panel topology-quick-edit" aria-label={language === 'zh' ? '弧段快速编辑' : 'Arc quick edit'}>
    <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '弧段快速编辑' : 'Arc quick edit'}</h3>
      <p>{arc ? `${language === 'zh' ? '源弧段' : 'Source arc'} FID ${arc.fid}` : (language === 'zh' ? '请在地图或表格中选择一个源弧段。' : 'Select one source arc on the map or in the table.')}</p>
    </div></header>
    {!arc ? <div className="geometry-panel-body topology-quick-edit__hint">{rows.length > 1
      ? (language === 'zh' ? '已选择多个要素；快速编辑一次只作用于一个源弧段。' : 'Multiple features are selected; quick edit applies to one source arc at a time.')
      : (language === 'zh' ? '请选择唯一的源弧段。' : 'Select one unambiguous source arc.')}</div> :
      <div className="geometry-panel-body topology-quick-edit__body">
        <div className="topology-quick-edit__fields">
          <QuickChoiceField key={`${arc.id}:SizeFunction`} language={language} label={language === 'zh' ? '尺寸函数' : 'Size function'}
            field={field('SizeFunction')} value={sizeFunction} disabled={disabled}
            onCommit={(value) => run({ operation: 'setField', fieldID: 'SizeFunction', value })} />
          <div hidden={sizeFunction !== 'Constant'}>
            <QuickNumberField key={`${arc.id}:ConstantSize`} language={language} label={language === 'zh' ? '固定尺寸' : 'Constant size'}
              field={field('ConstantSize')} value={arc.values.ConstantSize ?? ''} kind="arc-size" disabled={disabled}
              onCommit={(value) => run({ operation: 'setField', fieldID: 'ConstantSize', value })} />
          </div>
          <div hidden={sizeFunction !== 'CountWithBias'}>
            <QuickNumberField key={`${arc.id}:PointCount`} language={language} label={language === 'zh' ? '点数' : 'Point count'}
              field={field('PointCount')} value={arc.values.PointCount ?? ''} kind="arc-count" disabled={disabled}
              onCommit={(value) => run({ operation: 'setField', fieldID: 'PointCount', value })} />
          </div>
        </div>
        {canResize ? <div className="topology-quick-edit__sizes">
          <button className="geometry-secondary" type="button" disabled={disabled || !sizingField || !!sizingField.readOnly}
            onClick={() => { void run({ operation: 'halfSize' }).catch(() => undefined); }}>{language === 'zh' ? '减半' : 'Half size'}</button>
          <button className="geometry-secondary" type="button" disabled={disabled || !sizingField || !!sizingField.readOnly}
            onClick={() => { void run({ operation: 'doubleSize' }).catch(() => undefined); }}>{language === 'zh' ? '加倍' : 'Double size'}</button>
        </div> : <div className="topology-quick-edit__hint">{language === 'zh' ? '请选择可调整尺寸的函数。' : 'Choose a size function to adjust sizing.'}</div>}
      </div>}
  </section>;
}

export default TopologyArcQuickEditPicker;
