import { useLayoutEffect, useRef, useState } from 'react';
import type { AssetReference, Language } from '../../../shared/workbench';
import { parseUserData, type ResultDataset } from '../data';

interface ResultPickerProps {
  importOwner: string;
  language: Language;
  selectedAsset: AssetReference | null;
  hasResultBackend: boolean;
  resultReady: boolean;
  resultLoading: boolean;
  userDataset: ResultDataset | null;
  activeSource: 'result' | 'user';
  onSourceChange: (source: 'result' | 'user') => void;
  onUserDataset: (dataset: ResultDataset) => boolean;
  onError: (error: string | null) => void;
}

function isResultAsset(asset: AssetReference | null): boolean {
  if (!asset) return false;
  const kind = asset.kind.trim().toLowerCase();
  if (!kind || kind === 'unknown') return /\.(hdf|h5)$/i.test(asset.path);
  return kind.includes('result');
}

export function ResultPicker({ importOwner, language, selectedAsset, hasResultBackend, resultReady, resultLoading, userDataset, activeSource, onSourceChange, onUserDataset, onError }: ResultPickerProps) {
  const fileRef = useRef<HTMLInputElement>(null);
  const importGenerationRef = useRef(0);
  const currentOwnerRef = useRef(importOwner);
  const mountedRef = useRef(false);
  const [readingFile, setReadingFile] = useState(false);
  const text = (zh: string, en: string): string => language === 'zh' ? zh : en;
  const resultAvailable = isResultAsset(selectedAsset);

  useLayoutEffect(() => {
    if (currentOwnerRef.current === importOwner) return;
    currentOwnerRef.current = importOwner;
    importGenerationRef.current += 1;
    setReadingFile(false);
    if (fileRef.current) fileRef.current.value = '';
  }, [importOwner]);

  useLayoutEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      importGenerationRef.current += 1;
    };
  }, []);

  const openFile = async (file: File | undefined): Promise<void> => {
    if (!file) return;
    const ownerAtStart = importOwner;
    const generation = ++importGenerationRef.current;
    const isCurrentImport = (): boolean => mountedRef.current
      && currentOwnerRef.current === ownerAtStart
      && importGenerationRef.current === generation;
    setReadingFile(true);
    onError(null);
    try {
      const content = await file.text();
      if (!isCurrentImport()) return;
      const dataset = parseUserData(content, file.name, language);
      if (!isCurrentImport()) return;
      if (onUserDataset(dataset) && isCurrentImport()) onSourceChange('user');
    } catch (error) {
      if (isCurrentImport()) {
        const detail = error instanceof Error ? error.message : String(error);
        const hasChinese = /[\u3400-\u9fff]/.test(detail);
        onError(language === 'zh' && !hasChinese ? `导入文件失败。原始错误：${detail}` : detail);
      }
    } finally {
      if (isCurrentImport()) {
        setReadingFile(false);
        if (fileRef.current) fileRef.current.value = '';
      }
    }
  };

  return (
    <section className="results-picker">
      <div className="results-picker-main">
        <div className="results-field results-source-field">
          <label>{text('结果来源', 'Data source')}</label>
          <select value={activeSource} onChange={(event) => onSourceChange(event.target.value as 'result' | 'user')} aria-label={text('选择结果数据来源', 'Select result data source')}>
            {resultAvailable && <option value="result">{selectedAsset?.name ?? text('工程结果', 'Project result')}</option>}
            {userDataset && <option value="user">{userDataset.name}</option>}
            {!resultAvailable && !userDataset && <option value="result">{text('尚无结果文件', 'No result selected')}</option>}
          </select>
        </div>
        <div className="results-source-status">
          {activeSource === 'result' ? <>
            <span className={'results-state-dot ' + (resultLoading ? 'is-loading' : resultReady ? 'is-ready' : '')} />
            <span>{resultLoading ? text('正在读取结果目录…', 'Reading result catalog…') : resultReady ? text('C# 结果数据已载入', 'C# result data loaded') : hasResultBackend ? text('等待结果目录', 'Waiting for result catalog') : text('当前 C# 未提供结果读取链路', 'Result reading is not exposed by the current C# build')}</span>
          </> : <>
            <span className="results-state-dot is-user" />
            <span>{text('本地导入数据 · 不代表 HDF 结果已接通', 'Imported user data · this does not indicate HDF results are connected')}</span>
          </>}
        </div>
      </div>
      {activeSource === 'result' && resultAvailable && <div className="results-selected-asset">
        <span className="results-file-mark">HDF</span>
        <div><strong>{selectedAsset?.name}</strong><small title={selectedAsset?.path}>{selectedAsset?.path}</small></div>
        {selectedAsset?.revision && <code>{selectedAsset.revision.slice(0, 10)}</code>}
      </div>}
      <div className="results-picker-actions">
        <input ref={fileRef} type="file" accept=".csv,.tsv,.txt,.json,text/csv,application/json,text/tab-separated-values" className="results-file-input" onChange={(event) => void openFile(event.target.files?.[0])} />
        <button type="button" className="results-secondary-button" onClick={() => fileRef.current?.click()} disabled={readingFile}>
          <span aria-hidden="true">⇧</span>{readingFile ? text('正在读取…', 'Reading…') : text('导入 CSV / JSON', 'Import CSV / JSON')}
        </button>
        {userDataset && activeSource === 'user' && <span className="results-import-count">{userDataset.times.length} {text('个时间步', 'time steps')} · {userDataset.series.length} {text('个变量', 'series')}</span>}
      </div>
    </section>
  );
}
