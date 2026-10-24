import type { EditorPageProps } from './model';
import { MeteorologyLayerControl } from './MeteorologyLayerControl';
import { WindConstantControl } from './WindConstantControl';

/** React port of Ras.Visual.WinUI.Controls.WindForcesControl. */
export function WindForcesControl(props: EditorPageProps) {
  return <MeteorologyLayerControl {...props} definition={{
    layerId: 'wind', schemaId: 'WindForcesControl',
    title: ['风力', 'Wind forces'], enabledField: 'WindLayer_IsEnabled', typeField: 'WindLayer_SpatialDataType',
    ratioField: 'WindLayer_Ratio', modes: ['Constant', 'Gridded', 'Point'], ratioUnit: ['比例', 'Ratio'],
  }} renderConstant={(pageProps) => <WindConstantControl {...pageProps} />} />;
}
