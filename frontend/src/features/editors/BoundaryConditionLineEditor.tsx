import { useEffect, useMemo, useState } from 'react';
import type { EditorPageProps } from './model';
import { BoundaryConditionLinePayloadsList } from './BoundaryConditionLinePayloadsList';
import { BoundaryConditionLineQuickEditPicker } from './BoundaryConditionLineQuickEditPicker';
import { DetailedBCLineHeaderControl } from './DetailedBCLineHeaderControl';
import { RegularSpatiallyVariableTimeSeriesPayloadEditor } from './RegularSpatiallyVariableTimeSeriesPayloadEditor';
import { RegularTimeSeriesPayloadEditor } from './RegularTimeSeriesPayloadEditor';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

/** Detail assembly for the source BoundaryConditionLineEditor control. */
export function BoundaryConditionLineEditor(props: EditorPageProps) {
  const boundary = props.draft.boundaryView ?? props.snapshot.boundary;
  const lines = boundary?.lines ?? [];
  const [activeLineID, setActiveLineID] = useState(boundary?.selectedLineId ?? lines[0]?.id ?? '');
  const [createOpen, setCreateOpen] = useState(false);
  const currentLine = lines.find((line) => line.id === activeLineID) ?? lines[0];
  const lineSeries = useMemo(() => Object.values(boundary?.series ?? {}).filter((series) =>
    !!currentLine && series.scope.startsWith(currentLine.scope)), [boundary?.series, currentLine]);

  useEffect(() => {
    if (!lines.some((line) => line.id === activeLineID)) setActiveLineID(boundary?.selectedLineId ?? lines[0]?.id ?? '');
  }, [activeLineID, boundary?.selectedLineId, lines]);

  if (!currentLine) return <div className="editor-empty-state">{props.language === 'zh' ? '此边界条件没有水力边界线。' : 'This boundary condition has no hydraulic boundary lines.'}</div>;

  const boundPayloadIDs = new Set(lineSeries.filter((series) => currentLine.payloads.some((payload) => series.scope.startsWith(payload.scope))).map((series) => series.pageId));
  const unboundSeries = lineSeries.filter((series) => !boundPayloadIDs.has(series.pageId));
  return <div className="editor-boundary-layout">
    <nav className="editor-line-list" aria-label={props.language === 'zh' ? '边界线' : 'Boundary lines'}>
      {lines.map((line) => <LineNavigationItem key={line.id} line={line} selected={line.id === currentLine.id} {...props} onSelect={() => { setActiveLineID(line.id); setCreateOpen(false); }} />)}
    </nav>
    <div className="editor-boundary-content">
      <DetailedBCLineHeaderControl {...props} line={currentLine} />
      <BoundaryConditionLineQuickEditPicker {...props} line={currentLine} onAddPayload={() => setCreateOpen(true)} />
      <BoundaryConditionLinePayloadsList
        {...props}
        line={currentLine}
        series={lineSeries}
        createOpen={createOpen}
        onCreateOpenChange={setCreateOpen}
      />
      {unboundSeries.map((series) => series.schemaId === 'RegularSpatiallyVariableTimeSeriesPayloadEditor'
        ? <RegularSpatiallyVariableTimeSeriesPayloadEditor {...props} key={series.pageId} series={series} />
        : <RegularTimeSeriesPayloadEditor {...props} key={series.pageId} series={series} />)}
    </div>
  </div>;
}

function LineNavigationItem({ line, selected, onSelect, ...props }: EditorPageProps & {
  line: BoundaryLine;
  selected: boolean;
  onSelect: () => void;
}) {
  const nameID = `${line.scope}/DetailedBCLineHeaderControl/BoundaryConditionLine_Name`;
  const name = props.draft.fields[nameID] ?? props.snapshot.fields[nameID] ?? line.name;
  return <button type="button" className={selected ? 'selected' : ''} aria-current={selected ? 'location' : undefined} onClick={onSelect}>
    {name}<small>{line.payloads.length} {props.language === 'zh' ? '个载荷' : 'payloads'}</small>
  </button>;
}
