import { useEffect, useMemo, useState } from 'react';
import type { EditorPageProps } from './model';
import { BCLinesLayerActiveFeatures } from './BCLinesLayerActiveFeatures';
import { BoundaryConditionLineEditor } from './BoundaryConditionLineEditor';

/** Page composition for choosing a snapshot line and editing its existing payloads. */
export function BoundaryConditionLineAndPayloadPickerPage(props: EditorPageProps) {
  const boundary = props.draft.boundaryView ?? props.snapshot.boundary;
  const lines = boundary?.lines ?? [];
  const initialSelection = boundary?.selectedLineId && lines.some((line) => line.id === boundary.selectedLineId)
    ? boundary.selectedLineId
    : lines[0]?.id ?? '';
  const [selectedLineID, setSelectedLineID] = useState(initialSelection);
  const activeLineID = lines.some((line) => line.id === selectedLineID) ? selectedLineID : initialSelection;

  useEffect(() => {
    if (selectedLineID !== activeLineID) setSelectedLineID(activeLineID);
  }, [activeLineID, selectedLineID]);

  const detailDraft = useMemo(() => ({
    ...props.draft,
    // This child-only selection projection does not mutate the parent draft or its save baseline.
    boundaryView: boundary ? { ...boundary, selectedLineId: activeLineID } : null,
  }), [activeLineID, boundary, props.draft]);
  const detailProps = useMemo(() => ({ ...props, draft: detailDraft }), [detailDraft, props]);

  return <section className="editor-schema-page" aria-label={props.language === 'zh' ? '边界线与载荷选择' : 'Boundary line and payload picker'}>
    <div className="editor-schema-heading">
      <div>
        <p className="editor-eyebrow">{props.language === 'zh' ? '水力边界' : 'Hydraulic boundaries'}</p>
        <h2>{props.language === 'zh' ? '边界线与载荷' : 'Boundary lines and payloads'}</h2>
      </div>
      <span className="editor-catalog-count">{lines.length} {props.language === 'zh' ? '条边界线' : 'boundary lines'}</span>
    </div>
    <p className="editor-field-muted">{props.language === 'zh' ? '从边界线列表选择对象，然后编辑其载荷和时序。' : 'Choose a line from the list to edit its payloads and time series.'}</p>
    {!boundary && <div className="editor-empty-state">{props.language === 'zh' ? '当前快照没有边界条件数据。' : 'The current snapshot has no boundary-condition data.'}</div>}
    {boundary && <div className="editor-boundary-layout">
      <BCLinesLayerActiveFeatures {...props} selectedLineID={activeLineID} onSelect={setSelectedLineID} />
      {activeLineID
        ? <BoundaryConditionLineEditor key={activeLineID} {...detailProps} showLinePicker={false} />
        : <div className="editor-empty-state">{props.language === 'zh' ? '请选择一条边界线。' : 'Select a boundary line.'}</div>}
    </div>}
  </section>;
}
