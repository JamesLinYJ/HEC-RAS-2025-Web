import { useEffect, useState } from 'react';
import type { FeaturePageProps, WorkbenchContext } from '../../shared/workbench';
import type { SurfaceCreationKind, VerifiedSurfaceCreation } from './SurfaceCreationWorkflow';
import { useSurfaceCreation } from './SurfaceCreationWorkflow';

interface Props {
  language: FeaturePageProps['language'];
  backend: FeaturePageProps['backend'];
  context: WorkbenchContext;
  kind: SurfaceCreationKind | null;
  hasUnsavedDrafts: boolean;
  blockedReason?: string;
  onClose: () => void;
  onContextCommit?: FeaturePageProps['onContextCommit'];
  onCreated: (source: WorkbenchContext, verified: VerifiedSurfaceCreation, selected: WorkbenchContext) => boolean;
}

function defaultName(kind: SurfaceCreationKind): string {
  if (kind === 'newTerrain') return 'Terrain';
  return kind === 'newClassification' ? 'Classifications' : 'N Values';
}

function kindName(kind: SurfaceCreationKind, language: FeaturePageProps['language']): string {
  if (language !== 'zh') {
    if (kind === 'newTerrain') return 'terrain';
    return kind === 'newClassification' ? 'classification layer' : 'N-Value layer';
  }
  if (kind === 'newTerrain') return '地形';
  return kind === 'newClassification' ? '分类图层' : '糙率图层';
}

