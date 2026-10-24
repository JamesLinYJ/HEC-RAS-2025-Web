import { useEffect, useRef, useState } from 'react';
import type { EditorPageProps } from './model';
import { NewPayloadPage } from './NewPayloadPage';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];
const fieldPrefix = 'NewPayloadPage/ViewModel_';

/** Source NewPayloadDialog: hosts the page and closes only after the backend accepted the draft transform. */
export function NewPayloadDialog({ line, open, onOpenChange, ...props }: EditorPageProps & {
  line: BoundaryLine;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const dialogRef = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    if (!open) return;
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    return () => { if (dialog?.open) dialog.close(); };
  }, [open]);

  useEffect(() => {
    if (!open || !line.newPayload) return;
    setValues({ ...line.newPayload.fields });
    setError('');
  }, [line.id, line.newPayload, open]);

  const readValue = (name: string) => values[`${fieldPrefix}${name}`] ?? '';
  const setValue = (name: string, value: string) => setValues((current) => ({ ...current, [`${fieldPrefix}${name}`]: value }));
  const close = () => {
    setError('');
    if (dialogRef.current?.open) dialogRef.current.close();
    onOpenChange(false);
  };

  const submit = async (): Promise<void> => {
    if (!line.newPayload || !props.onBoundaryTransform || submitting) return;
    const name = readValue('PayloadName').trim();
    const variable = readValue('HydraulicType');
    const dataType = readValue('HydraulicDataType');
    if (!name) {
      setError(props.language === 'zh' ? '请输入载荷名称。' : 'Enter a payload name.');
      return;
    }
    const typeChoices = line.newPayload.choices[`${fieldPrefix}HydraulicDataType`] ?? [];
    if (!typeChoices.some((choice) => choice.id === dataType)) {
      setError(props.language === 'zh' ? '请选择服务端支持的数据类型。' : 'Choose a data type supported by the server.');
      return;
    }
    const variableChoices = line.newPayload.choices[`${fieldPrefix}HydraulicType`] ?? [];
    if (!variableChoices.some((choice) => choice.id === variable)) {
      setError(props.language === 'zh' ? '请选择服务端支持的水力变量。' : 'Choose a hydraulic variable supported by the server.');
      return;
    }
    if (variable === 'Normal Depth' && dataType !== 'Constant' || variable === 'StageVelocityVelocity' && dataType !== 'SpatiallyVariableTabular') {
      setError(props.language === 'zh' ? '此水力变量不支持所选数据类型。' : 'This hydraulic variable does not support the selected data type.');
      return;
    }
    if (dataType === 'Constant') {
      const value = readValue('HydraulicConstantValue');
      if (value.trim() === '' || !Number.isFinite(Number(value))) {
        setError(props.language === 'zh' ? '请输入有效的恒定值。' : 'Enter a valid constant value.');
        return;
      }
    }
    if ((dataType === 'Tabular' || dataType === 'SpatiallyVariableTabular')
      && !(line.newPayload.choices[`${fieldPrefix}TimeSeriesType`] ?? []).some((choice) => choice.id === readValue('TimeSeriesType'))) {
      setError(props.language === 'zh' ? '请选择服务端支持的时序类型。' : 'Choose a time-series type supported by the server.');
      return;
    }
    if (dataType === 'Remote') {
      const connection = readValue('SelectedExternalConnection');
      if (!(line.newPayload.choices[`${fieldPrefix}SelectedExternalConnection`] ?? []).some((choice) => choice.id === connection)) {
        setError(props.language === 'zh' ? '请先选择有效的外部连接。' : 'Select a valid external connection first.');
        return;
      }
    }
    setSubmitting(true);
    try {
      const accepted = await props.onBoundaryTransform(line.id, 'create', null, {
        ...values,
        [`${fieldPrefix}PayloadName`]: name,
      });
      if (accepted) close();
      else setError(props.language === 'zh' ? '服务端未接受此载荷草稿，请查看父级提示后重试。' : 'The server did not accept this payload draft. Check the workbench message and try again.');
    } finally {
      setSubmitting(false);
    }
  };

  if (!open || !line.newPayload) return null;
  const disabled = !props.snapshot.canEdit || !props.onBoundaryTransform || submitting;
  return <dialog ref={dialogRef} className="editor-payload-dialog" aria-label={props.language === 'zh' ? '新建边界载荷' : 'New boundary payload'} style={{ width: 'min(900px, calc(100vw - 32px))', maxHeight: '90vh', overflowY: 'auto', border: 0, borderRadius: 12, padding: 12 }} onCancel={(event) => { event.preventDefault(); close(); }}>
    <NewPayloadPage {...props} line={line} values={values} setValue={setValue} readValue={readValue} error={error} disabled={disabled} onCancel={close} onSubmit={() => { void submit(); }} />
  </dialog>;
}
