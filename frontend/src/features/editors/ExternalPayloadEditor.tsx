import { isEditorFieldReadOnly, type EditorPageProps } from './model';

type BoundaryPayload = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number]['payloads'][number];

/** React port of source ExternalPayloadEditor fields, without fabricating an evaluation result. */
export function ExternalPayloadEditor({ payload, ...props }: EditorPageProps & { payload: BoundaryPayload }) {
  const pathID = `${payload.scope}/ExternalPayloadEditor/ExternalPayload_ExternalPath`;
  const connectionID = `${payload.scope}/ExternalPayloadEditor/ExternalPayload_DataConnection`;
  const path = props.draft.fields[pathID] ?? props.snapshot.fields[pathID] ?? '';
  const connection = props.draft.fields[connectionID] ?? props.snapshot.fields[connectionID] ?? '';
  return <div className="editor-field-grid">
    <div className="editor-field">
      <label htmlFor={`payload-${payload.id}-external-connection`}>{props.language === 'zh' ? '外部连接' : 'External connection'}</label>
      <input id={`payload-${payload.id}-external-connection`} type="text" value={connection} readOnly />
    </div>
    <div className="editor-field">
      <label htmlFor={`payload-${payload.id}-external-path`}>{props.language === 'zh' ? '外部数据路径' : 'External data path'}</label>
      <input id={`payload-${payload.id}-external-path`} type="text" value={path} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, pathID)} onChange={(event) => props.onFieldChange(pathID, event.currentTarget.value)} />
    </div>
    <p className="editor-field-muted">{props.language === 'zh' ? '连接名称和路径来自模型草稿；此快照未提供已评估时序，不能在此显示计算曲线。' : 'The connection and path come from the model draft. This snapshot has no evaluated series to plot.'}</p>
  </div>;
}
