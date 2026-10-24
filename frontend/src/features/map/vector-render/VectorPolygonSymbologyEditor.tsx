import type { Language } from '../../../shared/workbench';
import type { MapVectorPlotStyle } from './vectorRenderTypes';
import { VectorStyleControl, type VectorStyleProperty } from './VectorStyleControl';

export function VectorPolygonSymbologyEditor({
  style,
  properties,
  disabled,
  language,
  onChange,
  onValidityChange,
}: {
  style: MapVectorPlotStyle;
  properties: readonly VectorStyleProperty[];
  disabled: boolean;
  language: Language;
  onChange: (definitionID: string, property: VectorStyleProperty, value: string | number) => void;
  onValidityChange?: (definitionID: string, property: VectorStyleProperty, invalid: boolean) => void;
}) {
  return (
    <fieldset className="vector-render-style-fields" disabled={disabled} data-testid={`vector-polygon-style-${style.definitionID}`}>
      <legend>{language === 'zh' ? '多边形符号' : 'Polygon symbology'}</legend>
      {properties.map(property => <VectorStyleControl key={property} property={property} values={style.base}
        disabled={disabled} language={language} onChange={(_, value) => onChange(style.definitionID, property, value)}
        onValidityChange={(_, invalid) => onValidityChange?.(style.definitionID, property, invalid)} />)}
    </fieldset>
  );
}
