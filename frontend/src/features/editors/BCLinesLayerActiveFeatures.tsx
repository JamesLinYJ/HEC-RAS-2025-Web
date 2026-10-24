import type { EditorPageProps } from './model';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

/** Snapshot-backed list counterpart for the source active-feature line selector. */
export function BCLinesLayerActiveFeatures({ selectedLineID, onSelect, ...props }: EditorPageProps & {
  selectedLineID: string;
  onSelect: (lineID: string) => void;
}) {
  const boundary = props.draft.boundaryView ?? props.snapshot.boundary;
  const lines = boundary?.lines ?? [];
  if (!lines.length) return <div className="editor-empty-state">{props.language === 'zh' ? '此边界条件没有水力边界线。' : 'This boundary condition has no hydraulic boundary lines.'}</div>;

  // The current host exposes line GUIDs in the model snapshot but no map single-selection callback.
  // Keep selection in this source-backed list and pass the real GUID to the detail view.
  return <nav className="editor-line-list" aria-label={props.language === 'zh' ? '边界线列表' : 'Boundary line list'} data-testid="boundary-lines-list">
    {lines.map((line) => <BoundaryLineFeature
      key={line.id}
      line={line}
      selected={line.id === selectedLineID}
      onSelect={() => onSelect(line.id)}
      {...props}
    />)}
  </nav>;
}

function BoundaryLineFeature({ line, selected, onSelect, ...props }: EditorPageProps & {
  line: BoundaryLine;
  selected: boolean;
  onSelect: () => void;
}) {
  const nameID = `${line.scope}/DetailedBCLineHeaderControl/BoundaryConditionLine_Name`;
  const name = props.draft.fields[nameID] ?? props.snapshot.fields[nameID] ?? line.name;
  const activePayload = line.activeHydraulicPayloadId
    ? line.payloads.find((payload) => payload.id === line.activeHydraulicPayloadId)
    : undefined;

  return <button
    type="button"
    className={selected ? 'selected' : ''}
    aria-current={selected ? 'location' : undefined}
    aria-pressed={selected}
    data-line-id={line.id}
    data-testid={`boundary-line-option-${line.id}`}
    onClick={onSelect}
  >
    <span>{name}</span>
    <small>{line.payloads.length} {props.language === 'zh' ? '个载荷' : 'payloads'}</small>
    {activePayload && <small>{props.language === 'zh' ? '当前水力载荷' : 'Active hydraulic payload'}: {activePayload.name || activePayload.friendlyName}</small>}
  </button>;
}
