interface DiagnosticError {
  code?: unknown;
  diagnosticMessage?: unknown;
  details?: unknown;
}

/** Keep backend diagnostics rather than the localized summary on Error.message. */
export function geometryDiagnosticMessage(error: unknown): string {
  if (error && typeof error === 'object') {
    const candidate = error as DiagnosticError;
    const code = typeof candidate.code === 'string' && candidate.code.trim() ? candidate.code.trim() : '';
    const detailsMessage = diagnosticFromDetails(candidate.details);
    const message = typeof candidate.diagnosticMessage === 'string' && candidate.diagnosticMessage.length > 0
      ? candidate.diagnosticMessage
      : detailsMessage;
    if (message) return code ? `${message} [${code}]` : message;
  }
  return error instanceof Error ? error.message : String(error ?? '');
}

function diagnosticFromDetails(details: unknown): string {
  if (typeof details === 'string') return details;
  if (!details || typeof details !== 'object' || Array.isArray(details)) return '';
  const record = details as Record<string, unknown>;
  for (const key of ['diagnosticMessage', 'message', 'detail', 'title']) {
    if (typeof record[key] === 'string' && record[key].length > 0) return record[key];
  }
  return '';
}
