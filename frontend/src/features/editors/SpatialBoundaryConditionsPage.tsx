import type { EditorPageProps } from './model';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';
import { SpatialBoundaryConditions } from './SpatialBoundaryConditions';

export function SpatialBoundaryConditionsPage(props: EditorPageProps & { layerId: SpatialBoundaryLayerID }) {
  return <div className="editor-meteorology-page">
    <SpatialBoundaryConditions {...props} />
  </div>;
}
