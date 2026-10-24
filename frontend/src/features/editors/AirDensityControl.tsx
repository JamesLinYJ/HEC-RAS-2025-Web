import type { EditorPageProps } from './model';
import { MeteorologyLayerControl } from './MeteorologyLayerControl';

/** React port of Ras.Visual.WinUI.Controls.AirDensityControl. */
export function AirDensityControl(props: EditorPageProps) {
  return <MeteorologyLayerControl {...props} definition={{
    layerId: 'airDensity', schemaId: 'AirDensityControl',
    title: ['空气密度', 'Air density'], enabledField: 'AirDensityLayer_IsEnabled', typeField: 'AirDensityLayer_SpatialDataType',
    ratioField: 'AirDensityLayer_Ratio', constantField: 'AirDensityLayer_ConstantValue', modes: ['Constant', 'Gridded', 'Point'],
    constantUnit: ['kg/m³（项目单位）', 'kg/m³ (project units)'], ratioUnit: ['比例', 'Ratio'],
  }} />;
}
