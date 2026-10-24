import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import type { GeometryRow } from './geometryTypes';

export type ConceptualMeshControlPointEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  onCommand?: (row: Parameters<GeometryTopologyTablePageProps['onChange']>[0], field: Parameters<GeometryTopologyTablePageProps['onChange']>[1], command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[]) => void;
};

const controlPointFieldOrder = [
  'PatchType', 'ConstantSize', 'SecondaryConstantSize', 'RotationDegrees', 'BiasInternal',
  'Multipoint', 'TinResolution', 'SizeTolerance',
];
const controlPointForceSetCommands = {
  PatchType: 'forceSetPatchType',
  Multipoint: 'forceSetRegionMultipoint',
};
const controlPointClearCommands = {
  ConstantSize: 'setRegionInput',
  SecondaryConstantSize: 'setRegionInput',
  RotationDegrees: 'setRegionInput',
  BiasInternal: 'setRegionInput',
  TinResolution: 'setRegionInput',
  SizeTolerance: 'setRegionInput',
};

export function ConceptualMeshControlPointEditor({ onCommand, onSelectionChange, canEdit, ...props }: ConceptualMeshControlPointEditorProps) {
  return (
    <GeometryFeatureDetailEditor
      {...props}
      kind="region"
      editable={canEdit}
      fieldOrder={controlPointFieldOrder}
      readOnlyFieldIDs={['X', 'Y']}
      clearCommands={controlPointClearCommands}
      forceSetCommands={controlPointForceSetCommands}
      allowLocalAdd
      onCommand={onCommand}
      onSelectionChange={onSelectionChange}
    />
  );
}

export default ConceptualMeshControlPointEditor;
