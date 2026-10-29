import { createOsmTagFilter } from './OsmTagFilterViewModel';
import type { OsmTagFilterViewModel } from './OsmTagFilterViewModel';

/** Filters within a clause are ANDed; clauses themselves are a union. */
export interface OsmClauseViewModel {
  id: string;
  includeNodes: boolean;
  includeWays: boolean;
  includeRelations: boolean;
  filters: OsmTagFilterViewModel[];
}

let nextClauseID = 0;
export function createOsmClause(
  includeNodes = true, includeWays = true, includeRelations = true,
  filters: OsmTagFilterViewModel[] = [createOsmTagFilter()],
): OsmClauseViewModel {
  return { id: `osm-clause-${++nextClauseID}`, includeNodes, includeWays, includeRelations, filters };
}

export function osmKindTokens(clause: OsmClauseViewModel): string[] {
  if (clause.includeNodes && clause.includeWays && clause.includeRelations) return ['nwr'];
  return [clause.includeNodes && 'node', clause.includeWays && 'way', clause.includeRelations && 'relation']
    .filter((token): token is string => Boolean(token));
}
