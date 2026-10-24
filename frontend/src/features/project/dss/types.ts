export interface DssRecordDescriptor {
  path: string;
  recordType: string;
  parts: string[];
  comprisingPaths: string[];
}

export interface DssCatalogResponse {
  path: string;
  revision: string;
  records: DssRecordDescriptor[];
}

export interface DssTimeSeriesData {
  times: string[];
  values: Array<number | null>;
  qualities?: number[] | null;
  units: string;
  dataType: string;
}

export interface DssCurveData {
  label: string;
  values: Array<number | null>;
}

export interface DssPairedData {
  ordinates: Array<number | null>;
  curves: DssCurveData[];
  independentUnits: string;
  dependentUnits: string;
  independentType: string;
  dependentType: string;
}

export interface DssLocationData {
  x: number | null;
  y: number | null;
  z: number | null;
  coordinateSystem: string;
  coordinateID: number;
  horizontalUnits: number;
  horizontalDatum: number;
  verticalUnits: number;
  verticalDatum: number;
  timeZoneName: string;
  supplemental: string;
}

export interface DssRecordResult {
  path: string;
  recordType: string;
  kind: string;
  error?: string | null;
  timeSeries?: DssTimeSeriesData | null;
  pairedData?: DssPairedData | null;
  location?: DssLocationData | null;
}

export interface DssReadResponse {
  path: string;
  revision: string;
  records: DssRecordResult[];
}
