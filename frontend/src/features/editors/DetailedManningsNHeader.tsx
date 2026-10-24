import { useEffect, useRef, useState } from 'react';
import { message } from '../../shared/workbench';
import type { EditorPageProps, NValuePreviewData } from './model';
import { NValuePreview } from './ClassificationVisualizationOptions';

const modeField = 'ManningsNEditor/NValueLayer_Mode';
const associationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';

/** Live N-value layer heading and revision-bound preview action. */
export function DetailedManningsNHeader(props: EditorPageProps) {
  const { language, asset, snapshot, draft } = props;
  const [preview, setPreview] = useState<NValuePreviewData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const mode = draft.fields[modeField] ?? 'Constant';
  const associationID = draft.fields[associationField] ?? '';
  const association = snapshot.manning?.classifications.find((item) => item.id === associationID)?.title ?? '';

  useEffect(() => {
    controller.current?.abort();
    controller.current = null;
    setPreview(null);
    setError('');
    setLoading(false);
  }, [asset.path, snapshot.revision]);
  useEffect(() => () => { controller.current?.abort(); }, []);

  const loadPreview = async (): Promise<void> => {
    if (!props.onNValuePreview || !props.canPreviewNValue || loading) return;
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    setLoading(true);
    setError('');
    try {
      const result = await props.onNValuePreview(next.signal);
      if (!next.signal.aborted && controller.current === next) setPreview(result);
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
    <>
      <header className="editor-schema-heading editor-detailed-manning-header">
        <div><p className="editor-eyebrow">{language === 'zh' ? '糙率图层' : 'Manning layer'}</p><h2>{asset.name}</h2><p className="editor-field-muted">{asset.path}</p></div>
        <div className="editor-field">
          <span>{language === 'zh' ? '当前来源' : 'Current source'}</span>
          <strong>{mode === 'ClassificationLayer' ? (language === 'zh' ? '分类图层' : 'Classification layer') : mode === 'Constant' ? (language === 'zh' ? '常数' : 'Constant') : mode}</strong>
          {mode === 'ClassificationLayer' && <small>{association || (language === 'zh' ? '未关联分类图层' : 'No classification layer selected')}</small>}
          <button type="button" className="editor-button" disabled={!props.canPreviewNValue || !props.onNValuePreview || loading} onClick={() => void loadPreview()}>
            {loading ? (language === 'zh' ? '读取预览…' : 'Loading preview…') : (language === 'zh' ? '生成已保存糙率预览' : 'Preview saved Manning layer')}
          </button>
        </div>
      </header>
      {!props.canPreviewNValue && <p className="editor-field-muted">{language === 'zh' ? '糙率草稿有未保存更改或工程修订不可用；保存或丢弃后才能请求源文件预览。' : 'The Manning draft has unsaved changes or the project revision is unavailable. Save or discard before requesting a source-file preview.'}</p>}
      {error && <p className="editor-config-notice" role="alert">{language === 'zh' ? '糙率预览失败。' : 'The Manning preview failed.'}<details><summary>{language === 'zh' ? '查看详情' : 'View details'}</summary><pre>{error}</pre></details></p>}
      {preview && <NValuePreview preview={preview} language={language} />}
    </>
  );
}
