import type { EditorPageProps } from './model';

type ClassificationInput = NonNullable<EditorPageProps['snapshot']['classification']>['inputs'][number];

/** Classification values for the polygon features in the selected source input. */
export function PolygonClassificationInputProperties({ props, input }: { props: EditorPageProps; input: ClassificationInput }) {
  const { language, snapshot, draft } = props;
  const classes = snapshot.classification?.classes ?? [];
  const listID = `classification-classes-${encodeURIComponent(input.id)}`;

  return (
    <section className="editor-table-card" aria-label={language === 'zh' ? '多边形分类属性' : 'Polygon classification properties'}>
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '多边形分类' : 'Polygon classification'}</h3>
        <span>{input.polygons.length} {language === 'zh' ? '个要素' : 'features'}</span>
      </div>
      {input.polygons.length === 0 ? <div className="editor-empty-state">{language === 'zh' ? '该输入没有多边形要素。' : 'This input has no polygon features.'}</div> : (
        <div className="editor-polygon-list">
          {input.polygons.map((polygon, index) => {
            const fieldID = `${polygon.scope}/PolygonClassificationInputProperties/ClassifierMod_Classification`;
            return (
              <div className="editor-polygon-row" key={polygon.id}>
                <span>{language === 'zh' ? `要素 ${index + 1} · ${polygon.vertexCount} 个顶点` : `Feature ${index + 1} · ${polygon.vertexCount} vertices`}</span>
                <input
                  type="text"
                  value={draft.fields[fieldID] ?? polygon.classification}
                  list={listID}
                  disabled={!snapshot.canEdit}
                  aria-label={language === 'zh' ? `第 ${index + 1} 个多边形分类` : `Classification for polygon ${index + 1}`}
                  onChange={(event) => props.onFieldChange(fieldID, event.currentTarget.value)}
                />
              </div>
            );
          })}
          <datalist id={listID}>{classes.map((value) => <option value={value} key={value} />)}</datalist>
        </div>
      )}
    </section>
  );
}
