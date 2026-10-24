import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { QuickRatioCellSizeButton } from './QuickRatioCellSizeButton';
import { gfield } from './geometryText';
import type { GeometryMeshPatchMutation, GeometryRow } from './geometryTypes';

export interface TrianglePatchQuickEditorProps {
  language: Language;
  row: GeometryRow;
  selectedCount: number;
  canEdit: boolean;
  busy: boolean;
  onMutation: (mutation: GeometryMeshPatchMutation) => Promise<void>;
}

export function TrianglePatchQuickEditor({ language, row, selectedCount, canEdit, busy, onMutation }: TrianglePatchQuickEditorProps) {
  const [values, setValues] = useState({
    ConstantSize: row.values.ConstantSize ?? '',
    SecondaryConstantSize: row.values.SecondaryConstantSize ?? '',
  });

  useEffect(() => setValues({
    ConstantSize: row.values.ConstantSize ?? '',
    SecondaryConstantSize: row.values.SecondaryConstantSize ?? '',
  }), [row.id, row.values.ConstantSize, row.values.SecondaryConstantSize]);

  const applyField = (fieldID: 'ConstantSize' | 'SecondaryConstantSize'): void => {
    const text = values[fieldID].trim();
    const number = Number(text);
    if (row.fid == null || row.fid < 0 || !text || !Number.isFinite(number) || number <= 0) return;
    void onMutation({ operation: 'setPatchField', featureIDs: [row.fid], fieldID, value: text }).catch(() => undefined);
  };
  const resize = (factor: 0.5 | 2): void => {
    if (row.fid == null || row.fid < 0) return;
    void onMutation({ operation: 'resizePatch', featureIDs: [row.fid], value: factor === 0.5 ? '0.5' : '2' }).catch(() => undefined);
  };

  return <section className="geometry-panel" aria-label={language === 'zh' ? '三角网格分区快速编辑' : 'Triangle patch quick editor'}>
    <header className="geometry-panel-heading"><div>
      <h3>{language === 'zh' ? '三角网格分区尺寸' : 'Triangle patch sizing'}</h3>
      <p>{language === 'zh' ? `源区域 FID ${row.fid ?? '—'} · 快速缩放只作用于首个选中区域` : `Source region FID ${row.fid ?? '—'} · quick sizing applies to the first selected region`}</p>
    </div></header>
    <div className="geometry-panel-body geometry-grid two">
      {(['ConstantSize', 'SecondaryConstantSize'] as const).map((fieldID) => {
        const text = values[fieldID].trim();
        const number = Number(text);
        return <div className="geometry-detail-field" key={fieldID}>
          <label className="geometry-form-field">{gfield(language, fieldID, fieldID)}
            <input type="number" step="any" value={values[fieldID]} disabled={!canEdit || busy}
              onChange={(event) => setValues((current) => ({ ...current, [fieldID]: event.target.value }))} />
          </label>
          <button className="geometry-secondary" type="button" disabled={!canEdit || busy || !text || !Number.isFinite(number) || number <= 0}
            onClick={() => applyField(fieldID)}>{language === 'zh' ? '应用该尺寸' : 'Apply this size'}</button>
        </div>;
      })}
    </div>
    <div className="geometry-panel-body geometry-toolbar-group">
      <QuickRatioCellSizeButton language={language} factor={0.5} disabled={!canEdit || busy} onResize={resize} />
      <QuickRatioCellSizeButton language={language} factor={2} disabled={!canEdit || busy} onResize={resize} />
    </div>
    {selectedCount > 1 && <div className="geometry-inline-help">{language === 'zh'
      ? '尺寸输入与快速缩放遵循原控件：修改首个选中源区域；四边形侧尺寸会合并所选区域。'
      : 'Input and quick sizing follow the source editor and modify the first selected source region; Quad side sizing combines all selected regions.'}</div>}
  </section>;
}

export default TrianglePatchQuickEditor;
