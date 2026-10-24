import type { BackendRequestOptions } from '../../shared/workbench';

/** Exact camel-case JSON shape returned by the C# Host environment catalogue. */
export interface CumulusEnvironmentOption {
  id: string;
  name: string;
  requiresSignIn: boolean;
  authorizeEndpoint: string | null;
  clientId: string | null;
  redirectUri: string | null;
  idpHint: string | null;
  mockAccessToken: string | null;
  isSelectable: boolean;
}

/** Exact token response DTO used by the C# exchange and refresh routes. */
export interface CumulusTokenSet {
  accessToken: string;
  refreshToken: string | null;
  accessExpiresUtc: string;
  refreshExpiresUtc: string;
}

/** Importer-only in-memory auth state. Mock access tokens have no expiry. */
export interface CumulusSessionToken {
  accessToken: string;
  refreshToken: string | null;
  accessExpiresUtc: string | null;
  refreshExpiresUtc: string | null;
  kind: 'mock' | 'authenticated';
}

/** Product metadata returned by `cumulus.products`. */
export interface CumulusProductOption {
  id: string;
  name: string;
  slug: string | null;
  label: string | null;
  parameter: string | null;
  unit: string | null;
  temporalResolution: number;
  temporalDuration: number;
  dssDatatype: string | null;
  afterUtc: string | null;
  beforeUtc: string | null;
  isPrecip: boolean;
}

/** Preserve the stable server identity and both exact UTC timestamps. */
export interface CumulusGridSourceOption {
  id: string;
  datetimeUtc: string;
  versionUtc: string;
}

export interface CumulusImportExtent {
  minX: number;
  maxX: number;
  minY: number;
  maxY: number;
}

export interface CumulusFootprintCoordinate {
  x: number;
  y: number;
}

export interface CumulusGridFootprintResult {
  sourceId: string;
  datetimeUtc: string;
  versionUtc: string;
  sourceProjectionWkt: string;
  targetProjectionWkt: string;
  extent: CumulusImportExtent;
  points: CumulusFootprintCoordinate[];
}

export interface CumulusProjectMapContext {
  projectionWkt: string | null;
  units: string | null;
}

export type CumulusLayerID = 'precipitation' | 'wind' | 'airPressure' | 'airDensity';

/** A future map capture may provide this only with a real extent and its captured CRS. */
export interface CumulusClipArea {
  extent: CumulusImportExtent;
  projectionWkt: string;
}

/** Fields consumed by the Host's `CumulusGridImportRequest`. */
export interface CumulusGridImportCommand {
  environmentId: string;
  accessToken: string;
  productId: string;
  afterUtc: string;
  beforeUtc: string;
  sources: CumulusGridSourceOption[];
  layerId: CumulusLayerID;
  clipExtent?: CumulusImportExtent;
  clipProjectionWkt?: string;
  precipitationDataType: 'per-cum' | 'per-avg';
  precipitationUnits: 'mm' | 'in';
}

export interface CumulusImportWorkflow {
  canImport: boolean;
  clipArea?: CumulusClipArea | null;
  loadEnvironments(signal: AbortSignal): Promise<CumulusEnvironmentOption[]>;
  loadProducts(environmentId: string, accessToken: string, signal: AbortSignal): Promise<CumulusProductOption[]>;
  queryGrids(environmentId: string, accessToken: string, productId: string, afterUtc: string, beforeUtc: string, signal: AbortSignal): Promise<CumulusGridSourceOption[]>;
  loadProjectMapContext(signal: AbortSignal): Promise<CumulusProjectMapContext>;
  inspectFootprint(environmentId: string, accessToken: string, productId: string, afterUtc: string, beforeUtc: string, source: CumulusGridSourceOption, targetProjectionWkt: string, signal: AbortSignal): Promise<CumulusGridFootprintResult>;
  importGrids(command: CumulusGridImportCommand, onProgress?: BackendRequestOptions['onProgress']): Promise<boolean>;
}
