export type ProjectionMatch = { id: string; name: string; code: number; authority: string; area?: string | null };
export type ProjectionEvaluation = {
  isValid: boolean; name: string; units?: string | null; unitName?: string | null;
  epsgCode?: number | null; utmZone?: number | null; wkt?: string | null; error?: string | null;
  normalizedType: string; normalizedValue: string; validity?: string | null;
  isVertical?: boolean | null; isProjected?: boolean | null;
  areaOfUse?: { geographic: { left: number; right: number; bottom: number; top: number }; projected: { left: number; right: number; bottom: number; top: number } } | null;
};
