import { useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { topologySourceRows } from './geometryTopologySelection';
import { QuickChoiceField, QuickNumberField } from './TopologyQuickFields';
import { QuickRatioCellSizeButton } from './QuickRatioCellSizeButton';
import type { GeometryRow, GeometryTopologyQuickEdit, GeometryTopologySnapshot } from './geometryTypes';

export interface TopologyRegionQuickEditPickerProps {
  language: Language;
  rows: GeometryRow[];
  snapshot?: GeometryTopologySnapshot;
  canEdit: boolean;
  busy: boolean;
  onEdit?: (row: GeometryRow, edit: GeometryTopologyQuickEdit) => Promise<void>;
}

export function TopologyRegionQuickEditPicker({ language, rows, snapshot, canEdit, busy, onEdit }: TopologyRegionQuickEditPickerProps) {
  const [pending, setPending] = useState(false);
  const inFlight = useRef(false);
  const sourceRows = snapshot && rows.length === 1 ? topologySourceRows(snapshot, 'region', rows) : [];
  const region = sourceRows.length === 1 ? snapshot?.regions.rows.find((row) => row.id === sourceRows[0].id && row.fid === sourceRows[0].fid) : undefined;
  const fields = snapshot?.regions.editableFields ?? [];
  const field = (id: string) => fields.find((item) => item.id === id);
  const patchType = region?.values.PatchType ?? '';
  const simple = patchType === 'TrianglePaver' || patchType === 'Cartesian';
  const quad = patchType === 'Quad';
  const disabled = !region || !canEdit || !snapshot?.canEdit || busy || pending || !onEdit;
  const run = async (edit: GeometryTopologyQuickEdit): Promise<void> => {
    if (!region || !onEdit || disabled || inFlight.current) return;
    inFlight.current = true; setPending(true);
    try { await onEdit(region, edit); } finally { inFlight.current = false; setPending(false); }
  };
  const resize = (factor: 0.5 | 2, axis?: 'NorthSouth' | 'EastWest'): void => {
    void run({ operation: factor === 0.5 ? 'halfSize' : 'doubleSize', ...(axis ? { axis } : {}) }).catch(() => undefined);
  };
  const sizeField = field('ConstantSize');

  return <section className="geometry-panel topology-quick-edit" aria-label={language === 'zh' ? '区域快速编辑' : 'Region quick edit'}>
    <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '区域快速编辑' : 'Region quick edit'}</h3>
      <p>{region ? `${language === 'zh' ? '源区域' : 'Source region'} FID ${region.fid}` : (language === 'zh' ? '请在地图或表格中选择一个源区域。' : 'Select one source region on the map or in the table.')}</p>
    </div></header>
    {!region ? <div className="geometry-panel-body topology-quick-edit__hint">{rows.length > 1
      ? (language === 'zh' ? '已选择多个要素；快速编辑一次只作用于一个源区域。' : 'Multiple features are selected; quick edit applies to one source region at a time.')
      : (language === 'zh' ? '请选择唯一的源区域。' : 'Select one unambiguous source region.')}</div> :
      <div className="geometry-panel-body topology-quick-edit__body">
        <div className="topology-quick-edit__fields">
          <QuickChoiceField key={`${region.id}:PatchType`} language={language} label={language === 'zh' ? '网格类型' : 'Patch type'}
            field={field('PatchType')} value={patchType} disabled={disabled}
            onCommit={(value) => run({ operation: 'setField', fieldID: 'PatchType', value })} />
          <QuickNumberField key={`${region.id}:ConstantSize`} language={language} label={language === 'zh' ? '固定尺寸' : 'Constant size'}
            field={sizeField} value={region.values.ConstantSize ?? ''} kind="region-size" disabled={disabled}
            onCommit={(value) => run({ operation: 'setField', fieldID: 'ConstantSize', value })} />
        </div>
        {simple || quad ? <>
          {quad && <div className="topology-quick-edit__sizes">
            <QuickRatioCellSizeButton language={language} factor={0.5} axis="NorthSouth" disabled={disabled || !sizeField || !!sizeField.readOnly} onResize={(factor) => resize(factor, 'NorthSouth')} />
            <QuickRatioCellSizeButton language={language} factor={2} axis="NorthSouth" disabled={disabled || !sizeField || !!sizeField.readOnly} onResize={(factor) => resize(factor, 'NorthSouth')} />
          </div>}
          {simple && <div className="topology-quick-edit__sizes">
            <QuickRatioCellSizeButton language={language} factor={0.5} disabled={disabled || !sizeField || !!sizeField.readOnly} onResize={resize} />
            <QuickRatioCellSizeButton language={language} factor={2} disabled={disabled || !sizeField || !!sizeField.readOnly} onResize={resize} />
          </div>}
          {quad && <div className="topology-quick-edit__sizes">
            <QuickRatioCellSizeButton language={language} factor={0.5} axis="EastWest" disabled={disabled || !sizeField || !!sizeField.readOnly} onResize={(factor) => resize(factor, 'EastWest')} />
            <QuickRatioCellSizeButton language={language} factor={2} axis="EastWest" disabled={disabled || !sizeField || !!sizeField.readOnly} onResize={(factor) => resize(factor, 'EastWest')} />
          </div>}
        </> : <div className="topology-quick-edit__hint">{language === 'zh' ? '当前网格类型不支持快速缩放。' : 'Quick sizing is unavailable for this patch type.'}</div>}
      </div>}
  </section>;
}

export default TopologyRegionQuickEditPicker;
