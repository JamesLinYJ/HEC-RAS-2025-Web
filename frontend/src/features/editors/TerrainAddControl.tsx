import type { EditorPageProps } from './model';
import { TerrainInputLayerCreator } from './TerrainInputLayerCreator';

/** Type-specific add actions supported by model.terrainTransform. */
export function TerrainAddControl({ language, canEdit, onTerrainTransform }: Pick<EditorPageProps, 'language' | 'onTerrainTransform'> & { canEdit: boolean }) {
  return <TerrainInputLayerCreator language={language} canEdit={canEdit} onTerrainTransform={onTerrainTransform} />;
}
