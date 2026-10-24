import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { RasterCalculatorConstantDraft } from './RasterCalculatorRecipe';

interface Props {
  language: Language;
  value: RasterCalculatorConstantDraft;
  onChange: (value: RasterCalculatorConstantDraft) => void;
  onRemove: () => void;
}

export function RasterCalculatorConstantRecipe({ language, value, onChange, onRemove }: Props) {
  return <div className="tools-constant-row tools-calculator-constant-row" data-testid="raster-calculator-constant-row">
    <label className="tools-field"><span>{text(language, '常量名称', 'Constant name')}</span>
      <input aria-label={text(language, '常量名称', 'Constant name')} value={value.name}
        onChange={event => onChange({ ...value, name: event.target.value })} spellCheck={false} />
    </label>
    <label className="tools-field"><span>{text(language, '有限数值', 'Finite value')}</span>
      <input aria-label={text(language, '常量数值', 'Constant value')} inputMode="decimal" value={value.value}
        onChange={event => onChange({ ...value, value: event.target.value })} />
    </label>
    <button className="tools-remove-button" type="button" aria-label={text(language, '移除常量', 'Remove constant')} onClick={onRemove}>×</button>
  </div>;
}
