import { isEditorFieldReadOnly, type EditorPageProps } from './model';
import { UnitAwareNumberField } from './UnitAwareNumberField';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];
type BoundaryPayload = BoundaryLine['payloads'][number];

/** React port of the source ConstantPayloadEditor; values stay in source units. */
export function ConstantPayloadEditor({ line, payload, ...props }: EditorPageProps & { line: BoundaryLine; payload: BoundaryPayload }) {
  const fieldID = `${payload.scope}/ConstantPayloadEditor/ConstantPayload_Value`;
  const value = props.draft.fields[fieldID] ?? props.snapshot.fields[fieldID] ?? '';
  const schemaField = props.schemas.find((schema) => schema.id === 'ConstantPayloadEditor')?.fields.find((field) => field.id === 'ConstantPayload_Value')
    ?? { id: 'ConstantPayload_Value', title: 'Value', kind: 'number', sourceType: 'double' };
  const unit = line.newPayload?.constantUnits[payload.variable];
  return <UnitAwareNumberField
    backend={props.backend}
    id={`payload-${payload.id}-constant-value`}
    title={props.language === 'zh' ? '恒定值' : 'Constant value'}
    value={value}
    baseline={props.snapshot.fields[fieldID]}
    field={schemaField}
    unitData={payload.valueUnits ?? undefined}
    unitLabel={unit}
    disabled={!props.snapshot.canEdit || isEditorFieldReadOnly(props, fieldID)}
    language={props.language}
    onChange={(next) => props.onFieldChange(fieldID, next)}
  />;
}
