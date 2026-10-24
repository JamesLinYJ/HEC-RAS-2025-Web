import { useEffect, useRef, useState } from 'react';
import { message } from '../../shared/workbench';
import type { EditorPageProps, NValuePreviewData } from './model';
import { NValuePreview } from './ClassificationVisualizationOptions';
import { manningEditorIdentity } from './manningEditorUi';

const modeField = 'ManningsNEditor/NValueLayer_Mode';
const associationField = 'ClassificationLayerManningNEditor/NValueLayer_ClassificationLayer';

/** Source identity, revision-bound read-only preview, and details for one N-value layer. */
export function DetailedManningsNHeader(props: EditorPageProps) {
  const { language, asset, snapshot, draft } = props;
  const zh = language === 'zh';
  const [preview, setPreview] = useState<NValuePreviewData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const mode = draft.fields[modeField] ?? 'Constant';
  const associationID = draft.fields[associationField] ?? '';
  const association = (draft.manningView ?? snapshot.manning)?.classifications.find((item) => item.id === associationID)?.title ?? '';
  const previewIdentity = [manningEditorIdentity(props), draft.sourceState ?? '', mode, associationID,
    props.canPreviewNValue ? 'clean' : 'blocked'].join('\u001f');
  const previewIdentityRef = useRef(previewIdentity);
  previewIdentityRef.current = previewIdentity;

  useEffect(() => {
    controller.current?.abort();
    controller.current = null;
    setPreview(null);
    setError('');
    setLoading(false);
  }, [previewIdentity]);

  useEffect(() => () => { controller.current?.abort(); }, []);

  const loadPreview = async (): Promise<void> => {
    if (!props.onNValuePreview || !props.canPreviewNValue || loading) return;
    controller.current?.abort();
    const next = new AbortController();
    const requestIdentity = previewIdentity;
    controller.current = next;
    setLoading(true);
    setPreview(null);
    setError('');
    try {
      const result = await props.onNValuePreview(next.signal);
      if (next.signal.aborted || controller.current !== next || previewIdentityRef.current !== requestIdentity) return;
      if (!result || result.revision !== snapshot.revision || !Array.isArray(result.legendStops)
        || (result.raster && (!Number.isInteger(result.raster.width) || !Number.isInteger(result.raster.height) || typeof result.raster.rgba !== 'string')))
        throw new Error(zh ? '糙率预览与当前源修订不匹配。' : 'The Manning preview does not match the current source revision.');
      setPreview(result);
    } catch (cause) {
      if (!next.signal.aborted && controller.current === next && previewIdentityRef.current === requestIdentity) setError(message(cause, language));
    } finally {
      if (controller.current === next) {
        controller.current = null;
        setLoading(false);
      }
    }
  };

  return (
    <>
      <header className="editor-schema-heading editor-detailed-manning-header" data-testid="nvalue-layer-header">
        <div>
          <p className="editor-eyebrow">{zh ? '糙率图层' : 'Manning layer'}</p>
          <h2>{asset.name}</h2>
          <p className="editor-field-muted">{asset.path}</p>
        </div>
        <div className="editor-field">
          <span>{zh ? '当前来源' : 'Current source'}</span>
          <strong>{mode === 'ClassificationLayer' ? (zh ? '分类图层' : 'Classification layer') : mode === 'Constant' ? (zh ? '常数' : 'Constant') : mode}</strong>
          {mode === 'ClassificationLayer' && <small>{association || (zh ? '未关联分类图层' : 'No classification layer selected')}</small>}
          <button type="button" className="editor-button" data-testid="nvalue-preview-button"
            aria-busy={loading || undefined}
            disabled={!props.canPreviewNValue || !props.onNValuePreview || loading}
            onClick={() => void loadPreview()}>
            {loading ? (zh ? '读取预览…' : 'Loading preview…') : (zh ? '生成已保存糙率预览' : 'Preview saved Manning layer')}
          </button>
        </div>
      </header>
      {!props.onNValuePreview && <p className="editor-field-muted" role="status" data-testid="nvalue-preview-unavailable">
        {zh ? '当前服务未提供糙率栅格预览。' : 'The active service does not provide Manning raster preview.'}
      </p>}
      {!!props.onNValuePreview && !props.canPreviewNValue && <p className="editor-field-muted" role="status" data-testid="nvalue-preview-needs-saved-source">
        {zh ? '糙率草稿有未保存更改或工程修订不可用；保存或丢弃后，才能请求与源文件修订对应的只读预览。' : 'The Manning draft has unsaved changes or the project revision is unavailable. Save or discard before requesting a read-only preview for the source revision.'}
      </p>}
      {error && <p className="editor-config-notice" role="alert" data-testid="nvalue-preview-error">
        {zh ? '糙率预览失败。' : 'The Manning preview failed.'}
        <details><summary>{zh ? '查看详情' : 'View details'}</summary><pre>{error}</pre></details>
      </p>}
      {preview && <div data-testid="nvalue-preview-result"><NValuePreview preview={preview} language={language} /></div>}
    </>
  );
}
