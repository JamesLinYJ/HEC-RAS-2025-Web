import { useRef, useState } from 'react';
import type { Language } from '../../shared/workbench';

export type LocalSourceFile = { file: File; text: string };

export function LocalFilesImportSource({ language, onFile, busy = false }: {
  language: Language;
  onFile: (source: LocalSourceFile | null) => void;
  busy?: boolean;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const zh = language === 'zh';
  const [error, setError] = useState<string>();

  const readFile = async (file?: File): Promise<void> => {
    setError(undefined);
    if (!file) {
      onFile(null);
      return;
    }
    const allowed = /\.(geojson|json|csv)$/i.test(file.name) || /^(application\/geo\+json|application\/json|text\/csv)$/i.test(file.type);
    if (!allowed) {
      onFile(null);
      setError(zh ? '文件格式不受支持，请选择 GeoJSON、JSON 或 CSV 文件。' : 'Unsupported file type. Choose a GeoJSON, JSON, or CSV file.');
      return;
    }
    try {
      onFile({ file, text: await file.text() });
    } catch (caught) {
      const detail = caught instanceof Error ? caught.message : String(caught);
      setError(zh ? `读取文件失败。原始错误：${detail}` : `Could not read the file. Original error: ${detail}`);
    }
  };

  return (
    <div className="tools-file-picker">
      <input
        ref={inputRef}
        type="file"
        accept=".geojson,.json,.csv,application/geo+json,application/json,text/csv"
        disabled={busy}
        onChange={(event) => { const input = event.currentTarget; void readFile(input.files?.[0]).finally(() => { input.value = ''; }); }}
      />
      <button className="tools-button tools-button-secondary" type="button" disabled={busy} onClick={() => inputRef.current?.click()}>
        {zh ? '选择 GeoJSON 或 CSV' : 'Choose GeoJSON or CSV'}
      </button>
      <span className="tools-muted">{zh ? '文件只在此浏览器中读取' : 'The file is read in this browser only'}</span>
      {error && <span className="tools-inline-error" role="alert">{error}</span>}
    </div>
  );
}
