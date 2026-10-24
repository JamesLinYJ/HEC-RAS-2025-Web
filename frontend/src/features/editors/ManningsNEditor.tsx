import { useCallback, useEffect, useRef, useState } from 'react';
import type { EditorPageProps, ManningTransformOperation } from './model';
import { editorValueLabel } from './editorLocalization';
import { ClassificationLayerManningNEditor } from './ClassificationLayerManningNEditor';
import { ConstantManningsNEditor } from './ConstantManningsNEditor';
import { DetailedManningsNHeader } from './DetailedManningsNHeader';
import { manningEditorIdentity, useManningTransform } from './manningEditorUi';

const modeField = 'ManningsNEditor/NValueLayer_Mode';
const associationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';

/** React port of Ras.Visual.WinUI.Controls.ManningsNEditor. */
export function ManningsNEditor(props: EditorPageProps) {
  const { language, snapshot, draft } = props;
  const zh = language === 'zh';
  const manning = draft.manningView ?? snapshot.manning;
  const currentMode = draft.fields[modeField] ?? 'Constant';
  const association = draft.fields[associationField] ?? '';
  const savedAssociation = snapshot.fields[associationField] ?? '';
  const identity = manningEditorIdentity(props);
  const { busy: operationBusy, run } = useManningTransform(identity, props.onManningTransform);
  const [associationProjection, setAssociationProjection] = useState({ identity, association: savedAssociation });
  const [associationSyncPending, setAssociationSyncPending] = useState(false);
  const pendingAssociation = useRef<{ identity: string; association: string } | null>(null);
  const identityRef = useRef(identity);
  identityRef.current = identity;
  const currentProjectionReady = associationProjection.identity === identity && associationProjection.association === association;

  useEffect(() => {
    setAssociationProjection({ identity, association: savedAssociation });
    pendingAssociation.current = null;
    setAssociationSyncPending(false);
  }, [identity, savedAssociation]);

  const runManningTransform = useCallback(async (operation: ManningTransformOperation): Promise<boolean> => {
    const requestIdentity = identity;
    const requestAssociation = association;
    const accepted = await run(operation);
    if (accepted && identityRef.current === requestIdentity) {
      setAssociationProjection({ identity: requestIdentity, association: requestAssociation });
    }
    return accepted;
  }, [association, identity, run]);

  const chooseAssociation = (nextAssociation: string): void => {
    if (!snapshot.canEdit || operationBusy || associationSyncPending || nextAssociation === association) return;
    pendingAssociation.current = { identity, association: nextAssociation };
    setAssociationSyncPending(true);
    props.onFieldChange(associationField, nextAssociation);
  };

  useEffect(() => {
    const request = pendingAssociation.current;
    if (!request) return;
    if (request.identity !== identity) {
      pendingAssociation.current = null;
      setAssociationSyncPending(false);
      return;
    }
    if (request.association !== association) return;
    pendingAssociation.current = null;
    const requestIdentity = identity;
    void runManningTransform('refresh').finally(() => {
      if (identityRef.current === requestIdentity) setAssociationSyncPending(false);
    });
  }, [association, identity, runManningTransform]);

  return (
    <section className="editor-schema-page editor-manning-editor" data-testid="manning-editor">
      <DetailedManningsNHeader {...props} />
      {manning?.warning && <div className="editor-config-notice" role="status" data-testid="manning-source-warning">
        {zh ? '糙率数据存在提示。' : 'The Manning data includes a warning.'}
        <details><summary>{zh ? '查看详情' : 'View details'}</summary><pre>{manning.warning}</pre></details>
      </div>}
      <div className="editor-field-grid">
        <div className="editor-field">
          <label htmlFor="manning-mode">{zh ? '糙率来源' : 'Source mode'}</label>
          <select
            id="manning-mode"
            data-testid="manning-source-mode"
            value={currentMode}
            disabled={!snapshot.canEdit || operationBusy || associationSyncPending}
            onChange={(event) => props.onFieldChange(modeField, event.currentTarget.value)}
          >
            {!['Constant', 'ClassificationLayer'].includes(currentMode) && <option value={currentMode}>{editorValueLabel(language, currentMode)}</option>}
            <option value="Constant">{zh ? '常数' : 'Constant'}</option>
            <option value="ClassificationLayer">{zh ? '分类图层' : 'Classification layer'}</option>
          </select>
        </div>
      </div>
      {operationBusy && <p className="editor-field-muted" role="status" data-testid="manning-operation-status">
        {zh ? '正在读取或更新糙率草稿…' : 'Reading or updating the Manning draft…'}
      </p>}
      {currentMode === 'Constant' && <ConstantManningsNEditor {...props} operationBusy={operationBusy || associationSyncPending} />}
      {currentMode === 'ClassificationLayer' && <ClassificationLayerManningNEditor
        {...props}
        operationBusy={operationBusy}
        associationProjectionReady={currentProjectionReady}
        associationSyncPending={associationSyncPending}
        onAssociationChange={chooseAssociation}
        onManningTransform={runManningTransform}
      />}
      {!['Constant', 'ClassificationLayer'].includes(currentMode) && <p className="editor-field-muted" role="status" data-testid="manning-mode-unavailable">
        {zh ? '该来源模式保留在模型中；当前服务仅支持编辑常数和分类图层模式。' : 'This source mode is preserved; the current service only supports Constant and ClassificationLayer edits.'}
      </p>}
      <details className="editor-source-details">
        <summary>{zh ? '糙率草稿与保存' : 'Manning draft and save'}</summary>
        <p>{zh ? '来源、关联和解释表更改会先进入当前工作台草稿。只有点击工作台“保存”才会写入糙率模型文件；默认值操作也只改草稿。' : 'Source, association, and interpretation changes stay in the workbench draft. Only the workbench Save writes the Manning model file; applying defaults also changes the draft only.'}</p>
      </details>
    </section>
  );
}
