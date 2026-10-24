import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { ModelCreationDialogFrame } from './ModelCreationDialogFrame';
import { NewPlanControl } from './NewPlanControl';
import type { CreatedModel, ModelCreationFields } from './ProjectModelCreationWorkflow';
import { useProjectModelCreation } from './ProjectModelCreationWorkflow';

interface Props {
  language: FeaturePageProps['language'];
  backend: FeaturePageProps['backend'];
  context: WorkbenchContext;
  open: boolean;
  blockedReason?: string;
  hasUnsavedDrafts?: boolean;
  onClose: () => void;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onCreated?: (source: WorkbenchContext, committed: WorkbenchContext, created: CreatedModel) => void;
}

const initialFields: ModelCreationFields = {
  'NewPlanControl/PlanName': '',
  'NewPlanControl/SelectedGeometry': '',
  'NewPlanControl/SelectedBoundaryCondition': '',
  'NewPlanControl/TimeWindowMode': 'BoundaryCondition',
  'NewPlanControl/StartTime': '',
  'NewPlanControl/EndTime': '',
};

export function NewPlanDialog(props: Props) {
  const { language, backend, context, open, blockedReason, hasUnsavedDrafts = false, onClose, onContextCommit, onCreated } = props;
  const [fields, setFields] = useState<ModelCreationFields>(initialFields);
  const defaultsApplied = useRef(false);
  const workflow = useProjectModelCreation({ backend, context, language, kind: 'newPlan', fields, open, hasUnsavedDrafts, onContextCommit, onCreated });
  const changeField = (field: string, value: string): void => {
    setFields((current) => ({ ...current, [field]: value }));
    workflow.invalidate();
  };

  useEffect(() => {
    if (!open) {
      defaultsApplied.current = false;
      setFields(initialFields);
      workflow.invalidate();
      return;
    }
    if (!workflow.inspection || defaultsApplied.current) return;
    const inspection = workflow.inspection;
    const geometryKey = 'NewPlanControl/SelectedGeometry';
    const boundaryKey = 'NewPlanControl/SelectedBoundaryCondition';
    const geometry = fields[geometryKey] || inspection.choices?.[geometryKey]?.[0]?.id || '';
    const boundary = fields[boundaryKey] || inspection.choices?.[boundaryKey]?.[0]?.id || '';
    const window = inspection.boundaryTimeWindows?.[boundary];
    if (!geometry && !boundary) return;
    defaultsApplied.current = true;
    setFields((current) => ({
      ...current,
      [geometryKey]: geometry,
      [boundaryKey]: boundary,
      'NewPlanControl/TimeWindowMode': current['NewPlanControl/TimeWindowMode'] || 'BoundaryCondition',
      ...(window ? { 'NewPlanControl/StartTime': window.start, 'NewPlanControl/EndTime': window.end } : {}),
    }));
    workflow.invalidate();
  }, [fields, open, workflow.inspection, workflow.invalidate]);

  return <ModelCreationDialogFrame
    language={language}
    open={open}
    title={language === 'zh' ? '新建方案' : 'New plan'}
    kicker={language === 'zh' ? '工程模型' : 'PROJECT MODEL'}
    path={context.projectPath}
    revision={context.projectRevision}
    busy={workflow.busy}
    canInspect={backend.supports('modelCreation.inspect')}
    canCreate={workflow.canCreate}
    blockedReason={blockedReason}
    hasUnsavedDrafts={hasUnsavedDrafts}
    inspection={workflow.inspection}
    error={workflow.error}
    details={workflow.details}
    notice={workflow.notice}
    onClose={onClose}
    onInspect={() => void workflow.inspect()}
    onCreate={() => void workflow.create()}
  >
    <NewPlanControl language={language} fields={fields} inspection={workflow.inspection} disabled={workflow.busy !== null} onChange={changeField} />
  </ModelCreationDialogFrame>;
}
