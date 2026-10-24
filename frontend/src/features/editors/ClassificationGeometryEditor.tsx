import { useState } from 'react';
import type { EditorPageProps } from './model';

const selectionField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';

/** Coordinate entry and undo/redo commands backed by ClassificationEditorService geometry mutations. */
export function ClassificationGeometryEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const classification = draft.classificationView ?? snapshot.classification;
  const inputs = classification?.inputs ?? [];
  const chosenID = draft.fields[selectionField] ?? classification?.selectedId ?? '';
  const polygonInput = inputs.find((input) => input.id === chosenID && input.polygons.length > 0)
    ?? inputs.find((input) => input.polygons.length > 0);
  const [verticesText, setVerticesText] = useState('');
  const [classValue, setClassValue] = useState(classification?.classes[0] ?? 'NoData');
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState('');
  const parseVertices = (): Array<{ x: number; y: number }> | null => {
    const parsed = verticesText.split(/\r?\n/).map((line) => line.trim()).filter(Boolean).map((line) => {
      const parts = line.split(/[\s,;]+/).filter(Boolean);
      if (parts.length !== 2) return null;
      const x = Number(parts[0]); const y = Number(parts[1]);
      return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
    });
    if (parsed.length < 3 || parsed.some((point) => point == null)) return null;
    const distinct = new Set(parsed.map((point) => `${point!.x}\u0000${point!.y}`));
    return distinct.size >= 3 ? parsed as Array<{ x: number; y: number }> : null;
  };
  const vertices = parseVertices();
  const canMutate = snapshot.canEdit && !!props.onClassificationTransform && !busy;
  const run = async (operation: 'undo' | 'redo' | 'create'): Promise<void> => {
    if (!canMutate || !props.onClassificationTransform) return;
    setBusy(true);
    setNote('');
    try {
      const accepted = await props.onClassificationTransform(operation === 'create'
        ? { operation: 'geometry', geometry: { operation: 'create', inputId: polygonInput?.id ?? null, vertices: vertices ?? [], classification: classValue } }
        : { operation: 'geometry', geometry: { operation } });
      if (accepted) {
        setNote(language === 'zh' ? '几何编辑已进入草稿；点击“保存”后才会写入模型文件。' : 'The geometry edit is in the draft. Save to write it to the model file.');
        if (operation === 'create') setVerticesText('');
      }
    } catch {
      setNote(language === 'zh' ? '多边形操作未完成；请检查当前输入和源修订后重试。' : 'The polygon operation did not complete. Check the current input and source revision, then try again.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '分类多边形几何' : 'Classification polygon geometry'}>
      <div className="editor-terrain-table-heading"><h3>{language === 'zh' ? '多边形几何' : 'Polygon geometry'}</h3><span>{polygonInput?.name ?? (language === 'zh' ? '将创建新的多边形输入' : 'A new polygon input will be created')}</span></div>
      <div className="editor-field-grid">
        <div className="editor-field"><label htmlFor="classification-polygon-vertices">{language === 'zh' ? '顶点坐标（每行 X, Y）' : 'Vertices (one X, Y pair per line)'}</label>
          <textarea id="classification-polygon-vertices" rows={5} value={verticesText} disabled={!canMutate} placeholder={language === 'zh' ? '按工程投影坐标输入，至少三行；例如：\n500000, 4400000\n500100, 4400000\n500100, 4400100' : 'Enter project-projection coordinates, at least three lines; for example:\n500000, 4400000\n500100, 4400000\n500100, 4400100'} onChange={(event) => setVerticesText(event.currentTarget.value)} />
        </div>
        <div className="editor-field"><label htmlFor="classification-polygon-class">{language === 'zh' ? '分类值' : 'Classification value'}</label>
          <input id="classification-polygon-class" value={classValue} list="classification-geometry-classes" disabled={!canMutate} onChange={(event) => setClassValue(event.currentTarget.value)} />
          <datalist id="classification-geometry-classes">{classification?.classes.map((value) => <option value={value} key={value} />)}</datalist>
        </div>
      </div>
      <div className="editor-table-toolbar">
        <button type="button" className="editor-button" disabled={!canMutate || !vertices} onClick={() => void run('create')}>{language === 'zh' ? '添加多边形' : 'Add polygon'}</button>
        <button type="button" className="editor-button" disabled={!canMutate || !classification?.canUndoGeometry} onClick={() => void run('undo')}>{language === 'zh' ? '撤销几何' : 'Undo geometry'}</button>
        <button type="button" className="editor-button" disabled={!canMutate || !classification?.canRedoGeometry} onClick={() => void run('redo')}>{language === 'zh' ? '重做几何' : 'Redo geometry'}</button>
      </div>
      {!props.onClassificationTransform && <p className="editor-field-muted">{language === 'zh' ? '当前工作环境不支持多边形编辑，操作已停用。' : 'Polygon editing is unavailable in this workspace.'}</p>}
      {verticesText.trim() && !vertices && <p className="editor-field-muted" role="alert">{language === 'zh' ? '每行需要有限的 X、Y 数值，且至少有三个不同顶点。' : 'Enter finite X and Y values on each line and at least three distinct vertices.'}</p>}
      {note && <p className="editor-field-muted" role="status">{note}</p>}
    </section>
  );
}
