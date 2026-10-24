import type { EditorPageProps, ModelSnapshot } from './model';
import { LeveeModEditor } from './LeveeModEditor';
import { LeveeModPropertiesEditor } from './LeveeModPropertiesEditor';

const featureTableID = 'LeveeModsTable';
const propertyTableID = 'LeveeModPropertiesEditor/PropertySets';

type TerrainInput = NonNullable<ModelSnapshot['terrain']>['inputs'][number];

/** Levee input projection, joining only the feature and property editors returned for that input. */
export function LeveeModTerrainInputEditor(props: EditorPageProps & { selected: TerrainInput }) {
  const { snapshot, draft, selected, schemas } = props;
  const terrain = draft.terrainView ?? snapshot.terrain;
  if (terrain?.selectedId !== selected.id) return null;
  const tables = terrain.tables ?? {};
  const hasFeatures = tables[featureTableID] !== undefined;
  const hasProperties = tables[propertyTableID] !== undefined;
  if (!hasFeatures && !hasProperties) return null;
  const detailSchema = schemas.find((schema) => schema.id === 'LeveeModDetailEditor');
  const propertySchema = schemas.find((schema) => schema.id === 'LeveeModPropertiesDetailEditor');
  return <section className="editor-field-sections editor-levee-input-editor" aria-label={props.language === 'zh' ? '挖填输入编辑器' : 'Levee input editor'}>
    {hasFeatures && <LeveeModEditor {...props} selected={selected} schema={detailSchema} />}
    {hasProperties && <LeveeModPropertiesEditor {...props} selected={selected} schema={propertySchema} />}
  </section>;
}
