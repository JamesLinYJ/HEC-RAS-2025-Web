import type { CSSProperties } from 'react';
import type { Language } from '../../shared/workbench';
import { gerror, gt } from './geometryText';
import { geometryDiagnosticMessage } from './geometryErrors';

export interface GeometryErrorProps {
  language: Language;
  error: unknown;
  title?: string;
  style?: CSSProperties;
}

/** Shows a localized summary while keeping the unmodified backend/exception text available. */
export function GeometryError({ language, error, title, style }: GeometryErrorProps) {
  const raw = geometryDiagnosticMessage(error);
  if (!raw) return null;
  return (
    <div className="geometry-error" role="alert" style={style}>
      {title ? <strong>{title}{language === 'zh' ? '：' : ': '}</strong> : null}
      {gerror(language, raw)}
      <details style={{ marginTop: 7 }}>
        <summary>{gt(language, 'rawErrorDetails')}</summary>
        <pre style={{ whiteSpace: 'pre-wrap', marginBottom: 0 }}>{raw}</pre>
      </details>
    </div>
  );
}

export default GeometryError;
