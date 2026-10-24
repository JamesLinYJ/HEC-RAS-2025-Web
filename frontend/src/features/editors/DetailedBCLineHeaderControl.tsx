import { isEditorFieldReadOnly, type EditorPageProps } from './model';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

/** React port of Ras.Visual.WinUI.Controls.DetailedBCLineHeaderControl. */
export function DetailedBCLineHeaderControl({ line, ...props }: EditorPageProps & { line: BoundaryLine }) {
  const fieldID = `${line.scope}/DetailedBCLineHeaderControl/BoundaryConditionLine_Name`;
  const name = props.draft.fields[fieldID] ?? props.snapshot.fields[fieldID] ?? line.name;
  const invalid = props.snapshot.canEdit && name.trim().length === 0;
  return <header className="editor-payload-heading">
    <div className="editor-field" style={{ flex: '1 1 280px' }}>
      <label htmlFor={`boundary-line-${line.id}-name`}>{props.language === 'zh' ? '边界线名称' : 'Boundary line name'}</label>
      <input id={`boundary-line-${line.id}-name`} type="text" value={name} readOnly={!props.snapshot.canEdit || isEditorFieldReadOnly(props, fieldID)} aria-invalid={invalid || undefined} onChange={(event) => props.onFieldChange(fieldID, event.currentTarget.value)} />
      {invalid && <small className="editor-field-error">{props.language === 'zh' ? '边界线名称不能为空。' : 'A boundary line name is required.'}</small>}
    </div>
    <div className="editor-source-chip" title={line.scope}>
      {line.geometry?.length ? `${line.geometry.length} ${props.language === 'zh' ? '个几何点' : 'geometry points'}` : props.language === 'zh' ? '未提供几何点' : 'No geometry points'}
    </div>
  </header>;
}
