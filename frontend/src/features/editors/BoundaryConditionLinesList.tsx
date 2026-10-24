import type { EditorPageProps } from './model';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

/** GUID-keyed line selector; selection is presentation state and does not edit the draft. */
export function BoundaryConditionLinesList({ selectedLineID, onSelect, ...props }: EditorPageProps & {
  selectedLineID: string;
  onSelect: (lineID: string) => void;
}) {
  const boundary = props.draft.boundaryView ?? props.snapshot.boundary;
  const lines = boundary?.lines ?? [];
  if (!lines.length) return <div className="editor-empty-state">{props.language === 'zh' ? '此边界条件没有水力边界线。' : 'This boundary condition has no hydraulic boundary lines.'}</div>;

  return <nav className="editor-line-list" aria-label={props.language === 'zh' ? '边界线' : 'Boundary lines'}>
    {lines.map((line) => <BoundaryLineItem
      key={line.id}
      line={line}
      selected={line.id === selectedLineID}
      onSelect={() => onSelect(line.id)}
      {...props}
    />)}
  </nav>;
}

function BoundaryLineItem({ line, selected, onSelect, ...props }: EditorPageProps & {
  line: BoundaryLine;
  selected: boolean;
  onSelect: () => void;
}) {
  const nameID = `${line.scope}/DetailedBCLineHeaderControl/BoundaryConditionLine_Name`;
  const name = props.draft.fields[nameID] ?? props.snapshot.fields[nameID] ?? line.name;
  const activePayload = line.activeHydraulicPayloadId
    ? line.payloads.find((payload) => payload.id === line.activeHydraulicPayloadId)
    : undefined;

  return <button type="button" className={selected ? 'selected' : ''} aria-current={selected ? 'location' : undefined} onClick={onSelect}>
    <span>{name}</span>
    <small>{line.payloads.length} {props.language === 'zh' ? '个载荷' : 'payloads'}</small>
    <small>{props.language === 'zh' ? '范围' : 'Scope'}: <code>{line.scope}</code></small>
    {activePayload && <small>{props.language === 'zh' ? '活动类型' : 'Active type'}: {activePayload.variable}</small>}
  </button>;
}
