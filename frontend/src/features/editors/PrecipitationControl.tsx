import type { EditorPageProps } from './model';
import { MeteorologyLayerControl } from './MeteorologyLayerControl';
import { MetDataUnits } from './MetDataUnits';

/** React port of Ras.Visual.WinUI.Controls.PrecipitationControl. */
export function PrecipitationControl(props: EditorPageProps) {
  const spatial = props.snapshot.boundary?.spatial?.precipitation;
  return <>
    <MeteorologyLayerControl {...props} definition={{
      layerId: 'precipitation', schemaId: 'PrecipitationControl',
      title: ['降水', 'Precipitation'], enabledField: 'PrecipitationLayer_IsEnabled', typeField: 'PrecipitationLayer_SpatialDataType',
      ratioField: 'PrecipitationLayer_Ratio', constantField: 'PrecipitationLayer_ConstantValue', modes: ['Constant', 'Gridded', 'Point'],
      ratioUnit: ['比例', 'Ratio'],
    }} />
    {!spatial?.valueUnits.choices.length && <div className="editor-meteorology-unit-options" aria-label={props.language === 'zh' ? '降水单位选项' : 'Precipitation unit options'}>
      <span>{props.language === 'zh' ? '原控件单位选项' : 'Source control unit options'}</span>
      {MetDataUnits.map((unit) => <span key={unit}>{unit}</span>)}
    </div>}
  </>;
}
