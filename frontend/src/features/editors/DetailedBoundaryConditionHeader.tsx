import type { EditorPageProps } from './model';

/** Read-only boundary-condition name shown by the source detailed header. */
export function DetailedBoundaryConditionHeader(props: EditorPageProps) {
  return <header className="editor-schema-heading" aria-label={props.language === 'zh' ? '边界条件' : 'Boundary condition'}>
    <div>
      <p className="editor-eyebrow">{props.language === 'zh' ? '边界条件' : 'Boundary condition'}</p>
      <h2>{props.asset.name}</h2>
    </div>
  </header>;
}
