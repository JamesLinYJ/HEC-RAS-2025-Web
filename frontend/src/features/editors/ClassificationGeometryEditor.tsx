import { useEffect, useRef, useState } from 'react';
import { message } from '../../shared/workbench';
import type { ClassificationGeometryMutation, EditorPageProps } from './model';
import { ClassificationPolygonCanvas, type ClassificationPolygonMapFeature } from './ClassificationVisualizationOptions';

const selectionField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';
const polygonClassField = 'PolygonClassificationInputProperties/ClassifierMod_Classification';

type ClassificationData = NonNullable<EditorPageProps['snapshot']['classification']>;
type ClassificationInput = ClassificationData['inputs'][number];
type PolygonFeature = ClassificationInput['polygons'][number];
type PolygonVertex = NonNullable<ClassificationGeometryMutation['vertices']>[number];
type ClipboardPolygon = NonNullable<ClassificationGeometryMutation['features']>[number];

function isPolygonInput(input: ClassificationInput): boolean {
  return !input.tableId && (input.type === 'PolyClassifier' || !input.canRemove || input.polygons.length > 0 || /polygon/i.test(input.type));
}

function parseVertices(text: string, hasZ: boolean, hasM: boolean): PolygonVertex[] | null {
  const lines = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const expectedValues = 2 + Number(hasZ) + Number(hasM);
  const vertices: PolygonVertex[] = [];
  for (const line of lines) {
    const values = line.split(/[\s,;]+/).filter(Boolean);
    if (values.length !== expectedValues) return null;
    const x = Number(values[0]);
    const y = Number(values[1]);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    const vertex: PolygonVertex = { x, y };
    let ordinate = 2;
    if (hasZ) {
      const z = values[ordinate++];
      if (!Number.isFinite(Number(z))) return null;
      vertex.z = z;
    }
    if (hasM) {
      const m = values[ordinate];
      if (!Number.isFinite(Number(m))) return null;
      vertex.m = m;
    }
    vertices.push(vertex);
  }
  if (vertices.length < 3 || new Set(vertices.map((vertex) => `${vertex.x}\u0000${vertex.y}`)).size < 3) return null;
  let doubledArea = 0;
  for (let index = 0; index < vertices.length; index++) {
    const current = vertices[index];
    const next = vertices[(index + 1) % vertices.length];
    doubledArea += current.x * next.y - next.x * current.y;
  }
  return Number.isFinite(doubledArea) && doubledArea !== 0 ? vertices : null;
}

function verticesToText(vertices: PolygonFeature['vertices'], hasZ: boolean, hasM: boolean): string {
  return vertices.map((vertex) => {
    const values = [String(vertex.x), String(vertex.y)];
    if (hasZ) values.push(vertex.z ?? '');
    if (hasM) values.push(vertex.m ?? '');
    return values.join(', ');
  }).join('\n');
}

function convertCoordinateColumns(text: string, oldHasZ: boolean, oldHasM: boolean, newHasZ: boolean, newHasM: boolean): string {
  return text.split(/\r?\n/).map((line) => {
    if (!line.trim()) return line;
    const values = line.trim().split(/[\s,;]+/).filter(Boolean);
    if (values.length !== 2 + Number(oldHasZ) + Number(oldHasM)) return line;
    let ordinate = 2;
    const z = oldHasZ ? values[ordinate++] : undefined;
    const m = oldHasM ? values[ordinate] : undefined;
    const converted = [values[0], values[1]];
    if (newHasZ) converted.push(z ?? '');
    if (newHasM) converted.push(m ?? '');
    return converted.join(', ');
  }).join('\n');
}

function formattedVertices(vertices: PolygonFeature['vertices'], hasZ: boolean, hasM: boolean): string {
  return verticesToText(vertices, hasZ, hasM);
}

