import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { QuickRatioCellSizeButton } from './QuickRatioCellSizeButton';
import { gfield } from './geometryText';
import type { GeometryRow } from './geometryTypes';

export interface CartesianPatchQuickEditorProps {
  language: Language;
  row: GeometryRow;
  selectedCount: number;
  canEdit: boolean;
  busy: boolean;
  onResize: (factor: 0.5 | 2) => Promise<void>;
  onSetCombinedCellSize?: (value: string) => Promise<void>;
}

function combinedCellSize(row: GeometryRow): number | undefined {
  const primary = Number(row.values.ConstantSize);
  const secondary = Number(row.values.SecondaryConstantSize);
  if (!Number.isFinite(primary) || !Number.isFinite(secondary) || Math.abs(primary - secondary) >= 1e-9) return undefined;
  return primary;
}

export function CartesianPatchQuickEditor({
  language, row, selectedCount, canEdit, busy, onResize, onSetCombinedCellSize,
}: CartesianPatchQuickEditorProps) {
  const combined = combinedCellSize(row);
  const [value, setValue] = useState(combined == null ? '' : String(combined));
  const valueText = value.trim();
  const numericValue = Number(valueText);

  useEffect(() => setValue(combined == null ? '' : String(combined)), [row.id, row.values.ConstantSize, row.values.SecondaryConstantSize]);

  const apply = (): void => {
    if (!onSetCombinedCellSize || !valueText || !Number.isFinite(numericValue) || numericValue <= 0) return;
    void onSetCombinedCellSize(valueText).catch(() => undefined);
  };

  return <section className="geometry-panel" aria-label={language === 'zh' ? '笛卡尔网格分区快速编辑' : 'Cartesian patch quick editor'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '笛卡尔网格分区尺寸' : 'Cartesian patch sizing'}</h3>
      <p>{language === 'zh' ? `源区域 FID ${row.fid ?? '—'} · 快速缩放只作用于首个选中区域` : `Source region FID ${row.fid ?? '—'} · quick sizing applies to the first selected region`}</p>
    </div></header>
    <div className="geometry-panel-body">
      <label className="geometry-form-field">{gfield(language, 'CombinedCellSize', language === 'zh' ? '合并单元尺寸' : 'Combined cell size')}
        <input type="number" step="any" value={value} disabled={!canEdit || busy || !onSetCombinedCellSize}
          placeholder={combined == null ? (language === 'zh' ? '主、次尺寸不同' : 'Primary and secondary sizes differ') : undefined}
          onChange={(event) => setValue(event.target.value)} />
      </label>
      {combined == null && <div className="geometry-inline-help">{language === 'zh'
        ? `主尺寸 ${row.values.ConstantSize || '—'}；次尺寸 ${row.values.SecondaryConstantSize || '—'}。源控件只在二者相同时显示合并值。`
        : `Primary size ${row.values.ConstantSize || '—'}; secondary size ${row.values.SecondaryConstantSize || '—'}. The source editor shows a combined value only when both match.`}</div>}
      {!onSetCombinedCellSize && <div className="geometry-inline-help">{language === 'zh'
        ? '当前 Host 尚无一次性提交合并尺寸的操作；半尺寸和加倍命令仍可用。'
        : 'The current host has no atomic combined-size edit operation; half and double sizing remain available.'}</div>}
      <button className="geometry-primary" type="button" disabled={!canEdit || busy || !onSetCombinedCellSize || !valueText || !Number.isFinite(numericValue) || numericValue <= 0}
        onClick={apply}>{language === 'zh' ? '应用合并尺寸' : 'Apply combined size'}</button>
    </div>
    <div className="geometry-panel-body geometry-toolbar-group">
      <QuickRatioCellSizeButton language={language} factor={0.5} disabled={!canEdit || busy} onResize={(factor) => { void onResize(factor).catch(() => undefined); }} />
      <QuickRatioCellSizeButton language={language} factor={2} disabled={!canEdit || busy} onResize={(factor) => { void onResize(factor).catch(() => undefined); }} />
    </div>
    {selectedCount > 1 && <div className="geometry-inline-help">{language === 'zh'
      ? '快速缩放会按照源控件作用于首个选中源区域。'
      : 'Quick sizing follows the source editor and applies to the first selected source region.'}</div>}
  </section>;
}

export default CartesianPatchQuickEditor;
