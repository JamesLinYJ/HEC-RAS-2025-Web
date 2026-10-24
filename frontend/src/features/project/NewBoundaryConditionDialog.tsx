import { useEffect, useRef, useState } from 'react';
import type { FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { ModelCreationDialogFrame } from './ModelCreationDialogFrame';
import { NewBoundaryConditionControl } from './NewBoundaryConditionControl';
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
  'NewBoundaryConditionControl/BoundaryConditionName': '',
  'NewBoundaryConditionControl/StartTime': '',
  'NewBoundaryConditionControl/EndTime': '',
};

export function NewBoundaryConditionDialog(props: Props) {
  const { language, backend, context, open, blockedReason, hasUnsavedDrafts = false, onClose, onContextCommit, onCreated } = props;
  const [fields, setFields] = useState<ModelCreationFields>(initialFields);
  const defaultsApplied = useRef(false);
  const workflow = useProjectModelCreation({ backend, context, language, kind: 'newBoundary', fields, open, hasUnsavedDrafts, onContextCommit, onCreated });
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
    const defaults = workflow.inspection?.boundaryDefaults;
    if (!defaults || defaultsApplied.current || fields['NewBoundaryConditionControl/BoundaryConditionName']) return;
    defaultsApplied.current = true;
    setFields((current) => ({
      ...current,
      'NewBoundaryConditionControl/BoundaryConditionName': defaults.name,
      'NewBoundaryConditionControl/StartTime': defaults.start,
      'NewBoundaryConditionControl/EndTime': defaults.end,
    }));
    workflow.invalidate();
  }, [fields, open, workflow.inspection, workflow.invalidate]);

  return <ModelCreationDialogFrame
    language={language}
    open={open}
    title={language === 'zh' ? '新建边界条件' : 'New boundary condition'}
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
    <NewBoundaryConditionControl language={language} fields={fields} inspection={workflow.inspection} disabled={workflow.busy !== null} onChange={changeField} />
  </ModelCreationDialogFrame>;
}