/** Revision-bound polygon operations over the current classification draft. */
export function ClassificationGeometryEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const classification = draft.classificationView ?? snapshot.classification;
  const inputs = classification?.inputs ?? [];
  const chosenInputId = draft.fields[selectionField] ?? classification?.selectedId ?? '';
  const polygonInputs = inputs.filter(isPolygonInput);
  const polygonInputIDs = polygonInputs.map((input) => input.id);
  const polygonInputIDsKey = polygonInputIDs.join('\u0000');
  const initialInputId = polygonInputs.find((input) => input.id === chosenInputId)?.id ?? polygonInputIDs[0] ?? '';
  const [targetInputId, setTargetInputId] = useState(initialInputId);
  const polygonInput = polygonInputs.find((input) => input.id === targetInputId);
  const polygons = polygonInput?.polygons ?? [];
  const polygonIds = polygons.map((polygon) => polygon.id);
  const polygonIdsKey = polygonIds.join('\u0000');
  const allKnownPolygonIds = inputs.flatMap((input) => input.polygons.map((polygon) => polygon.id));
  const allKnownPolygonIdsRef = useRef(allKnownPolygonIds);
  allKnownPolygonIdsRef.current = allKnownPolygonIds;

  const [verticesText, setVerticesText] = useState('');
  const [classValue, setClassValue] = useState(classification?.classes[0] ?? 'NoData');
  const [hasZ, setHasZ] = useState(false);
  const [hasM, setHasM] = useState(false);
  const [selectedPolygonIds, setSelectedPolygonIds] = useState<string[]>([]);
  const [editingPolygonId, setEditingPolygonId] = useState<string | null>(null);
  const [clipboard, setClipboard] = useState<ClipboardPolygon[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState('');
  const [errorDetails, setErrorDetails] = useState('');
  const operationGeneration = useRef(0);
  const pendingNewFeatures = useRef<string[] | null>(null);

  useEffect(() => {
    const selectedInput = polygonInputs.find((input) => input.id === chosenInputId);
    if (selectedInput) setTargetInputId(selectedInput.id);
    else if (targetInputId && !polygonInputIDs.includes(targetInputId)) setTargetInputId(polygonInputIDs[0] ?? '');
  }, [chosenInputId, polygonInputIDsKey, targetInputId]);

  useEffect(() => {
    operationGeneration.current += 1;
    pendingNewFeatures.current = null;
    setBusy(false);
    setNote('');
    setErrorDetails('');
    setTargetInputId(polygonInputs.find((input) => input.id === chosenInputId)?.id ?? polygonInputIDs[0] ?? '');
    setSelectedPolygonIds([]);
    setEditingPolygonId(null);
    setVerticesText('');
    setHasZ(false);
    setHasM(false);
    setClipboard(null);
  }, [props.asset.path, snapshot.revision]);

  useEffect(() => () => { operationGeneration.current += 1; }, []);

  useEffect(() => {
    setSelectedPolygonIds((current) => {
      const created = pendingNewFeatures.current === null ? [] : polygonIds.filter((id) => !pendingNewFeatures.current!.includes(id));
      if (created.length > 0) {
        pendingNewFeatures.current = null;
        return created;
      }
      const surviving = current.filter((id) => polygonIds.includes(id));
      return surviving.length > 0 ? surviving : polygonIds.slice(0, 1);
    });
  }, [targetInputId, polygonIdsKey]);

  useEffect(() => {
    if (!editingPolygonId || polygonIds.includes(editingPolygonId)) return;
    setEditingPolygonId(null);
    setVerticesText('');
    setHasZ(false);
    setHasM(false);
  }, [editingPolygonId, polygonIdsKey]);

  const selectedPolygon = selectedPolygonIds.length === 1
    ? polygons.find((polygon) => polygon.id === selectedPolygonIds[0]) ?? null
    : null;
  const editingPolygon = editingPolygonId
    ? polygons.find((polygon) => polygon.id === editingPolygonId) ?? null
    : null;
  const parsedVertices = parseVertices(verticesText, hasZ, hasM);
  const canMutate = snapshot.canEdit && !!props.onClassificationTransform && !busy;
  const featureNameListId = `classification-geometry-classes-${encodeURIComponent(polygonInput?.id ?? 'new')}`;
  const mapFeatures: ClassificationPolygonMapFeature[] = polygons.map((polygon, index) => ({
    id: polygon.id,
    inputName: draft.fields[`${polygonInput?.scope ?? ''}/Name`] ?? polygonInput?.name ?? '',
    index,
    classification: draft.fields[`${polygon.scope}/${polygonClassField}`] ?? polygon.classification,
    vertices: polygon.vertices,
  }));

  const selectPolygon = (id: string, additive: boolean): void => {
    setSelectedPolygonIds((current) => additive
      ? current.includes(id) ? current.filter((existing) => existing !== id) : [...current, id]
      : [id]);
    if (editingPolygonId !== id) {
      setEditingPolygonId(null);
      setVerticesText('');
      setHasZ(false);
      setHasM(false);
    }
  };

  const beginEdit = (): void => {
    if (!selectedPolygon) return;
    setEditingPolygonId(selectedPolygon.id);
    setSelectedPolygonIds([selectedPolygon.id]);
    setHasZ(selectedPolygon.hasZ);
    setHasM(selectedPolygon.hasM);
    setVerticesText(formattedVertices(selectedPolygon.vertices, selectedPolygon.hasZ, selectedPolygon.hasM));
    setNote('');
    setErrorDetails('');
  };

  const changeAxis = (axis: 'z' | 'm', enabled: boolean): void => {
    const nextHasZ = axis === 'z' ? enabled : hasZ;
    const nextHasM = axis === 'm' ? enabled : hasM;
    setVerticesText(convertCoordinateColumns(verticesText, hasZ, hasM, nextHasZ, nextHasM));
    setHasZ(nextHasZ);
    setHasM(nextHasM);
  };

  const run = async (mutation: ClassificationGeometryMutation, successText: string, selectCreated = false): Promise<void> => {
    if (!canMutate || !props.onClassificationTransform) return;
    const token = ++operationGeneration.current;
    if (selectCreated) pendingNewFeatures.current = [...allKnownPolygonIdsRef.current];
    setBusy(true);
    setNote('');
    setErrorDetails('');
    try {
      const accepted = await props.onClassificationTransform({ operation: 'geometry', geometry: mutation });
      if (operationGeneration.current !== token) return;
      if (!accepted) {
        pendingNewFeatures.current = null;
        setNote(language === 'zh' ? '操作未被当前模型草稿接受；请检查当前来源和未保存修改。' : 'The current model draft did not accept this operation. Check its source and unsaved edits.');
        return;
      }
      setNote(successText);
      if (mutation.operation === 'create' || mutation.operation === 'paste' || mutation.operation === 'replace') {
        setEditingPolygonId(null);
        setVerticesText('');
        setHasZ(false);
        setHasM(false);
      }
      if (mutation.operation === 'delete') {
        setSelectedPolygonIds([]);
        setEditingPolygonId(null);
        setVerticesText('');
        setHasZ(false);
        setHasM(false);
      }
    } catch (cause) {
      if (operationGeneration.current !== token) return;
      pendingNewFeatures.current = null;
      setNote(message(cause, language));
      setErrorDetails(cause instanceof Error ? cause.message : String(cause));
    } finally {
      if (operationGeneration.current === token) setBusy(false);
    }
  };

  const createPolygon = (): void => {
    if (!parsedVertices || !classValue.trim()) return;
    void run({
      operation: 'create',
      inputId: polygonInput?.id ?? null,
      vertices: parsedVertices,
      hasZ,
      hasM,
      classification: classValue,
    }, language === 'zh' ? '多边形已加入当前草稿；点击“保存”后写入模型。' : 'The polygon is in the current draft. Save to write it to the model.', true);
  };

  const replacePolygon = (): void => {
    if (!editingPolygon || !polygonInput || !parsedVertices) return;
    void run({ operation: 'replace', inputId: polygonInput.id, polygonIds: [editingPolygon.id], vertices: parsedVertices, hasZ, hasM },
      language === 'zh' ? '多边形坐标已更新到当前草稿；保存后写入模型。' : 'Polygon coordinates were updated in the current draft. Save to write the model.');
  };

  const deletePolygons = (): void => {
    if (!polygonInput || selectedPolygonIds.length === 0) return;
    void run({ operation: 'delete', inputId: polygonInput.id, polygonIds: selectedPolygonIds }, language === 'zh' ? '所选多边形已从当前草稿删除。' : 'The selected polygons were deleted from the current draft.');
  };

  const copyPolygons = (): void => {
    const selected = polygons.filter((polygon) => selectedPolygonIds.includes(polygon.id));
    if (selected.length === 0) return;
    setClipboard(selected.map((polygon) => ({
      vertices: polygon.vertices.map((vertex) => ({ ...vertex })),
      hasZ: polygon.hasZ,
      hasM: polygon.hasM,
      classification: draft.fields[`${polygon.scope}/${polygonClassField}`] ?? polygon.classification,
    })));
    setNote(language === 'zh' ? `已复制 ${selected.length} 个多边形到此编辑器的剪贴板。` : `Copied ${selected.length} polygons to this editor's clipboard.`);
    setErrorDetails('');
  };

  const pastePolygons = (): void => {
    if (!polygonInput || !clipboard?.length) return;
    void run({ operation: 'paste', inputId: polygonInput.id, features: clipboard.map((feature) => ({
      vertices: feature.vertices.map((vertex) => ({ ...vertex })),
      hasZ: feature.hasZ,
      hasM: feature.hasM,
      classification: feature.classification,
    })) }, language === 'zh' ? `已将 ${clipboard.length} 个多边形粘贴到当前草稿。` : `Pasted ${clipboard.length} polygons into the current draft.`, true);
  };

  const toggleCreateAxis = (axis: 'z' | 'm', enabled: boolean): void => {
    if (editingPolygon) return;
    changeAxis(axis, enabled);
  };

  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '分类多边形几何' : 'Classification polygon geometry'} data-testid="classification-geometry-editor">
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '多边形几何' : 'Polygon geometry'}</h3>
        <span>{polygonInput?.name ?? (language === 'zh' ? '新建多边形输入' : 'New polygon input')}</span>
      </div>

      <div className="editor-field-grid">
        <div className="editor-field">
          <label htmlFor="classification-geometry-input">{language === 'zh' ? '多边形输入' : 'Polygon input'}</label>
          <select id="classification-geometry-input" data-testid="classification-geometry-input" value={targetInputId} disabled={busy} onChange={(event) => {
            const id = event.currentTarget.value;
            setTargetInputId(id);
            setEditingPolygonId(null);
            setVerticesText('');
            setHasZ(false);
            setHasM(false);
            setSelectedPolygonIds([]);
            props.onFieldChange(selectionField, id);
          }}>
            <option value="">{language === 'zh' ? '创建新的多边形输入' : 'Create a new polygon input'}</option>
            {polygonInputs.map((input) => <option value={input.id} key={input.id}>{draft.fields[`${input.scope}/Name`] ?? input.name}</option>)}
          </select>
          <small className="editor-field-muted">{language === 'zh' ? '可选择现有输入（包括空输入）；创建时会复用所选输入，没有所选输入时由服务创建新输入。' : 'Choose an existing input, including an empty one. Creation uses it, or the service creates a new input when none is selected.'}</small>
        </div>
        <div className="editor-field">
          <span className="editor-field-label">{language === 'zh' ? '选择状态' : 'Selection'}</span>
          <span>{selectedPolygonIds.length} {language === 'zh' ? '个多边形' : 'polygons'}</span>
          <button type="button" className="editor-button" data-testid="classification-polygon-clear-selection" disabled={busy || selectedPolygonIds.length === 0} onClick={() => {
            setSelectedPolygonIds([]);
            setEditingPolygonId(null);
            setVerticesText('');
            setHasZ(false);
            setHasM(false);
          }}>{language === 'zh' ? '清除选择' : 'Clear selection'}</button>
        </div>
      </div>

      <ClassificationPolygonCanvas features={mapFeatures} selectedIDs={selectedPolygonIds} language={language} onSelect={selectPolygon} />

      {polygons.length > 0 && <div className="editor-polygon-list" aria-label={language === 'zh' ? '多边形要素列表' : 'Polygon feature list'} data-testid="classification-polygon-list">
        {polygons.map((polygon, index) => {
          const isSelected = selectedPolygonIds.includes(polygon.id);
          const selectedClass = draft.fields[`${polygon.scope}/${polygonClassField}`] ?? polygon.classification;
          return <button type="button" key={polygon.id} className={`editor-button${isSelected ? ' editor-button-selected' : ''}`} style={{ display: 'flex', width: '100%', flexDirection: 'column', alignItems: 'flex-start', gap: 4, textAlign: 'left' }} aria-pressed={isSelected} data-testid={`classification-polygon-row-${index + 1}`} onClick={(event) => selectPolygon(polygon.id, event.shiftKey || event.ctrlKey || event.metaKey)}>
            {language === 'zh' ? `要素 ${index + 1}` : `Feature ${index + 1}`}
            <small>{polygon.vertexCount} {language === 'zh' ? '个顶点' : 'vertices'} · {language === 'zh' ? '分类' : 'class'} {selectedClass} · {polygon.hasZ ? 'Z' : ''}{polygon.hasM ? 'M' : ''}{!polygon.hasZ && !polygon.hasM ? (language === 'zh' ? '无 Z/M' : 'no Z/M') : ''}</small>
          </button>;
        })}
      </div>}

      {polygonInputs.length === 0 && <p className="editor-field-muted">{language === 'zh' ? '此分类图层还没有多边形输入；添加首个多边形时，服务会创建输入。' : 'This classification layer has no polygon input yet. The service creates one when the first polygon is added.'}</p>}
      {polygonInput && polygons.length === 0 && <div className="editor-empty-state">{language === 'zh' ? '所选多边形输入为空，可以直接添加第一个多边形。' : 'The selected polygon input is empty. Add its first polygon here.'}</div>}

      <div className="editor-field-grid">
        <div className="editor-field">
          <label htmlFor="classification-polygon-vertices">{editingPolygon ? (language === 'zh' ? '编辑顶点（每行 X, Y, Z?, M?）' : 'Edit vertices (one X, Y, optional Z/M per line)') : (language === 'zh' ? '新多边形顶点（每行 X, Y, Z?, M?）' : 'New polygon vertices (one X, Y, optional Z/M per line)')}</label>
          <textarea id="classification-polygon-vertices" data-testid="classification-polygon-vertices" rows={6} value={verticesText} disabled={!canMutate} placeholder={language === 'zh' ? '输入实际工程坐标，每行一个顶点；按启用的 Z/M 维度追加数值。' : 'Enter project coordinates, one vertex per line, followed by enabled Z/M ordinates.'} onChange={(event) => setVerticesText(event.currentTarget.value)} />
          <small className="editor-field-muted">{language === 'zh' ? '编辑现有要素时，原有 Z/M 维度和值会预填并保留；新建时可选择要写入的维度。' : 'Editing preloads and retains existing Z/M dimensions and values. Choose dimensions for new polygons.'}</small>
        </div>
        <div className="editor-field">
          <span className="editor-field-label">{language === 'zh' ? '坐标维度' : 'Coordinate dimensions'}</span>
          <label className="editor-toggle"><input type="checkbox" checked={hasZ} disabled={!canMutate || !!editingPolygon} onChange={(event) => toggleCreateAxis('z', event.currentTarget.checked)} /> Z</label>
          <label className="editor-toggle"><input type="checkbox" checked={hasM} disabled={!canMutate || !!editingPolygon} onChange={(event) => toggleCreateAxis('m', event.currentTarget.checked)} /> M</label>
          {editingPolygon && <small className="editor-field-muted">{language === 'zh' ? '当前要素的 Z/M 维度固定保留。' : 'The selected feature retains its existing Z/M dimensions.'}</small>}
        </div>
        {!editingPolygon && <div className="editor-field">
          <label htmlFor="classification-polygon-class">{language === 'zh' ? '新多边形分类值' : 'New polygon classification'}</label>
          <input id="classification-polygon-class" data-testid="classification-polygon-class" value={classValue} list={featureNameListId} disabled={!canMutate} onChange={(event) => setClassValue(event.currentTarget.value)} />
          <datalist id={featureNameListId}>{classification?.classes.map((value) => <option value={value} key={value} />)}</datalist>
        </div>}
      </div>

      {verticesText.trim() && !parsedVertices && <p className="editor-field-muted" role="alert">{language === 'zh' ? `需要至少三个不同顶点、非零面积且所有坐标有限；每行应有 ${2 + Number(hasZ) + Number(hasM)} 个数值。` : `Use at least three distinct vertices with nonzero area and finite ordinates; each line needs ${2 + Number(hasZ) + Number(hasM)} values.`}</p>}
      {!editingPolygon && classValue.trim().length === 0 && <p className="editor-field-muted" role="alert">{language === 'zh' ? '新多边形需要分类值。' : 'A classification value is required for a new polygon.'}</p>}

      <div className="editor-table-toolbar">
        {editingPolygon
          ? <>
            <button type="button" className="editor-button editor-button-primary" data-testid="classification-polygon-apply-edit" disabled={!canMutate || !parsedVertices} onClick={replacePolygon}>{language === 'zh' ? '应用顶点修改' : 'Apply vertex edits'}</button>
            <button type="button" className="editor-button" disabled={busy} onClick={() => { setEditingPolygonId(null); setVerticesText(''); setHasZ(false); setHasM(false); }}>{language === 'zh' ? '取消编辑' : 'Cancel edit'}</button>
          </>
          : <button type="button" className="editor-button editor-button-primary" data-testid="classification-polygon-create" disabled={!canMutate || !parsedVertices || !classValue.trim()} onClick={createPolygon}>{language === 'zh' ? '添加多边形' : 'Add polygon'}</button>}
        <button type="button" className="editor-button" data-testid="classification-polygon-edit" disabled={!canMutate || !selectedPolygon} onClick={beginEdit}>{language === 'zh' ? '编辑所选顶点' : 'Edit selected vertices'}</button>
        <button type="button" className="editor-button" data-testid="classification-polygon-delete" disabled={!canMutate || selectedPolygonIds.length === 0} onClick={deletePolygons}>{language === 'zh' ? `删除所选（${selectedPolygonIds.length}）` : `Delete selected (${selectedPolygonIds.length})`}</button>
        <button type="button" className="editor-button" data-testid="classification-polygon-copy" disabled={selectedPolygonIds.length === 0 || busy} onClick={copyPolygons}>{language === 'zh' ? '复制所选' : 'Copy selected'}</button>
        <button type="button" className="editor-button" data-testid="classification-polygon-paste" disabled={!canMutate || !polygonInput || !clipboard?.length} onClick={pastePolygons}>{language === 'zh' ? `粘贴${clipboard?.length ? `（${clipboard.length}）` : ''}` : `Paste${clipboard?.length ? ` (${clipboard.length})` : ''}`}</button>
        <button type="button" className="editor-button" data-testid="classification-polygon-undo" disabled={!canMutate || !classification?.canUndoGeometry} onClick={() => void run({ operation: 'undo' }, language === 'zh' ? '已撤销最近一次多边形几何操作。' : 'Undid the latest polygon geometry operation.')}>{language === 'zh' ? '撤销' : 'Undo'}</button>
        <button type="button" className="editor-button" data-testid="classification-polygon-redo" disabled={!canMutate || !classification?.canRedoGeometry} onClick={() => void run({ operation: 'redo' }, language === 'zh' ? '已重做多边形几何操作。' : 'Redid the polygon geometry operation.')}>{language === 'zh' ? '重做' : 'Redo'}</button>
      </div>
      {!props.onClassificationTransform && <p className="editor-field-muted">{language === 'zh' ? '当前工作环境不支持多边形几何操作；写入按钮已停用。' : 'Polygon geometry operations are unavailable in this workspace.'}</p>}
      {!snapshot.canEdit && <p className="editor-field-muted">{language === 'zh' ? '当前模型为只读状态。' : 'The current model is read-only.'}</p>}
      {note && <div className="editor-field-muted" role="status"><span>{note}</span>{errorDetails && <details><summary>{language === 'zh' ? '诊断详情' : 'Diagnostic details'}</summary><pre>{errorDetails}</pre></details>}</div>}
    </section>
  );
}
