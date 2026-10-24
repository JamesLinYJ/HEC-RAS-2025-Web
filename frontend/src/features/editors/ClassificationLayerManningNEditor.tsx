import type { EditorPageProps, ManningTransformOperation, ModelRow } from './model';
import { ManningInterpretationTable } from './ManningInterpretationTable';
import { assessManningRows } from './manningEditorUi';

const associationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';
const interpretationTable = 'ClassificationLayerManningNEditor/InterpretationTable';

interface ClassificationLayerManningNEditorProps extends EditorPageProps {
  operationBusy?: boolean;
  associationProjectionReady?: boolean;
  associationSyncPending?: boolean;
  onAssociationChange?: (value: string) => void;
  onManningTransform?: (operation: ManningTransformOperation) => Promise<boolean>;
}

/** Classification association and the source-owned, fixed-key Manning interpretation table. */
export function ClassificationLayerManningNEditor(props: ClassificationLayerManningNEditorProps) {
  const { language, snapshot, draft } = props;
  const zh = language === 'zh';
  const manning = draft.manningView ?? snapshot.manning;
  const choices = manning?.classifications ?? [];
  const association = draft.fields[associationField] ?? '';
  const projectionRows = manning?.rows ?? [];
  const rows: ModelRow[] = draft.rows[interpretationTable] ?? projectionRows;
  const review = assessManningRows(rows, projectionRows);
  const busy = props.operationBusy === true || props.associationSyncPending === true;
  const projectionReady = props.associationProjectionReady !== false;
  const selectedAssociationIsAvailable = association === '' || choices.some((choice) => choice.id === association);
  const canRefresh = snapshot.canEdit && selectedAssociationIsAvailable && review.identityIsSafe
    && review.invalidValueRowIDs.length === 0 && !!props.onManningTransform && !busy;
  const canApplyDefaults = snapshot.canEdit && association !== '' && choices.some((choice) => choice.id === association)
    && projectionReady && review.identityIsSafe && review.invalidValueRowIDs.length === 0
    && !!props.onManningTransform && !busy;

  return (
    <section className="editor-field-grid" aria-label={zh ? '分类图层糙率属性' : 'Classification layer Manning properties'} data-testid="manning-classification-editor">
      <div className="editor-field">
        <label htmlFor="manning-classification-layer">{zh ? '关联分类图层' : 'Classification layer'}</label>
        <select
          id="manning-classification-layer"
          data-testid="manning-classification-layer"
          value={association}
          disabled={!snapshot.canEdit || busy}
          onChange={(event) => props.onAssociationChange?.(event.currentTarget.value)}
        >
          <option value="">{zh ? '未关联' : 'None'}</option>
          {association && !choices.some((choice) => choice.id === association) && <option value={association}>{association}</option>}
          {choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.title}</option>)}
        </select>
      </div>

      <section className="editor-table-card" aria-label={zh ? '糙率解释表' : 'Manning interpretation table'} data-testid="manning-interpretation-table">
        <div className="editor-terrain-table-heading">
          <h3>{zh ? '糙率解释表' : 'Manning interpretation table'}</h3>
          <span>{rows.length} {zh ? '个分类' : 'classes'}</span>
        </div>
        {props.associationSyncPending && <p className="editor-field-muted" role="status" data-testid="manning-association-sync-status">
          {zh ? '正在按所选分类图层读取糙率映射…' : 'Reading the Manning mapping for the selected classification…'}
        </p>}
        {!props.associationSyncPending && !projectionReady && <p className="editor-config-notice" role="status" data-testid="manning-association-refresh-required">
          {zh ? '所选分类图层已更改；表格暂时保留上一分类的服务端映射。请先刷新映射，再编辑糙率值。' : 'The classification selection changed. The table still shows the previous Host projection; refresh the mapping before editing its values.'}
        </p>}
        <div className="editor-table-toolbar">
          {props.onManningTransform && <button type="button" className="editor-button" data-testid="manning-refresh-classification"
            disabled={!canRefresh} onClick={() => void props.onManningTransform?.('refresh')}>
            {zh ? (projectionReady ? '刷新分类映射' : '读取所选分类映射') : (projectionReady ? 'Refresh classification mapping' : 'Read selected classification mapping')}
          </button>}
          {canApplyDefaults && <button type="button" className="editor-button" data-testid="manning-apply-nlcd-defaults"
            disabled={!canApplyDefaults} onClick={() => void props.onManningTransform?.('setNLCD')}>
            {zh ? '套用 NLCD 糙率默认值' : 'Set NLCD defaults'}
          </button>}
        </div>
        <ManningInterpretationTable
          language={language}
          rows={rows}
          projectionRows={projectionRows}
          canEdit={snapshot.canEdit && !busy && projectionReady}
          busy={busy}
          onCellChange={props.onRowCellChange}
        />
      </section>
      <p className="editor-field-muted">{zh
        ? '分类键和行数由服务端分类数据决定。可编辑现有行的糙率 n；当前 Host 没有新增、删除或改名分类行的操作。NLCD 默认值只更新名称匹配的现有行，其余行保留原值。'
        : 'The Host classification data owns the keys and row count. Existing Manning values can be edited; the Host has no add, remove, or rename-row operation. NLCD defaults update matching existing names and preserve all other rows.'}</p>
      {!props.onManningTransform && <p className="editor-field-muted" role="status">{zh ? '当前工作环境没有糙率变换服务；刷新映射和套用 NLCD 默认值已停用。' : 'Manning transform is unavailable in this workspace; mapping refresh and NLCD defaults are disabled.'}</p>}
      {props.onManningTransform && review.invalidValueRowIDs.length > 0 && <p className="editor-field-muted">{zh ? '修正无效糙率值后，才能刷新映射或套用默认值。' : 'Correct invalid Manning values before refreshing the mapping or applying defaults.'}</p>}
    </section>
  );
}
