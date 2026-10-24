import { useState } from 'react';
import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import { GeometryError } from './GeometryError';
import { geometryDiagnosticMessage } from './geometryErrors';
import type { GeometryCulvertSnapshot, GeometryField, GeometryRow } from './geometryTypes';
import { CulvertEditor } from './CulvertEditor';
import { GeometryCulvertBarrelPropertiesPage } from './GeometryCulvertBarrelPropertiesPage';
import { GeometryCulvertOpeningPropertiesPage } from './GeometryCulvertOpeningPropertiesPage';

export interface GeometryCulvertsPageProps {
  language: Language;
  snapshot?: GeometryCulvertSnapshot;
  localBarrels: GeometryRow[];
  localBarrelProperties: GeometryRow[];
  localOpeningProperties: GeometryRow[];
  canEdit: boolean;
  busy: boolean;
  onChange: (featureKind: 'barrel' | 'barrelProperties' | 'openingProperties', row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onCommand?: (featureKind: 'barrel' | 'barrelProperties' | 'openingProperties', row: GeometryRow, field: GeometryField, command: string, value?: string) => void;
  onDelete: (featureKind: 'barrel' | 'barrelProperties' | 'openingProperties', rows: GeometryRow[]) => void;
  onAddBarrel: (coordinates: Array<{ x: number; y: number }>) => Promise<void>;
  onAddPropertySet: (kind: 'barrelProperties' | 'openingProperties') => void;
}

type CulvertSubpage = 'barrels' | 'barrelProperties' | 'openingProperties';

export function GeometryCulvertsPage({ language, snapshot, localBarrels, localBarrelProperties, localOpeningProperties, canEdit, busy, onChange, onCommand, onDelete, onAddBarrel, onAddPropertySet }: GeometryCulvertsPageProps) {
  const [page, setPage] = useState<CulvertSubpage>('barrels');
  const [showAdd, setShowAdd] = useState(false);
  const [coordinates, setCoordinates] = useState({ x1: '', y1: '', x2: '', y2: '' });
  const [addError, setAddError] = useState('');
  const [addBusy, setAddBusy] = useState(false);

  const addBarrel = async (): Promise<void> => {
    const values = Object.values(coordinates).map(Number);
    if (Object.values(coordinates).some((value) => value.trim() === '') || values.some((value) => !Number.isFinite(value))) {
      setAddError(language === 'zh' ? '四个坐标都必须是有限数字。' : 'All four coordinates must be finite numbers.');
      return;
    }
    if (values[0] === values[2] && values[1] === values[3]) {
      setAddError(language === 'zh' ? '起点和终点不能相同。' : 'Start and end points must differ.');
      return;
    }
    setAddBusy(true); setAddError('');
    try {
      await onAddBarrel([{ x: values[0], y: values[1] }, { x: values[2], y: values[3] }]);
      setShowAdd(false); setCoordinates({ x1: '', y1: '', x2: '', y2: '' });
    } catch (error) {
      setAddError(geometryDiagnosticMessage(error));
    } finally { setAddBusy(false); }
  };

  const handleChange = (kind: 'barrel' | 'barrelProperties' | 'openingProperties') => (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => onChange(kind, row, field, value, isLocal);
  const handleDelete = (kind: 'barrel' | 'barrelProperties' | 'openingProperties') => (rows: GeometryRow[]) => onDelete(kind, rows);

  return (
    <div className="geometry-grid">
      <section className="geometry-panel">
        <header className="geometry-panel-heading"><div><h2>{gt(language, 'culverts')}</h2><p>{snapshot?.associatedTerrainName ? `${gt(language, 'terrain')}: ${snapshot.associatedTerrainName}` : `${gt(language, 'terrain')}: ${gt(language, 'noTerrain')}`}</p></div>
          <div className="geometry-toolbar-group">
            <button className="geometry-secondary" disabled={!canEdit || busy} onClick={() => setShowAdd(true)}>{gt(language, 'addBarrel')}</button>
            <button className="geometry-secondary" disabled={!canEdit || busy} onClick={() => onAddPropertySet('barrelProperties')}>{gt(language, 'addBarrelProperties')}</button>
            <button className="geometry-secondary" disabled={!canEdit || busy} onClick={() => onAddPropertySet('openingProperties')}>{gt(language, 'addOpeningProperties')}</button>
          </div>
        </header>
        {snapshot && !snapshot.canEdit && <div className="geometry-local-banner">{gt(language, 'editDisabled')}</div>}
        <nav className="geometry-culvert-subnav" aria-label={gt(language, 'culverts')}>
          {(['barrels', 'barrelProperties', 'openingProperties'] as const).map((item) => <button className="geometry-subtab" key={item} aria-current={page === item ? 'page' : undefined} onClick={() => setPage(item)}>{gt(language, item)}</button>)}
        </nav>
        <div className="geometry-tab-content">
          {page === 'barrels' ? <CulvertEditor language={language} table={snapshot?.barrels} localRows={localBarrels} editable={canEdit && !!snapshot?.canEdit} busy={busy} onChange={handleChange('barrel')} onDelete={handleDelete('barrel')} /> : null}
          {page === 'barrelProperties' ? <GeometryCulvertBarrelPropertiesPage language={language} table={snapshot?.barrelProperties} localRows={localBarrelProperties} canEdit={canEdit && !!snapshot?.canEdit} busy={busy} onChange={handleChange('barrelProperties')} onDelete={handleDelete('barrelProperties')} onAdd={() => onAddPropertySet('barrelProperties')} /> : null}
          {page === 'openingProperties' ? <GeometryCulvertOpeningPropertiesPage language={language} table={snapshot?.openingProperties} localRows={localOpeningProperties} canEdit={canEdit && !!snapshot?.canEdit} busy={busy} onChange={handleChange('openingProperties')} onDelete={handleDelete('openingProperties')} onAdd={() => onAddPropertySet('openingProperties')} onCommand={(row, field, command, value) => onCommand?.('openingProperties', row, field, command, value)} /> : null}
        </div>
      </section>
      <div className="geometry-inline-help">{gt(language, 'dirtyCaption')}</div>
      {showAdd && <div className="geometry-modal-scrim" role="presentation"><section className="geometry-modal" role="dialog" aria-modal="true" aria-labelledby="geometry-add-culvert-title">
        <h3 id="geometry-add-culvert-title">{gt(language, 'addBarrel')}</h3><p>{gt(language, 'choosePolyline')}</p>
        <div className="geometry-modal-fields">{([['x1', 'startX'], ['y1', 'startY'], ['x2', 'endX'], ['y2', 'endY']] as const).map(([id, key]) => <label className="geometry-form-field" key={id}>{gt(language, key)}<input type="number" step="any" value={coordinates[id]} onChange={(event) => setCoordinates((current) => ({ ...current, [id]: event.target.value }))} /></label>)}</div>
        {addError && <GeometryError language={language} error={addError} />}
        <div className="geometry-modal-actions"><button className="geometry-secondary" disabled={addBusy} onClick={() => setShowAdd(false)}>{gt(language, 'cancel')}</button><button className="geometry-primary" disabled={addBusy || !canEdit} onClick={() => void addBarrel()}>{addBusy ? gt(language, 'operationRunning') : gt(language, 'add')}</button></div>
      </section></div>}
    </div>
  );
}

export default GeometryCulvertsPage;
