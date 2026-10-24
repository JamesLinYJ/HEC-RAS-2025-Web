import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { EditorPageProps } from './model';
import { NewPayloadPage } from './NewPayloadPage';
import { PayloadExternalConnectionFlow } from './PayloadExternalConnectionFlow';

type BoundaryLine = NonNullable<EditorPageProps['snapshot']['boundary']>['lines'][number];
const fieldPrefix = 'NewPayloadPage/ViewModel_';

type NewPayloadDialogProps = EditorPageProps & {
  line: BoundaryLine;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/** Source NewPayloadDialog: hosts the page and closes only after the backend accepted the draft transform. */
export function NewPayloadDialog({ line, open, onOpenChange, boundaryExternalConnection, ...props }: NewPayloadDialogProps) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [showExternalConnectionFlow, setShowExternalConnectionFlow] = useState(false);
  const [creatingExternalConnection, setCreatingExternalConnection] = useState(false);
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const latestLineRef = useRef(line);
  const currentLineIDRef = useRef(line.id);
  const submittingRef = useRef(false);
  const creatingExternalConnectionRef = useRef(false);
  latestLineRef.current = line;
  currentLineIDRef.current = line.id;

  // The parent owns navigation/unload protection, including this unsaved form.
  // Keep the same owner during a project-only connection receipt rebase.
  useEffect(() => {
    if (!open) return;
    props.onPayloadFormDirtyChange?.(line.id, true);
    return () => props.onPayloadFormDirtyChange?.(line.id, false);
  }, [open, line.id, props.onPayloadFormDirtyChange]);

  useEffect(() => {
    if (!open) {
      setShowExternalConnectionFlow(false);
      return;
    }
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    return () => { if (dialog?.open) dialog.close(); };
  }, [open, props.payloadDialogHost]);

  useEffect(() => {
    if (!open) return;
    const payload = latestLineRef.current.newPayload;
    if (!payload) return;
    setValues({ ...payload.fields });
    setError('');
    setShowExternalConnectionFlow(false);
  }, [line.id, open]);

  const readValue = (name: string) => values[`${fieldPrefix}${name}`] ?? '';
  const setValue = (name: string, value: string) => setValues((current) => ({ ...current, [`${fieldPrefix}${name}`]: value }));
  const close = () => {
    if (submittingRef.current || creatingExternalConnectionRef.current) return;
    setError('');
    setShowExternalConnectionFlow(false);
    if (dialogRef.current?.open) dialogRef.current.close();
    onOpenChange(false);
  };

  const setExternalConnectionCreating = (creating: boolean): void => {
    creatingExternalConnectionRef.current = creating;
    setCreatingExternalConnection(creating);
  };

  const acceptCreatedConnection = (connectionName: string, createdForLineID: string): void => {
    if (currentLineIDRef.current !== createdForLineID) {
      setError(props.language === 'zh'
        ? '连接已提交到工程，但边界线已切换；尚未将它选入当前载荷。请在目标边界线上重新打开载荷并选择此连接。'
        : 'The connection was committed to the project, but the boundary line changed. It was not selected for the current payload. Reopen the target line payload and choose this connection.');
      setShowExternalConnectionFlow(false);
      return;
    }
    setValues((current) => ({
      ...current,
      [`${fieldPrefix}SelectedExternalConnection`]: connectionName,
    }));
    setError('');
    setShowExternalConnectionFlow(false);
  };

  const submit = async (): Promise<void> => {
    if (!line.newPayload || !props.onBoundaryTransform || submittingRef.current || creatingExternalConnectionRef.current) return;
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
    submittingRef.current = true;
    setSubmitting(true);
    try {
      const accepted = await props.onBoundaryTransform(line.id, 'create', null, {
        ...values,
        [`${fieldPrefix}PayloadName`]: name,
      });
      if (accepted) {
        submittingRef.current = false;
        close();
      }
      else setError(props.language === 'zh' ? '服务端未接受此载荷草稿，请查看父级提示后重试。' : 'The server did not accept this payload draft. Check the workbench message and try again.');
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  if (!open || !line.newPayload || !props.payloadDialogHost) return null;
  const disabled = !props.snapshot.canEdit || !props.onBoundaryTransform || submitting || creatingExternalConnection;
  // The wizard owns a form; mount outside the model form while retaining the
  // editor feature's visibility/theme. React portal events still bubble, so
  // prevent wizard submits from invoking the parent model's save handler.
  return createPortal(<dialog ref={dialogRef} className="editor-payload-dialog" aria-label={props.language === 'zh' ? '新建边界载荷' : 'New boundary payload'} style={{ width: 'min(900px, calc(100vw - 32px))', maxHeight: '90vh', overflowY: 'auto', border: 0, borderRadius: 12, padding: 12 }} onSubmit={(event) => event.stopPropagation()} onCancel={(event) => { event.preventDefault(); close(); }}>
    {showExternalConnectionFlow
      ? <PayloadExternalConnectionFlow line={line} language={props.language} workflow={boundaryExternalConnection}
          onCreated={acceptCreatedConnection} onCancel={() => setShowExternalConnectionFlow(false)} onCreatingChange={setExternalConnectionCreating} />
      : <NewPayloadPage {...props} line={line} values={values} setValue={setValue} readValue={readValue} error={error}
          disabled={disabled} boundaryExternalConnection={boundaryExternalConnection}
          externalConnectionCreating={creatingExternalConnection}
          onCreateExternalConnection={() => setShowExternalConnectionFlow(true)}
          onCancel={close} onSubmit={() => { void submit(); }} />}
  </dialog>, props.payloadDialogHost);
}
