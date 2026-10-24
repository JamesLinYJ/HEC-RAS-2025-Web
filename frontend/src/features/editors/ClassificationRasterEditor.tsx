import { useEffect, useRef, useState } from 'react';
import { message } from '../../shared/workbench';
import type { ClassificationMapData, EditorPageProps } from './model';
import { RasterClassificationInputProperties } from './RasterClassificationInputProperties';
import { ClassificationVisualizationOptions } from './ClassificationVisualizationOptions';

type ClassificationInput = NonNullable<EditorPageProps['snapshot']['classification']>['inputs'][number];

/** Classification raster map preview and selected raster mapping table. */
export function ClassificationRasterEditor({ props, input }: { props: EditorPageProps; input: ClassificationInput }) {
  const { language, snapshot, draft } = props;
  const [map, setMap] = useState<ClassificationMapData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const draftStamp = JSON.stringify({ sourceState: draft.sourceState, fields: draft.fields, rows: draft.rows });

  useEffect(() => {
    controller.current?.abort();
    controller.current = null;
    setMap(null);
    setError('');
    setLoading(false);
  }, [snapshot.revision, draftStamp, input.id]);
  useEffect(() => () => { controller.current?.abort(); }, []);

  const loadMap = async (): Promise<void> => {
    if (!props.onClassificationMap || loading) return;
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    setLoading(true);
    setError('');
    try {
      const result = await props.onClassificationMap(next.signal);
      if (!next.signal.aborted && controller.current === next) setMap(result);
    } catch (cause) {
      if (!next.signal.aborted && controller.current === next) setError(message(cause, language));
    } finally {
      if (controller.current === next) {
        controller.current = null;
        setLoading(false);
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
      {map && <ClassificationVisualizationOptions map={map} language={language} selectedInputName={draft.fields[`${input.scope}/Name`] ?? input.name} />}
      {!map && props.onClassificationMap && <p className="editor-field-muted">{language === 'zh' ? '预览使用当前分类草稿和源修订生成栅格与图例。' : 'The preview uses the current classification draft and source revision to produce a raster and legend.'}</p>}
    </section>
  );
}
