import type { EditorPageProps } from './model';
import { DetailedBCLineHeaderControl } from './DetailedBCLineHeaderControl';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];

/** Detail header for the line currently selected in the boundary-line list. */
export function BCLineLayerLastSelectedFeatureLineEditor({ line, ...props }: EditorPageProps & { line: BoundaryLine }) {
  return <section className="editor-boundary-content" aria-label={props.language === 'zh' ? '当前边界线' : 'Current boundary line'} data-line-id={line.id}>
    <DetailedBCLineHeaderControl {...props} line={line} />
    <dl className="editor-readonly-list">
      <dt>{props.language === 'zh' ? '边界线范围' : 'Boundary line scope'}</dt>
      <dd><code>{line.scope}</code></dd>
      <dt>{props.language === 'zh' ? '载荷数量' : 'Payload count'}</dt>
      <dd>{line.payloads.length}</dd>
    </dl>
  </section>;
}
