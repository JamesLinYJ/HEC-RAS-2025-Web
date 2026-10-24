import type { EditorPageProps } from './model';
import { MeteorologyLayerControl } from './MeteorologyLayerControl';
import { AirPressureUnits } from './AirPressureUnits';

/** React port of Ras.Visual.WinUI.Controls.AirPressureControl. */
export function AirPressureControl(props: EditorPageProps) {
  const data = props.snapshot.boundary?.spatial?.airPressure;
  return <>
    <MeteorologyLayerControl {...props} definition={{
      layerId: 'airPressure', schemaId: 'AirPressureControl',
      title: ['气压', 'Air pressure'], enabledField: 'AirPressureLayer_IsEnabled', typeField: 'AirPressureLayer_SpatialDataType',
      constantField: 'AirPressureLayer_ConstantValue', modes: ['Constant', 'Gridded', 'Point'],
      constantUnit: ['项目气压单位', 'Project pressure units'],
    }} />
    <div className="editor-meteorology-unit-options" aria-label={props.language === 'zh' ? '气压单位' : 'Air pressure units'}>
      {AirPressureUnits.map((unit) => <span key={unit}>{unit}</span>)}
      {data?.unit && <small>{(props.language === 'zh' ? '当前快照单位：' : 'Snapshot unit: ') + data.unit}</small>}
    </div>
  </>;
}
