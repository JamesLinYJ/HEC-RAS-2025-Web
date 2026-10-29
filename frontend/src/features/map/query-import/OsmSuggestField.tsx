import { useEffect, useId, useRef, useState } from 'react';
import type { BackendGateway, Language } from '../../../shared/workbench';
import { text } from '../../../shared/workbench';
import { trimOsmTag } from './OsmTagFilterViewModel';

export type OsmSuggestFieldKind = 'key' | 'value';

interface SuggestIdentity {
  backend: BackendGateway;
  backendRevision: number;
  field: OsmSuggestFieldKind;
  keyText: string;
  value: string;
  enabled: boolean;
}

interface SuggestResult {
  identity: SuggestIdentity;
  suggestions: string[];
  failed: boolean;
}

function sameIdentity(a: SuggestIdentity, b: SuggestIdentity): boolean {
  return a.backend === b.backend && a.backendRevision === b.backendRevision && a.field === b.field
    && a.keyText === b.keyText && a.value === b.value && a.enabled === b.enabled;
}

function parseSuggestions(response: unknown): string[] | null {
  // The HTTP operation protocol can return a cancellation receipt as its result.
  if (response && typeof response === 'object' && 'state' in response && response.state === 'canceled') return null;
  if (!response || typeof response !== 'object' || !('suggestions' in response)
    || !Array.isArray(response.suggestions) || !response.suggestions.every(item => typeof item === 'string')) {
    throw new Error('Invalid OSM tag suggestion response.');
  }
  return response.suggestions;
}

/** Debounced TagInfo suggestions, tied to the exact field, text and backend that requested them. */
export function OsmSuggestField({ backend, backendRevision = 0, language, disabled, field, keyText, value, onChange, label }: {
  backend: BackendGateway;
  backendRevision?: number;
  language: Language;
  disabled: boolean;
  field: OsmSuggestFieldKind;
  keyText: string;
  value: string;
  onChange: (value: string) => void;
  label: string;
}) {
  const listID = useId();
  const [focused, setFocused] = useState(false);
  const [result, setResult] = useState<SuggestResult | null>(null);
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const supported = backend.supports('featureQuery.suggest');
  const identity: SuggestIdentity = { backend, backendRevision, field, keyText, value,
    enabled: focused && !disabled && supported && (field === 'key' || Boolean(trimOsmTag(keyText))) };
  const current = useRef(identity);
  // A render changes the guard immediately, before the previous effect's cleanup.
  current.current = identity;

  function cancelPending() {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
  }

  useEffect(() => {
    if (!identity.enabled) return;
    const captured = identity;
    const requestGeneration = ++generation.current;
    const requestController = new AbortController();
    controller.current = requestController;
    const timer = window.setTimeout(() => {
      void backend.request<unknown>('featureQuery.suggest', {
        key: keyText, value, isKey: field === 'key',
      }, { signal: requestController.signal }).then(response => {
        const suggestions = parseSuggestions(response);
        if (generation.current === requestGeneration && !requestController.signal.aborted
          && sameIdentity(captured, current.current)) {
          setResult(suggestions === null ? null : { identity: captured, suggestions, failed: false });
        }
      }).catch(() => {
        if (generation.current === requestGeneration && !requestController.signal.aborted
          && sameIdentity(captured, current.current)) setResult({ identity: captured, suggestions: [], failed: true });
      });
    }, 300);
    return () => {
      window.clearTimeout(timer);
      requestController.abort();
      if (controller.current === requestController) controller.current = null;
    };
  }, [backend, backendRevision, disabled, field, focused, keyText, supported, value]);

  const visibleResult = result && sameIdentity(result.identity, identity) ? result : null;
  return <span className="feature-query-osm-suggest">
    <input type="text" aria-label={label} disabled={disabled} value={value} spellCheck={false}
      list={visibleResult?.suggestions.length ? listID : undefined}
      onFocus={() => setFocused(true)}
      onBlur={() => { cancelPending(); setFocused(false); }}
      onChange={event => { cancelPending(); onChange(event.target.value); }} />
    {visibleResult?.suggestions.length ? <datalist id={listID}>
      {visibleResult.suggestions.map((suggestion, index) => <option key={`${index}:${suggestion}`} value={suggestion} />)}
    </datalist> : null}
    {visibleResult?.failed && <small>{text(language, '标签建议暂不可用，可继续手动输入。', 'Tag suggestions are unavailable; you can still enter a value.')}</small>}
  </span>;
}
