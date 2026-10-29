/** Comparisons exposed by the original OSM query builder. */
export type OsmTagOperator = 'exists' | 'notExists' | 'equals' | 'notEquals' | 'matches' | 'notMatches' | 'oneOf';

export interface OsmTagOperatorOption {
  operator: OsmTagOperator;
  zh: string;
  en: string;
  requiresValue: boolean;
}

export const OSM_TAG_OPERATORS: readonly OsmTagOperatorOption[] = [
  { operator: 'exists', zh: '存在', en: 'exists', requiresValue: false },
  { operator: 'notExists', zh: '不存在', en: 'does not exist', requiresValue: false },
  { operator: 'equals', zh: '等于', en: '=', requiresValue: true },
  { operator: 'notEquals', zh: '不等于', en: '!=', requiresValue: true },
  { operator: 'matches', zh: '匹配正则表达式', en: 'matches (regex)', requiresValue: true },
  { operator: 'notMatches', zh: '不匹配正则表达式', en: 'does not match (regex)', requiresValue: true },
  { operator: 'oneOf', zh: '属于其中之一（用分号分隔）', en: 'one of (a;b)', requiresValue: true },
];

export function osmOperatorRequiresValue(operator: OsmTagOperator): boolean {
  return operator !== 'exists' && operator !== 'notExists';
}
