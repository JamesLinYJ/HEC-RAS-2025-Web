import type { EditorPageProps } from './model';

/** Displays the current air-pressure spatial mode; the editable selector remains in the layer form. */
export function AirPressureHeader(props: EditorPageProps) {
  const fieldId = 'AirPressureLayer_SpatialDataType';
  const fieldKey = `AirPressureControl/${fieldId}`;
  const value = props.draft.fields[fieldKey] ?? props.snapshot.fields[fieldKey] ?? '';
  const schema = props.schemas.find((item) => item.id === 'AirPressureControl');
  const choices = props.snapshot.fieldChoices?.[fieldKey] ?? schema?.fields.find((field) => field.id === fieldId)?.choices ?? [];
  const selected = choices.find((choice) => choice.id === value);
  const zhModes: Record<string, string> = { Constant: '常数', Gridded: '格网', Point: '测站输入' };
  const mode = props.language === 'zh' ? zhModes[value] ?? selected?.title ?? value : selected?.title ?? value;
  return <header className="editor-schema-heading editor-air-pressure-header">
    <div>
      <p className="editor-eyebrow">{props.language === 'zh' ? '气象边界条件' : 'Meteorological boundary condition'}</p>
      <h2>{props.language === 'zh' ? '气压' : 'Air pressure'}</h2>
      <p className="editor-field-muted">
        {mode
          ? `${props.language === 'zh' ? '当前空间数据类型：' : 'Current spatial data type: '}${mode}`
          : props.language === 'zh' ? '服务快照未提供当前空间数据类型。' : 'The service snapshot does not include the current spatial data type.'}
      </p>
    </div>
  </header>;
}
