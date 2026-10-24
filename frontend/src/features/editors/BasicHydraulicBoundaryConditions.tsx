import { useEffect, useMemo, useState } from 'react';
import type { EditorPageProps } from './model';
import { BoundaryConditionLinesList } from './BoundaryConditionLinesList';
import { BoundaryConditionLineEditor } from './BoundaryConditionLineEditor';

/** Source composition for the basic hydraulic line list and its existing detail/payload editors. */
export function BasicHydraulicBoundaryConditions(props: EditorPageProps) {
  const boundary = props.draft.boundaryView ?? props.snapshot.boundary;
  const lines = boundary?.lines ?? [];
  const [selectedLineID, setSelectedLineID] = useState(boundary?.selectedLineId ?? lines[0]?.id ?? '');
  const activeLineID = lines.some((line) => line.id === selectedLineID) ? selectedLineID : lines[0]?.id ?? '';

  useEffect(() => {
    if (selectedLineID !== activeLineID) setSelectedLineID(activeLineID);
  }, [activeLineID, selectedLineID]);

  const detailDraft = useMemo(() => ({
    ...props.draft,
    // This child-only projection drives the detail editor's initial line selection.
    // Choosing a line never calls a workbench draft mutation or changes its save baseline.
    boundaryView: boundary ? { ...boundary, selectedLineId: activeLineID } : null,
  }), [activeLineID, boundary, props.draft]);
  const detailProps = useMemo(() => ({ ...props, draft: detailDraft }), [detailDraft, props]);

  return <div className="editor-basic-hydraulic-boundaries">
    <style>{`.editor-basic-hydraulic-boundaries > .editor-boundary-layout { grid-template-columns: minmax(0, 1fr); }
.editor-basic-hydraulic-boundaries > .editor-boundary-layout > .editor-line-list { display: none; }`}</style>
    <BoundaryConditionLinesList {...props} selectedLineID={activeLineID} onSelect={setSelectedLineID} />
    <BoundaryConditionLineEditor key={activeLineID} {...detailProps} />
  </div>;
}
