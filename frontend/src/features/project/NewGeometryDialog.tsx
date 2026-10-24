import { useState } from 'react';
import type { FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import { ModelCreationDialogFrame } from './ModelCreationDialogFrame';
import { NewGeometryControl } from './NewGeometryControl';
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
  'NewGeometryControl/GeometryName': '',
  'NewGeometryControl/SelectedTerrain': '',
  'NewGeometryControl/SelectedNValue': '',
};

export function NewGeometryDialog(props: Props) {
  const { language, backend, context, open, blockedReason, hasUnsavedDrafts = false, onClose, onContextCommit, onCreated } = props;
  const [fields, setFields] = useState<ModelCreationFields>(initialFields);
  const workflow = useProjectModelCreation({ backend, context, language, kind: 'newGeometry', fields, open, hasUnsavedDrafts, onContextCommit, onCreated });
  const changeField = (field: string, value: string): void => {
    setFields((current) => ({ ...current, [field]: value }));
    workflow.invalidate();
  };
  return <ModelCreationDialogFrame
    language={language}
    open={open}
    title={language === 'zh' ? '新建几何' : 'New geometry'}
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
    <NewGeometryControl language={language} fields={fields} inspection={workflow.inspection} disabled={workflow.busy !== null} onChange={changeField} />
  </ModelCreationDialogFrame>;
}
