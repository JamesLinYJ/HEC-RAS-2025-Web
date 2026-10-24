import type { EditorPageProps } from './model';
import type { BoundarySeries } from './RegularTimeSeriesPayloadEditor';
import { NewPayloadDialog } from './NewPayloadDialog';
import { PayloadListItem } from './PayloadListItem';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];
type BoundaryPayload = BoundaryLine['payloads'][number];

/** Source BoundaryConditionLinePayloadsList with mounted payload cards and the server-backed add dialog. */
export function BoundaryConditionLinePayloadsList({ line, series, createOpen, onCreateOpenChange, ...props }: EditorPageProps & {
  line: BoundaryLine;
  series: BoundarySeries[];
  createOpen: boolean;
  onCreateOpenChange: (open: boolean) => void;
}) {
  const canMutate = props.snapshot.canEdit && !!props.onBoundaryTransform;
  const removePayload = (payload: BoundaryPayload): void => {
    if (!props.onBoundaryTransform) return;
    void props.onBoundaryTransform(line.id, 'remove', payload.id, {});
  };

  return <section className="editor-boundary-content" aria-label={props.language === 'zh' ? '边界线载荷列表' : 'Boundary line payload list'}>
    <div className="editor-payload-heading">
      <h3>{props.language === 'zh' ? '边界载荷' : 'Boundary payloads'} <span className="editor-field-muted">{line.payloads.length}</span></h3>
      <button type="button" className="editor-button" disabled={!canMutate || !line.newPayload} onClick={() => onCreateOpenChange(!createOpen)}>
        {createOpen ? (props.language === 'zh' ? '收起' : 'Close') : (props.language === 'zh' ? '添加载荷…' : 'Add payload…')}
      </button>
    </div>
    {!props.onBoundaryTransform && <p className="editor-field-muted">{props.language === 'zh' ? '当前工作台未提供边界载荷变换回调，添加和移除已停用。' : 'The current workbench has no boundary payload transform callback, so add and remove are disabled.'}</p>}
    <NewPayloadDialog {...props} line={line} open={createOpen} onOpenChange={onCreateOpenChange} />
    {line.payloads.length === 0 && <div className="editor-empty-state">{props.language === 'zh' ? '此边界线尚无载荷。' : 'This boundary line has no payloads.'}</div>}
    <div className="editor-payload-grid">
      {line.payloads.map((payload) => <PayloadListItem key={payload.id} payload={payload} line={line} series={series.find((item) => item.scope === payload.scope || item.scope.startsWith(`${payload.scope}/`))} {...props} onRemove={() => removePayload(payload)} />)}
    </div>
  </section>;
}
