import { useEffect, useRef, useState } from 'react';
import { message } from '../../shared/workbench';
import type { ClassificationMapData, EditorPageProps } from './model';
import { RasterClassificationInputProperties } from './RasterClassificationInputProperties';
import { ClassificationVisualizationOptions } from './ClassificationVisualizationOptions';

type ClassificationInput = NonNullable<EditorPageProps['snapshot']['classification']>['inputs'][number];

/** Classification raster map preview and selected raster mapping table. */
export function ClassificationRasterEditor({ props, input }: { props: EditorPageProps; input: ClassificationInput }) {
  const { language, snapshot, draft } = props;
  const [mapState, setMapState] = useState<{ identity: string; map: ClassificationMapData } | null>(null);
  const [loadingIdentity, setLoadingIdentity] = useState<string | null>(null);
  const [errorState, setErrorState] = useState<{ identity: string; message: string } | null>(null);
  const controller = useRef<AbortController | null>(null);
  const draftStamp = JSON.stringify({ sourceState: draft.sourceState, fields: draft.fields, rows: draft.rows });
  const classification = draft.classificationView ?? snapshot.classification;
  const selectionField = 'ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput';
  const requestedSelectedInputId = draft.fields[selectionField]
    ?? classification?.selectedId
    ?? '';
  const previewIdentity = JSON.stringify({ assetPath: props.asset.path, revision: snapshot.revision, sourceState: draft.sourceState, draftStamp, inputId: input.id, requestedSelectedInputId });
  const map = mapState?.identity === previewIdentity ? mapState.map : null;
  const loading = loadingIdentity === previewIdentity;
  const error = errorState?.identity === previewIdentity ? errorState.message : '';

  useEffect(() => {
    controller.current?.abort();
    controller.current = null;
    setMapState(null);
    setErrorState(null);
    setLoadingIdentity(null);
  }, [previewIdentity]);
  useEffect(() => () => { controller.current?.abort(); }, []);

  const loadMap = async (): Promise<void> => {
    if (!props.onClassificationMap || loading) return;
    const requestedIdentity = previewIdentity;
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    setLoadingIdentity(requestedIdentity);
    setErrorState(null);
    try {
      const result = await props.onClassificationMap(next.signal);
      if (requestedSelectedInputId) {
        if (result.selectedInputId !== requestedSelectedInputId) {
          throw new Error(language === 'zh' ? '预览响应与草稿中选中的分类输入不匹配。' : 'The preview response does not match the classification input selected in the draft.');
        }
      } else if (result.selectedInputId !== null || result.selectedRaster !== null) {
        throw new Error(language === 'zh' ? '未选中分类输入时，预览响应不应包含选中输入数据。' : 'A preview without a selected input must not contain selected-input data.');
      }
      if (!next.signal.aborted && controller.current === next) setMapState({ identity: requestedIdentity, map: result });
    } catch (cause) {
      if (!next.signal.aborted && controller.current === next) setErrorState({ identity: requestedIdentity, message: message(cause, language) });
    } finally {
      if (controller.current === next) {
        controller.current = null;
        setLoadingIdentity(null);
      }
    }
  };

  return (
    <section className="editor-terrain-card editor-classification-raster-editor">
      {input.tableId && <RasterClassificationInputProperties props={props} input={input} />}
      <div className="editor-terrain-table-heading">
        <h3>{language === 'zh' ? '分类栅格地图' : 'Classification raster map'}</h3>
        <button type="button" className="editor-button" disabled={!props.onClassificationMap || loading} onClick={() => void loadMap()}>
          {loading ? (language === 'zh' ? '正在读取…' : 'Loading…') : (language === 'zh' ? '更新地图预览' : 'Refresh map preview')}
        </button>
      </div>
      {!props.onClassificationMap && <p className="editor-field-muted">{language === 'zh' ? '当前工作环境不支持分类地图预览，预览已停用。' : 'Classification map preview is unavailable in this workspace.'}</p>}
      {error && <p className="editor-config-notice" role="alert">{language === 'zh' ? '分类地图预览失败。' : 'The classification map preview failed.'}<details><summary>{language === 'zh' ? '查看详情' : 'View details'}</summary><pre>{error}</pre></details></p>}
      {map && !requestedSelectedInputId && <p className="editor-field-muted">{language === 'zh' ? '当前草稿未明确选中分类输入，因此这里只显示完整分类图层。' : 'No classification input is explicitly selected in the current draft, so only the full classification layer is shown.'}</p>}
      {map && <ClassificationVisualizationOptions map={map} language={language} selectedInputName={requestedSelectedInputId === input.id ? draft.fields[`${input.scope}/Name`] ?? input.name : undefined} />}
      {!map && props.onClassificationMap && <p className="editor-field-muted">{language === 'zh' ? '预览使用当前分类草稿和源修订生成栅格与图例；源身份或草稿变化后，旧预览会立即隐藏。' : 'The preview uses the current classification draft and source revision. A change in source identity or draft immediately hides the previous preview.'}</p>}
    </section>
  );
}
