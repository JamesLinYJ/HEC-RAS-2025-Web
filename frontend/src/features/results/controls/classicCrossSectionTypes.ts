export interface ClassicCrossSectionRow {
  ordinal: number;
  rowToken: string;
  nodeName: string;
  riverName: string;
  reachName: string;
  riverStation: string;
  profilePointCount: number;
  usesOverride: boolean;
}

export interface ClassicCrossSectionCatalog {
  scopeID: string;
  sourcePath: string;
  sourceRevision: string;
  sourceName: string;
  contextToken: string;
  totalCount: number;
  offset: number;
  nextOffset: number | null;
  rows: ClassicCrossSectionRow[];
}

export interface ClassicCrossSectionPoint {
  ordinal: number;
  station: number | null;
  elevation: number | null;
}

export interface ClassicCrossSectionProfile {
  scopeID: string;
  sourcePath: string;
  sourceRevision: string;
  contextToken: string;
  row: ClassicCrossSectionRow;
  hasProfile: boolean;
  points: ClassicCrossSectionPoint[];
  banks: ClassicCrossSectionPoint[];
}
