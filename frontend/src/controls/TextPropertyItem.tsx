import { useState } from 'react';

/** Read-only text display counterpart for Visual.WinUI.Controls.TextPropertyItem. */
export function TextPropertyItem({ value, language }: { value: string; language: 'zh' | 'en' }) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const copyText = async () => {
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard API unavailable');
      await navigator.clipboard.writeText(value);
      setCopyState('copied');
    } catch {
      setCopyState('failed');
    }
  };
  const copyLabel = language === 'zh' ? '复制文本' : 'Copy text';
  const copiedLabel = language === 'zh' ? '已复制' : 'Copied';
  const failedLabel = language === 'zh' ? '复制失败' : 'Copy failed';
  return <div className="editor-text-property-item">
    <span className="editor-text-property-value">{value}</span>
    <button type="button" className="editor-text-property-copy" aria-label={copyLabel} title={copyLabel} disabled={!value} onClick={() => void copyText()}>{copyState === 'copied' ? copiedLabel : copyState === 'failed' ? failedLabel : copyLabel}</button>
  </div>;
}
