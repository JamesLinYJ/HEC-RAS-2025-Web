import { isEditorFieldReadOnly, type EditorPageProps } from './model';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

/** Detail-page counterpart to the map quick editor: choose the active hydraulic payload. */
export function BoundaryConditionLineQuickEditPicker({ line, onAddPayload, ...props }: EditorPageProps & { line: BoundaryLine; onAddPayload: () => void }) {
  const candidates = line.payloads.filter((payload) => payload.isHydraulic);
  const selected = candidates.find((payload) =>
    (props.draft.fields[`${payload.scope}/PayloadListItem/Payload_IsEnabled`]
      ?? props.snapshot.fields[`${payload.scope}/PayloadListItem/Payload_IsEnabled`]
      ?? String(payload.isEnabled)) === 'true')?.id ?? line.activeHydraulicPayloadId ?? '';
  const changeActive = (payloadID: string): void => {
    const payload = candidates.find((candidate) => candidate.id === payloadID);
    if (!payload || isEditorFieldReadOnly(props, `${payload.scope}/PayloadListItem/Payload_IsEnabled`)) return;
    for (const candidate of candidates) {
      props.onFieldChange(`${candidate.scope}/PayloadListItem/Payload_IsEnabled`, candidate.id === payloadID ? 'true' : 'false');
    }
  };
  return <div className="editor-payload-heading" aria-label={props.language === 'zh' ? '边界线快速编辑' : 'Boundary line quick edit'}>
    <div className="editor-field" style={{ flex: '1 1 260px' }}>
      <label htmlFor={`boundary-line-${line.id}-active-payload`}>{props.language === 'zh' ? '当前水力载荷' : 'Active hydraulic payload'}</label>
      <select id={`boundary-line-${line.id}-active-payload`} value={selected} disabled={!props.snapshot.canEdit || candidates.length === 0 || candidates.every((payload) => isEditorFieldReadOnly(props, `${payload.scope}/PayloadListItem/Payload_IsEnabled`))} onChange={(event) => changeActive(event.currentTarget.value)}>
        {candidates.length === 0 && <option value="">{props.language === 'zh' ? '没有水力载荷' : 'No hydraulic payloads'}</option>}
        {candidates.map((payload) => <option key={payload.id} value={payload.id}>{props.draft.fields[`${payload.scope}/PayloadListItem/Payload_Name`] ?? payload.name ?? payload.friendlyName}</option>)}
      </select>
      {candidates.length === 0 && <small className="editor-field-muted">{props.language === 'zh' ? '添加水力载荷后可在此切换。' : 'Add a hydraulic payload before selecting one here.'}</small>}
    </div>
    <button type="button" className="editor-button" disabled={!props.snapshot.canEdit || !props.onBoundaryTransform} onClick={onAddPayload}>{props.language === 'zh' ? '添加载荷…' : 'Add payload…'}</button>
  </div>;
}
