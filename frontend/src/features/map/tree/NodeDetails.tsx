import type { ReactNode } from 'react';
import { mapError } from '../mapText';
import type { MapTreeLanguage, MapTreeNodeData } from './types';

export function InfoLine({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value) return null;
  return <span className="map-tree-detail" title={value}><span>{label}</span> {value}</span>;
}

export function DetailGroup({ children }: { children: ReactNode }) {
  return <span className="map-tree-details">{children}</span>;
}

export function DiagnosticRows({ node, language, depth }: { node: MapTreeNodeData; language: MapTreeLanguage; depth: number }) {
  const messages: string[] = [];
  if (node.diagnosticReadError) messages.push(mapError(language, node.diagnosticReadError));
  for (const error of node.layerErrors ?? []) {
    const message = error.evaluatedMessage || error.code;
    if (message) messages.push(mapError(language, message));
  }
  if (node.resultComputeState === 'failed' && node.resultComputeFailureReason) {
    messages.push(mapError(language, node.resultComputeFailureReason));
  }
  if (messages.length === 0) return null;
  return (
    <div className="map-tree-error" role="status" style={{ marginInlineStart: 35 + depth * 15 }}>
      {messages.map((message, index) => <div key={`${index}:${message}`}>{message}</div>)}
    </div>
  );
}
