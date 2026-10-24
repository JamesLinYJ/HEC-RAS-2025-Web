import type { GeometryTopologyTablePageProps } from './GeometryTopologyTablePage';
import { GeometryFeatureDetailEditor } from './GeometryFeatureDetailEditor';
import type { GeometryRow } from './geometryTypes';

export type ConceptualMeshArcEditorProps = Omit<GeometryTopologyTablePageProps, 'kind'> & {
  onCommand?: (row: Parameters<GeometryTopologyTablePageProps['onChange']>[0], field: Parameters<GeometryTopologyTablePageProps['onChange']>[1], command: string, value?: string) => void;
  onSelectionChange?: (rows: GeometryRow[]) => void;
};

const arcFieldOrder = [
  'SizeFunction', 'PointCount', 'Bias', 'ConstantSize', 'ConstrainStretching', 'LaplaceBC',
  'DistanceStraightLine', 'AreaWeight', 'DiscretizeMagnetism', 'FollowExactly',
];

const arcClearCommands = { Bias: 'forceResetArcBias' };
const arcForceSetCommands = {
  ConstrainStretching: 'forceSetArcChoice',
  LaplaceBC: 'forceSetArcChoice',
};

export function ConceptualMeshArcEditor({ onCommand, onSelectionChange, canEdit, ...props }: ConceptualMeshArcEditorProps) {
  return (
    <GeometryFeatureDetailEditor
      {...props}
      kind="arc"
      editable={canEdit}
      fieldOrder={arcFieldOrder}
      readOnlyFieldIDs={['Length']}
      allowLocalAdd
      clearCommands={arcClearCommands}
      forceSetCommands={arcForceSetCommands}
      onCommand={onCommand}
      onSelectionChange={onSelectionChange}
    />
  );
}

export default ConceptualMeshArcEditor;
