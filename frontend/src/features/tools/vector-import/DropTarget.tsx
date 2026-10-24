import { useRef, useState } from 'react';
import { DropTargetHighlight } from './DropTargetHighlight';
import './vectorImport.css';

interface DropTargetProps {
  language: 'zh' | 'en';
  files: File[];
  disabled?: boolean;
  onFilesChange: (files: File[]) => void;
  onValidationChange: (message?: string) => void;
}

const shapefileExtensions = new Set(['.shp', '.shx', '.dbf', '.prj']);

function extensionOf(name: string): string {
  const index = name.lastIndexOf('.');
  return index < 0 ? '' : name.slice(index).toLocaleLowerCase();
}

function stemOf(name: string): string {
  const index = name.lastIndexOf('.');
  return (index < 0 ? name : name.slice(0, index)).toLocaleLowerCase();
}

/** Mirrors the source-upload endpoint's Shapefile primary/sidecar requirements. */
export function validateShapefileFamilyFiles(files: readonly File[], language: 'zh' | 'en'): string | undefined {
  if (files.length === 0) return language === 'zh' ? '请先选择一个或多个 Shapefile 文件族。' : 'Choose one or more Shapefile families first.';
  if (files.length > 256) return language === 'zh' ? '一次最多上传 256 个文件。' : 'Upload at most 256 files at a time.';
  const groups = new Map<string, Set<string>>();
  const names = new Set<string>();
  for (const file of files) {
    const extension = extensionOf(file.name);
    if (!shapefileExtensions.has(extension)) {
      return language === 'zh' ? `不支持 ${file.name}；仅接受 .shp、.shx、.dbf 和可选 .prj。` : `${file.name} is unsupported; choose .shp, .shx, .dbf, and optional .prj files.`;
    }
    if (!stemOf(file.name)) return language === 'zh' ? `文件名无有效主名：${file.name}。` : `The file name has no valid base name: ${file.name}.`;
    const name = file.name.toLocaleLowerCase();
    if (names.has(name)) return language === 'zh' ? `文件名重复：${file.name}。` : `Duplicate file name: ${file.name}.`;
    names.add(name);
    const stem = stemOf(file.name);
    const entries = groups.get(stem) ?? new Set<string>();
    if (entries.has(extension)) return language === 'zh' ? `文件族中重复包含 ${extension}：${file.name}。` : `The family contains duplicate ${extension} files: ${file.name}.`;
    entries.add(extension);
    groups.set(stem, entries);
  }
  for (const [stem, extensions] of groups) {
    if (!extensions.has('.shp')) return language === 'zh' ? `来源文件族 “${stem}” 缺少 .shp 主文件。` : `The source family “${stem}” is missing its .shp primary file.`;
    if (!extensions.has('.shx') || !extensions.has('.dbf')) return language === 'zh' ? `来源文件族 “${stem}” 必须包含 .shp、.shx 和 .dbf。` : `The source family “${stem}” requires .shp, .shx, and .dbf files.`;
  }
  return undefined;
}

function formatSize(bytes: number, language: 'zh' | 'en'): string {
  if (bytes < 1024) return `${bytes} ${language === 'zh' ? '字节' : 'bytes'}`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MiB`;
}

export function DropTarget({ language, files, disabled = false, onFilesChange, onValidationChange }: DropTargetProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [active, setActive] = useState(false);
  const zh = language === 'zh';

  const addFiles = (incoming: File[]): void => {
    const validExtensions = incoming.filter(file => shapefileExtensions.has(extensionOf(file.name)));
    let selectionIssue = validExtensions.length !== incoming.length
      ? (zh ? '选择中包含不支持的文件；仅保留 Shapefile 文件族。' : 'The selection includes unsupported files; only Shapefile family files are accepted.')
      : undefined;
    const merged = [...files];
    for (const file of validExtensions) {
      if (merged.some(existing => existing.name.toLocaleLowerCase() === file.name.toLocaleLowerCase())) {
        selectionIssue = zh ? `文件名重复：${file.name}。请移除旧文件后再选择。` : `Duplicate file name: ${file.name}. Remove the existing file before selecting it again.`;
        continue;
      }
      merged.push(file);
    }
    onFilesChange(merged);
    onValidationChange(selectionIssue ?? validateShapefileFamilyFiles(merged, language));
  };

  const handleFiles = (list: FileList | null): void => {
    if (list) addFiles(Array.from(list));
    if (inputRef.current) inputRef.current.value = '';
  };

  return <section className={`vector-drop-target${active ? ' is-active' : ''}`} aria-label={zh ? '选择 Shapefile 文件' : 'Select Shapefile files'}
    onDragEnter={event => { event.preventDefault(); if (!disabled) setActive(true); }}
    onDragOver={event => { event.preventDefault(); if (!disabled) setActive(true); }}
    onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setActive(false); }}
    onDrop={event => { event.preventDefault(); setActive(false); if (!disabled) handleFiles(event.dataTransfer.files); }}>
    <DropTargetHighlight active={active && !disabled} language={language} />
    <input ref={inputRef} className="vector-drop-file-input" type="file" multiple accept=".shp,.shx,.dbf,.prj"
      disabled={disabled} aria-label={zh ? '选择 Shapefile 文件族' : 'Choose Shapefile family files'}
      onChange={event => handleFiles(event.currentTarget.files)} />
    <div className="vector-drop-copy"><strong>{zh ? '拖放 Shapefile 文件族' : 'Drop a Shapefile family'}</strong>
      <span>{zh ? '同时选择 .shp、.shx、.dbf；.prj 可选。文件会上传到当前工程的来源目录。' : 'Select .shp, .shx, and .dbf together; .prj is optional. Files upload to the current project source folder.'}</span></div>
    <button type="button" className="ci-button" disabled={disabled} onClick={() => inputRef.current?.click()}>
      {zh ? '选择文件' : 'Choose files'}
    </button>
    {files.length > 0 && <ul className="vector-drop-file-list">{files.map((file, index) => <li key={`${file.name.toLocaleLowerCase()}:${file.size}:${file.lastModified}:${index}`}>
      <span><strong>{file.name}</strong><small>{formatSize(file.size, language)}</small></span>
      <button type="button" disabled={disabled} className="vector-drop-remove" aria-label={zh ? `移除 ${file.name}` : `Remove ${file.name}`}
        onClick={() => { const next = files.filter((_, fileIndex) => fileIndex !== index); onFilesChange(next); onValidationChange(validateShapefileFamilyFiles(next, language)); }}>
        {zh ? '移除' : 'Remove'}
      </button>
    </li>)}</ul>}
  </section>;
}
