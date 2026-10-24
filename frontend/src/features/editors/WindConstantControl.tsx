import type { EditorPageProps } from './model';
import type { DisplayUnitData } from './model';
import { UnitAwareNumberField } from './UnitAwareNumberField';
import { WindSpeedUnits } from './WindSpeedUnits';

/** React port of Ras.Visual.WinUI.Controls.WindConstantControl (ConstantVx/Vy). */
export function WindConstantControl(props: EditorPageProps) {
  const { language, snapshot } = props;
  const schema = props.schemas.find((item) => item.id === 'WindConstantControl');
  const spatial = snapshot.boundary?.spatial?.wind;
  const unit = spatial?.unit;
  return <fieldset className="editor-wind-constant">
    <legend>{language === 'zh' ? '恒定风速分量' : 'Constant wind components'}</legend>
    <p className="editor-field-muted">{language === 'zh' ? '分别设置 X 与 Y 方向风速；源单位由项目单位系统决定。' : 'Set the X and Y wind components. Source units follow the project unit system.'}</p>
    <div className="editor-field-grid">
      <WindComponent {...props} schema={schema} fieldID="Constant1" title={language === 'zh' ? 'X 方向风速' : 'X wind component'} unitData={spatial?.valueUnits} unitLabel={unit} />
      <WindComponent {...props} schema={schema} fieldID="Constant2" title={language === 'zh' ? 'Y 方向风速' : 'Y wind component'} unitData={spatial?.valueUnits} unitLabel={unit} />
    </div>
    {!spatial?.valueUnits.choices.length && <div className="editor-meteorology-unit-options" aria-label={language === 'zh' ? '风速单位' : 'Wind speed units'}>{WindSpeedUnits.map((value) => <span key={value}>{value}</span>)}{unit && <small>{(language === 'zh' ? '当前快照单位：' : 'Snapshot unit: ') + unit}</small>}</div>}
  </fieldset>;
}

function WindComponent(props: EditorPageProps & { schema?: EditorPageProps['schemas'][number]; fieldID: 'Constant1' | 'Constant2'; title: string; unitData?: DisplayUnitData; unitLabel?: string }) {
  const { language, snapshot, draft } = props;
  const fieldID = 'WindConstantControl/' + props.fieldID;
  const schemaField = props.schema?.fields.find((field) => field.id === props.fieldID) ?? { id: props.fieldID, title: props.fieldID, kind: 'number', sourceType: 'double' };
  const value = draft.fields[fieldID] ?? snapshot.fields[fieldID] ?? '';
  const readOnly = !snapshot.canEdit || snapshot.readOnlyFields?.includes(fieldID) === true;
  return <UnitAwareNumberField backend={props.backend} id={fieldID} title={props.title} value={value} baseline={snapshot.fields[fieldID]} field={schemaField} unitData={props.unitData} unitLabel={props.unitLabel} disabled={readOnly} language={language} onChange={(sourceValue) => props.onFieldChange(fieldID, sourceValue)} />;
}
