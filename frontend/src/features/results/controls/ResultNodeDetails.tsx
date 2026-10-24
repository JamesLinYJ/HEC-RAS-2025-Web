import type { Language } from '../../../shared/workbench';
import { DetailedResultHeader } from './DetailedResultHeader';
import { PlanNodeDetails } from './PlanNodeDetails';
import { ResultPlanAssociationsViewer } from './ResultPlanAssociationsViewer';
import { ResultTerrainAssociation } from './ResultTerrainAssociation';
import type {
  ResultInspectorSnapshot,
  ResultPlanAssociationsSnapshot,
  ResultPlanContextSnapshot,
  ResultPlanSettingsSnapshot,
} from './resultDetailsTypes';

export type { ResultInspectorSnapshot } from './resultDetailsTypes';

interface ResultNodeDetailsProps {
  snapshot: ResultInspectorSnapshot | null;
  loading: boolean;
  error: string | null;
  language: Language;
  planAssociations: ResultPlanAssociationsSnapshot | null;
  planAssociationsLoading: boolean;
  planAssociationsError: string | null;
  planContext: ResultPlanContextSnapshot | null;
  planContextLoading: boolean;
  planContextError: string | null;
  planSettings: ResultPlanSettingsSnapshot | null;
  planSettingsLoading: boolean;
  planSettingsError: string | null;
  canEditPlanDescription: boolean;
  savingPlanSettings: boolean;
  planSettingsSaveError: string | null;
  planSettingsSaveStatus: string | null;
  descriptionOwner: string;
  onDescriptionDirtyChange?: (owner: string, dirty: boolean) => void;
  onSavePlanDescription?: (description: string) => void;
}

export function ResultNodeDetails({
  snapshot, loading, error, language,
  planAssociations, planAssociationsLoading, planAssociationsError,
  planContext, planContextLoading, planContextError,
  planSettings, planSettingsLoading, planSettingsError,
  canEditPlanDescription, savingPlanSettings, planSettingsSaveError, planSettingsSaveStatus,
  descriptionOwner, onDescriptionDirtyChange, onSavePlanDescription,
}: ResultNodeDetailsProps) {
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;
  if (loading || error || !snapshot) return <div className="results-details-compact">
    <span className={'results-state-dot ' + (loading ? 'is-loading' : '')} />
    <span>{loading ? text('正在检查 HDF 结果元数据…', 'Inspecting HDF result metadata…') : error ? <>{text('结果读取状态：', 'Result read status: ')}<code>{error}</code></> : text('选择工程中的 HDF 结果后可查看源文件详情。', 'Select an HDF result from the project to inspect its source details.')}</span>
  </div>;

  return <DetailedResultHeader snapshot={snapshot} language={language}>
    <ResultPlanAssociationsViewer
      language={language}
      snapshot={planAssociations}
      inspector={snapshot}
      loading={planAssociationsLoading}
      error={planAssociationsError}
    />
    <ResultTerrainAssociation language={language} snapshot={snapshot} />
    <PlanNodeDetails
      language={language}
      settings={planSettings}
      planContext={planContext}
      loading={planSettingsLoading || planContextLoading}
      settingsError={planSettingsError}
      contextError={planContextError}
      canEditDescription={canEditPlanDescription}
      saving={savingPlanSettings}
      saveError={planSettingsSaveError}
      saveStatus={planSettingsSaveStatus}
      descriptionOwner={descriptionOwner}
      onDescriptionDirtyChange={onDescriptionDirtyChange}
      onSaveDescription={onSavePlanDescription}
    />
  </DetailedResultHeader>;
}
