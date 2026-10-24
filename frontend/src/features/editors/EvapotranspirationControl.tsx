import type { EditorPageProps } from './model';
import { MeteorologyLayerControl } from './MeteorologyLayerControl';
import { MetDataUnits } from './MetDataUnits';
import { EvapotranspirationMetDataMode } from './EvapotranspirationMetDataMode';

/** React port of Ras.Visual.WinUI.Controls.EvapotranspirationControl. */
export function EvapotranspirationControl(props: EditorPageProps) {
  const spatial = props.snapshot.boundary?.spatial?.evapotranspiration;
  return <>
    <MeteorologyLayerControl {...props} definition={{
      layerId: 'evapotranspiration', schemaId: 'EvapotranspirationControl',
      title: ['蒸散', 'Evapotranspiration'], enabledField: 'EvapotranspirationLayer_IsEnabled', typeField: 'EvapotranspirationLayer_SpatialDataType',
      ratioField: 'EvapotranspirationLayer_Ratio', constantField: 'EvapotranspirationLayer_ConstantValue', modes: [...EvapotranspirationMetDataMode],
      ratioUnit: ['比例', 'Ratio'],
    }} />
    {!spatial?.valueUnits.choices.length && <div className="editor-meteorology-unit-options" aria-label={props.language === 'zh' ? '蒸散单位选项' : 'Evapotranspiration unit options'}>
      <span>{props.language === 'zh' ? '原控件单位选项' : 'Source control unit options'}</span>
      {MetDataUnits.map((unit) => <span key={unit}>{unit}</span>)}
      <small>{props.language === 'zh' ? '此边界条件的源单位为 mm/hr。' : 'This boundary condition source unit is mm/hr.'}</small>
    </div>}
  </>;
}
