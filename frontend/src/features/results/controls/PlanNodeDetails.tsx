import { useEffect, useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { ResultPlanContextSnapshot, ResultPlanSettingsSnapshot } from './resultDetailsTypes';

interface PlanNodeDetailsProps {
  language: Language;
  settings: ResultPlanSettingsSnapshot | null;
  planContext: ResultPlanContextSnapshot | null;
  loading: boolean;
  settingsError: string | null;
  contextError: string | null;
  canEditDescription: boolean;
  saving: boolean;
  saveError: string | null;
  saveStatus: string | null;
  descriptionOwner: string;
  onDescriptionDirtyChange?: (owner: string, dirty: boolean) => void;
  onSaveDescription?: (description: string) => void;
}

/** Shows the selected Result's embedded Plan fields and context-derived candidate Results. */
export function PlanNodeDetails({
  language, settings, planContext, loading, settingsError, contextError,
  canEditDescription, saving, saveError, saveStatus, descriptionOwner,
  onDescriptionDirtyChange, onSaveDescription,
}: PlanNodeDetailsProps) {
  const zh = language === 'zh';
  const none = zh ? '未提供' : 'Not provided';
  const [descriptionDraft, setDescriptionDraft] = useState(settings?.planDescription ?? '');

  useEffect(() => {
    setDescriptionDraft(settings?.planDescription ?? '');
  }, [settings?.resultPath, settings?.revision, settings?.planDescription]);

  const savedDescription = settings?.planDescription ?? '';
  const descriptionDirty = !!settings?.hasPlan && descriptionDraft !== savedDescription;
  useEffect(() => {
    onDescriptionDirtyChange?.(descriptionOwner, descriptionDirty);
    return () => onDescriptionDirtyChange?.(descriptionOwner, false);
  }, [descriptionOwner, descriptionDirty, onDescriptionDirtyChange]);

  return <section className="results-detail-section results-plan-node-details" aria-label={zh ? '内嵌方案详情' : 'Embedded Plan details'}>
    <h4>{zh ? '结果中的内嵌方案' : 'Plan embedded in Result'}</h4>
    {loading && <p className="results-detail-status">{zh ? '正在读取内嵌方案字段…' : 'Reading embedded Plan fields…'}</p>}
    {settingsError && <p className="results-detail-status is-error">{zh ? '方案详情状态：' : 'Plan detail status: '}<code>{settingsError}</code></p>}
    {contextError && <p className="results-detail-status is-error">{zh ? '方案上下文状态：' : 'Plan context status: '}<code>{contextError}</code></p>}
    {!loading && !settings && !settingsError && <p className="results-detail-status">{zh ? '当前没有内嵌方案快照。' : 'No embedded Plan snapshot is available.'}</p>}
    {settings && <>
      {!settings.hasPlan && <p className="results-detail-status">{zh ? '此结果没有可编辑的内嵌方案。' : 'This Result has no editable embedded Plan.'}</p>}
      <dl className="results-association-grid">
        <div><dt>{zh ? '方案名' : 'Plan name'}</dt><dd>{settings.planName ?? none}</dd></div>
        <div><dt>{zh ? '几何' : 'Geometry'}</dt><dd>{settings.geometryName ?? none}</dd></div>
        <div><dt>{zh ? '边界条件' : 'Boundary conditions'}</dt><dd>{settings.boundaryConditionName ?? none}</dd></div>
        <div><dt>{zh ? '内嵌图片' : 'Embedded images'}</dt><dd>{settings.images.length.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')}</dd></div>
      </dl>
      {settings.hasPlan && <div className="results-plan-description-editor">
        <label>
          <span>{zh ? '方案描述' : 'Plan description'}</span>
          <textarea
            value={descriptionDraft}
            onChange={(event) => setDescriptionDraft(event.currentTarget.value)}
            readOnly={!canEditDescription || saving}
            rows={2}
            aria-label={zh ? '方案描述' : 'Plan description'}
          />
        </label>
        {canEditDescription && onSaveDescription && <div className="results-plan-description-actions">
          <button
            type="button"
            className="results-secondary-button"
            onClick={() => setDescriptionDraft(savedDescription)}
            disabled={saving || !descriptionDirty}
          >{zh ? '取消更改' : 'Discard changes'}</button>
          <button
            type="button"
            className="results-primary-button"
            onClick={() => onSaveDescription(descriptionDraft)}
            disabled={saving || !descriptionDirty}
          >
            {saving ? (zh ? '正在写入结果…' : 'Writing Result…') : (zh ? '保存到 HDF 结果' : 'Save to HDF Result')}
          </button>
        </div>}
        {!canEditDescription && descriptionDirty && <button type="button" className="results-secondary-button results-plan-description-cancel" onClick={() => setDescriptionDraft(savedDescription)} disabled={saving}>{zh ? '取消更改' : 'Discard changes'}</button>}
      </div>}
      {!canEditDescription && settings.hasPlan && <p className="results-detail-status">{zh
        ? '方案字段只读：安全保存要求当前 HDF/工程修订、父上下文提交回调均可用，且没有其他未保存草稿。'
        : 'Plan fields are read-only until the HDF/project revisions, parent-context commit callback, and absence of other unsaved drafts are confirmed.'}</p>}
      {saveError && <p className="results-detail-status is-error" role="alert">{saveError}</p>}
      {saveStatus && <p className="results-detail-status" role="status">{saveStatus}</p>}
      {settings.embeddedPlanGroups.length > 0 && <details className="results-detail-extra">
        <summary>{zh ? '内嵌计算方案字段' : 'Embedded computation-plan fields'}</summary>
        {settings.embeddedPlanGroups.map((group, groupIndex) => <div className="results-plan-property-group" key={`${group.title}-${groupIndex}`}>
          <h5>{group.title}</h5>
          <dl>{group.items.map((item, itemIndex) => <div key={`${item.label}-${itemIndex}`}><dt>{item.label}</dt><dd>{item.value}</dd></div>)}</dl>
        </div>)}
      </details>}
      {settings.images.length > 0 && <details className="results-detail-extra">
        <summary>{zh ? '内嵌图片（只读）' : 'Embedded images (read-only)'}</summary>
        <ul className="results-embedded-image-list">{settings.images.map((image) => <li key={image.id}>
          <strong>{image.name}</strong><code title={image.id}>{image.id}</code>
          <span>{image.byteCount.toLocaleString(language === 'zh' ? 'zh-CN' : 'en-US')} {zh ? '字节' : 'bytes'}</span>
        </li>)}</ul>
      </details>}
    </>}
    {planContext && <details className="results-detail-extra">
      <summary>{zh ? `同工程结果候选（${planContext.candidateGroups.reduce((count, group) => count + group.results.length, 0)}）` : `Result candidates in this project (${planContext.candidateGroups.reduce((count, group) => count + group.results.length, 0)})`}</summary>
      <p className="results-detail-status">{zh ? '候选列表来自当前工程和此结果的内嵌方案上下文；这里仅供查看，不会执行关联。' : 'Candidates come from this project and the embedded Plan context. This list is read-only and does not change associations.'}</p>
      {planContext.candidateGroups.length === 0
        ? <p className="results-detail-status">{zh ? '没有其他已保存结果候选。' : 'No other saved Result candidates were returned.'}</p>
        : planContext.candidateGroups.map((group, groupIndex) => <div className="results-plan-candidate-group" key={`${group.planName ?? '(none)'}-${groupIndex}`}>
          <h5>{group.planName ?? (zh ? '无方案' : '(no Plan)')}</h5>
          {group.results.map((candidate) => <div key={`${candidate.resultPath}-${candidate.revision}`}>
            <strong>{candidate.name}</strong><span title={candidate.resultPath}>{candidate.resultPath}</span>
            <code title={candidate.revision}>{candidate.revision.slice(0, 12)}…</code>
          </div>)}
        </div>)}
    </details>}
  </section>;
}
