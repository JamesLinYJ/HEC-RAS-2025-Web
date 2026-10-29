import { osmOperatorRequiresValue } from './OsmTagOperatorOption';
import type { OsmTagOperator } from './OsmTagOperatorOption';

/** Inputs remain untouched in the editor; only the generated QL follows C# trimming/escaping. */
export interface OsmTagFilterViewModel {
  id: string;
  key: string;
  operator: OsmTagOperator;
  value: string;
}

let nextFilterID = 0;
export function createOsmTagFilter(key = '', operator: OsmTagOperator = 'exists', value = ''): OsmTagFilterViewModel {
  return { id: `osm-filter-${++nextFilterID}`, key, operator, value };
}

// Char.IsWhiteSpace and String.Trim in .NET differ from JavaScript for U+0085 and U+FEFF.
export function trimOsmTag(value: string): string {
  return value.replace(/^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+|[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+$/g, '');
}

function escapeLiteral(value: string): string {
  return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

// Equivalent to Regex.Escape for the characters that .NET treats specially.
function escapeRegex(value: string): string {
  return value.replace(/[\\.*+?|{[()^$#\t\n\r\f ]/g, character => {
    switch (character) {
      case '\t': return '\\t';
      case '\n': return '\\n';
      case '\r': return '\\r';
      case '\f': return '\\f';
      default: return `\\${character}`;
    }
  });
}

export type OsmFilterQlResult = { ql: string; error: null } | { ql: null; error: 'missingValue' | 'noValues' };

/** The caller skips blank keys, as OsmClauseViewModel.BuildClause does. */
export function buildOsmFilterQl(filter: OsmTagFilterViewModel): OsmFilterQlResult {
  const key = escapeLiteral(trimOsmTag(filter.key));
  if (osmOperatorRequiresValue(filter.operator) && !trimOsmTag(filter.value)) return { ql: null, error: 'missingValue' };
  const value = escapeLiteral(trimOsmTag(filter.value));
  switch (filter.operator) {
    case 'exists': return { ql: `["${key}"]`, error: null };
    case 'notExists': return { ql: `[!"${key}"]`, error: null };
    case 'equals': return { ql: `["${key}"="${value}"]`, error: null };
    case 'notEquals': return { ql: `["${key}"!="${value}"]`, error: null };
    case 'matches': return { ql: `["${key}"~"${value}"]`, error: null };
    case 'notMatches': return { ql: `["${key}"!~"${value}"]`, error: null };
    case 'oneOf': {
      const options = filter.value.split(';').map(trimOsmTag).filter(Boolean).map(escapeRegex);
      if (!options.length) return { ql: null, error: 'noValues' };
      return { ql: `["${key}"~"${escapeLiteral(`^(${options.join('|')})$`)}"]`, error: null };
    }
  }
}
