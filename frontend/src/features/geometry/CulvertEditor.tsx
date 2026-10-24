import type { Language } from '../../shared/workbench';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import type { GeometryField, GeometryRow, GeometryTable } from './geometryTypes';

export interface CulvertEditorProps {
  language: Language;
  table?: GeometryTable;
  localRows: GeometryRow[];
  editable: boolean;
  busy: boolean;
  onChange: (row: GeometryRow, field: GeometryField, value: string, isLocal: boolean) => void;
  onDelete: (rows: GeometryRow[]) => void;
}

const culvertFieldOrder = [
  'Name', 'BarrelPropertyName', 'UpstreamSampledTerrainElevationDisplay', 'UpstreamInvert',
  'DownstreamSampledTerrainElevationDisplay', 'DownstreamInvert', 'UpstreamOpeningName', 'DownstreamOpeningName',
];

export function CulvertEditor(props: CulvertEditorProps) {
  return <GeometryFeatureDetailEditor {...props} kind="barrel" fieldOrder={culvertFieldOrder} readOnlyFieldIDs={['Length', 'VertexCount']} />;
}

export default CulvertEditor;