export function SurfaceCreationDialog(props: Props) {
  const { language, backend, context, kind, hasUnsavedDrafts, blockedReason, onClose, onContextCommit, onCreated } = props;
  const zh = language === 'zh';
  const [name, setName] = useState(kind ? defaultName(kind) : '');
  const open = kind !== null;
  const workflow = useSurfaceCreation({
    backend,
    context,
    language,
    kind: kind ?? 'newTerrain',
    name,
    open,
    hasUnsavedDrafts,
    onContextCommit,
    onCreated,
  });

  useEffect(() => {
    if (kind) setName(defaultName(kind));
    else setName('');
  }, [kind]);

  if (!kind) return null;

  const capabilitiesAvailable = backend.supports('surfaceCreation.inspect') &&
    backend.supports('surfaceCreation.create') && backend.supports('workspace.file.inspect') &&
    backend.supports('project.tree.load');
  const canInspect = capabilitiesAvailable && Boolean(context.projectPath && context.projectXml && context.projectRevision && context.projectSessionID) && !hasUnsavedDrafts && !blockedReason;
  const close = (): void => {
    if (workflow.busy) return;
    workflow.invalidate();
    onClose();
  };
  const useSuggestedName = (): void => {
    const suggestedName = workflow.inspection?.suggestedName;
    if (!suggestedName) return;
    setName(suggestedName);
    workflow.invalidate();
  };
  const title = zh ? `新建${kindName(kind, language)}` : `New ${kindName(kind, language)}`;

  return (
    <div className="project-modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget && !workflow.busy) close();
    }}>
      <section className="project-new-modal project-operation-modal" role="dialog" aria-modal="true" aria-labelledby="surface-create-title" data-testid="surface-creation-dialog">
        <header className="project-modal-header">
          <div>
            <div className="project-kicker">{zh ? '工程图层' : 'PROJECT LAYER'}</div>
            <h2 id="surface-create-title">{title}</h2>
          </div>
          <button className="project-icon-button" type="button" disabled={workflow.busy !== null} aria-label={zh ? '关闭' : 'Close'} onClick={close}>×</button>
        </header>

        <div className="project-modal-body">
          <div className="project-model-owner">
            <span>{zh ? '当前工程' : 'Current project'}</span>
            <strong title={context.projectPath ?? ''}>{context.projectPath || (zh ? '未打开工程' : 'No project open')}</strong>
            <small>{zh ? '工程文件 SHA-256' : 'Project file SHA-256'}：{context.projectRevision || (zh ? '缺失' : 'Missing')}</small>
          </div>

          <label className="project-form-field">
            <span>{zh ? '名称' : 'Name'}</span>
            <input
              data-testid="surface-create-name"
              value={name}
              maxLength={240}
              disabled={workflow.busy !== null}
              onChange={(event) => { setName(event.currentTarget.value); workflow.invalidate(); }}
              autoFocus
            />
          </label>

          {blockedReason && <div className="project-form-error" role="status"><span>!</span>{blockedReason}</div>}
          {!capabilitiesAvailable && <div className="project-form-error" role="status"><span>!</span>{zh ? '当前服务暂不支持创建该图层或核验创建结果。' : 'The current service does not support creating this layer or verifying its receipt.'}</div>}
          {hasUnsavedDrafts && <div className="project-inline-warning">{zh ? '存在未保存的编辑草稿；请先保存或处理草稿，再创建工程图层。' : 'There are unsaved edit drafts. Save or resolve them before creating a project layer.'}</div>}
          {!context.projectPath && <div className="project-form-error" role="status"><span>!</span>{zh ? '请先打开已保存的工程。' : 'Open a saved project first.'}</div>}

          {workflow.inspection && <div className="project-inline-warning" role="status">
            <div>{zh ? '目标文件夹' : 'Destination folder'}：<strong>{workflow.inspection.targetDirectory}</strong></div>
            <div>{zh ? '本次检查的名称' : 'Inspected name'}：<strong>{workflow.inspectedName}</strong></div>
            <div>{zh ? '检查修订' : 'Inspection revision'}：<code>{workflow.inspection.revision}</code></div>
            {workflow.inspection.suggestedName !== name && <button type="button" className="project-button project-button-secondary" disabled={workflow.busy !== null} onClick={useSuggestedName}>{zh ? `采用建议名称“${workflow.inspection.suggestedName}”` : `Use suggested name “${workflow.inspection.suggestedName}”`}</button>}
            {workflow.inspection.validationMessage && <div>{zh ? '服务检查发现名称或目标位置问题，请更正名称或使用建议名称。' : workflow.inspection.validationMessage}<details><summary>{zh ? '检查说明' : 'Inspection detail'}</summary><pre>{workflow.inspection.validationMessage}</pre></details></div>}
          </div>}

          {workflow.fileCreated && <div className="project-model-owner" data-testid="surface-create-receipt">
            <span>{zh ? '服务创建回执' : 'Creation receipt'}</span>
            <strong title={workflow.fileCreated.assetPath}>{workflow.fileCreated.assetPath}</strong>
            {workflow.capturedAsset && <small>{zh ? '文件 SHA-256' : 'File SHA-256'}：{workflow.capturedAsset.revision} · {workflow.capturedAsset.length} {zh ? '字节' : 'bytes'}</small>}
          </div>}
          {workflow.notice && <div className="project-inline-warning" role="status">{workflow.notice}</div>}
          {workflow.error && <div className="project-form-error" role="alert"><span>!</span><div>{workflow.error}{workflow.details && <details><summary>{zh ? '原始诊断' : 'Raw diagnostics'}</summary><pre>{workflow.details}</pre></details>}</div></div>}
        </div>

        <footer className="project-modal-footer">
          <span className="project-footer-hint">{zh ? '服务写入真实 HDF；只有工程目录按路径、类别和名称读回后才切换当前选择。' : 'The service writes a real HDF file. Selection changes only after the project catalog reads back its exact path, kind, and name.'}</span>
          <div className="project-footer-actions">
            {workflow.busy ? <button type="button" className="project-button project-button-secondary" data-testid="surface-create-cancel" onClick={() => workflow.cancel()}>{zh ? '停止等待' : 'Stop waiting'}</button> : <button type="button" className="project-button project-button-secondary" disabled={!canInspect || !name.trim()} onClick={() => void workflow.inspect()}>{zh ? '检查名称与位置' : 'Check name and location'}</button>}
            <button type="button" className="project-button project-button-primary" data-testid="surface-create-submit" disabled={!workflow.canCreate || Boolean(blockedReason)} onClick={() => void workflow.create()}>{workflow.busy === 'create' ? (zh ? '正在创建…' : 'Creating…') : (zh ? '创建图层' : 'Create layer')}</button>
          </div>
        </footer>
      </section>
    </div>
  );
}
