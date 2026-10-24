import type { ReactNode } from 'react';
import type { FeaturePageProps } from '../../shared/workbench';
import type { ModelCreationInspection } from './ProjectModelCreationWorkflow';

interface Props {
  language: FeaturePageProps['language'];
  open: boolean;
  title: string;
  kicker: string;
  path?: string | null;
  revision?: string | null;
  busy: 'inspect' | 'create' | null;
  canInspect: boolean;
  canCreate: boolean;
  blockedReason?: string;
  hasUnsavedDrafts: boolean;
  inspection: ModelCreationInspection | null;
  error?: string;
  details?: string;
  notice?: string;
  children: ReactNode;
  onClose: () => void;
  onInspect: () => void;
  onCreate: () => void;
}

export function ModelCreationDialogFrame({
  language,
  open,
  title,
  kicker,
  path,
  revision,
  busy,
  canInspect,
  canCreate,
  blockedReason,
  hasUnsavedDrafts,
  inspection,
  error,
  details,
  notice,
  children,
  onClose,
  onInspect,
  onCreate,
}: Props) {
  if (!open) return null;
  const zh = language === 'zh';
  return (
    <div className="project-modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <section className="project-new-modal project-operation-modal" role="dialog" aria-modal="true">
        <header className="project-modal-header">
          <div><div className="project-kicker">{kicker}</div><h2>{title}</h2></div>
          <button className="project-icon-button" type="button" disabled={busy !== null} aria-label={zh ? '关闭' : 'Close'} onClick={onClose}>×</button>
        </header>
        <div className="project-modal-body">
          <div className="project-model-owner">
            <span>{zh ? '当前工程' : 'Current project'}</span>
            <strong title={path ?? ''}>{path || (zh ? '未打开工程' : 'No project open')}</strong>
            <small>{zh ? '项目文件 SHA-256' : 'Project file SHA-256'}：{revision || (zh ? '缺失' : 'Missing')}</small>
          </div>
          {children}
          {inspection?.validationMessage && <div className="project-form-error" role="status"><span>!</span>{inspection.validationMessage}</div>}
          {inspection?.warning && <div className="project-inline-warning" role="status">{inspection.warning}</div>}
          {blockedReason && <div className="project-form-error"><span>!</span>{blockedReason}</div>}
          {hasUnsavedDrafts && <div className="project-inline-warning">{zh ? '存在未保存的模型编辑草稿。为避免创建操作改写工程修订，请先保存或处理这些草稿。' : 'Unsaved model-editor drafts exist. Save or resolve them before creating a model so the project revision stays protected.'}</div>}
          {!canInspect && <div className="project-form-error"><span>!</span>{zh ? '当前 C# 服务未发布 modelCreation.inspect。' : 'The C# service has not advertised modelCreation.inspect.'}</div>}
          {!canCreate && canInspect && <div className="project-backend-note"><span>i</span>{zh ? '创建需 modelCreation.commit 完整工程提交回执，并通过 project.tree.load 实际读回新模型。旧版仅写入 HDF 的 modelCreation.create 不用于此操作。' : 'Creation requires the complete project-commit receipt from modelCreation.commit and actual model read-back through project.tree.load. The older HDF-only modelCreation.create is not used here.'}</div>}
          {notice && <div className="project-inline-warning" role="status">{notice}</div>}
          {error && <div className="project-form-error" role="alert"><span>!</span><div>{error}{details && <details><summary>{zh ? '原始诊断' : 'Raw diagnostics'}</summary><pre>{details}</pre></details>}</div></div>}
        </div>
        <footer className="project-modal-footer">
          <span className="project-footer-hint">{zh ? '字段、关联与时间窗口均由 C# 工程服务检查。' : 'C# project services validate the fields, associations, and time window.'}</span>
          <div className="project-footer-actions">
            <button type="button" className="project-button project-button-secondary" disabled={busy !== null || !canInspect} onClick={onInspect}>{busy === 'inspect' ? (zh ? '正在检查…' : 'Inspecting…') : (zh ? '检查参数' : 'Inspect settings')}</button>
            <button type="button" className="project-button project-button-primary" disabled={!canCreate || Boolean(blockedReason)} onClick={onCreate}>{busy === 'create' ? (zh ? '正在创建…' : 'Creating…') : (zh ? '检查并创建' : 'Validate and create')}</button>
          </div>
        </footer>
      </section>
    </div>
  );
}
