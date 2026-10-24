import type { EditorPageProps } from './model';
import type { SpatialBoundaryLayerID } from './SpatialDataHeader';
import { GriddedDataImporterDialog } from './GriddedDataImporterDialog';

/** Spatial-boundary import entry point; the hosted importer owns its inspection and commit lifecycle. */
export function SpatialBoundaryConditionRasterImporter(props: EditorPageProps & {
  layerId: SpatialBoundaryLayerID;
  onClose: () => void;
}) {
  return <GriddedDataImporterDialog {...props} />;
}
