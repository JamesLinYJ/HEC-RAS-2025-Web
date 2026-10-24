export type WireObject = Record<string, unknown>;

export interface WilcockSbtmDraft extends WireObject {
  shearVelocity?: string | null;
  waterDensity?: string | null;
  sedimentDensity?: string | null;
  grainDiameter?: string[] | null;
  surfaceCdf?: string[] | null;
}

export interface WilcockIsbtmDraft extends WireObject {
  totalTransportRate?: string | null;
  grainDiameter?: string[] | null;
  transportCdf?: string[] | null;
}

export interface WilcockStabDraft extends WireObject {
  waterDischarges?: string[] | null;
  sedimentSupplyRates?: string[] | null;
  transportGrainSizes?: string[][] | null;
  bMin?: string | null;
  bMax?: string | null;
  z?: string | null;
  ns?: string | null;
  nSpec?: string | null;
  useThresholdSlope?: boolean;
  tcTauStarC?: string | null;
  tcD50?: string | null;
  tcD84?: string | null;
  tcM?: string | null;
  grainDiameter?: string[] | null;
}

export interface WilcockStateDraft extends WireObject {
  grainDiameter?: string[] | null;
  firstTransportCdfGrainSize?: string[] | null;
  secondTransportCdfGrainSize?: string[] | null;
  transportRates?: string[] | null;
  slopes?: string[] | null;
}

export interface WilcockDraft extends WireObject {
  numberCulture?: string | null;
  btmMode?: string | null;
  sbtm?: WilcockSbtmDraft | null;
  isbtm?: WilcockIsbtmDraft | null;
  stab?: WilcockStabDraft | null;
  state?: WilcockStateDraft | null;
  selectedCaseID?: string | null;
  selectedPlotIndex?: number | null;
}

export interface WilcockCaseChoice extends WireObject {
  id: string;
  index: number;
  waterDischarge: string;
  sedimentSupplyRate: string;
}

export interface WilcockInputIssue extends WireObject {
  section: string;
  path: string;
  messageKey: string;
}

export interface WilcockTableColumn extends WireObject {
  id: string;
  titleKey: string;
  sourceType: string;
  isEditable: boolean;
  caseIndex?: number | null;
}

export interface WilcockTableRow extends WireObject {
  id: string;
  cells: string[];
  titleKey?: string | null;
  caseIndex?: number | null;
}

export interface WilcockSourceTable extends WireObject {
  id: string;
  section: string;
  role: string;
  titleKey: string;
  columns: WilcockTableColumn[];
  rows: WilcockTableRow[];
}

export interface WilcockChartPoint extends WireObject {
  x: number | null;
  y: number | null;
}

export interface WilcockChartSeries extends WireObject {
  caseIndex?: number | null;
  labelKey: string;
  colorARGB: string;
  strokeWidth: number;
  points: WilcockChartPoint[];
}

export interface WilcockChart extends WireObject {
  id: string;
  section: string;
  titleKey: string;
  xAxisKey: string;
  yAxisKey: string;
  series: WilcockChartSeries[];
}

export interface WilcockWorkspaceSnapshot extends WireObject {
  draft: WilcockDraft;
  btmMode: string;
  cases: WilcockCaseChoice[];
  selectedCaseID: string | null;
  selectedPlotIndex: number;
  canAddCase: boolean;
  canRemoveCase: boolean;
  canImportCase: boolean;
  btmImportPolicy: string;
  btmImportReasonKey: string | null;
  importConfirmationRequired: boolean;
  replacedExistingCases: boolean;
  sbtmResults: WireObject | null;
  isbtmResults: WireObject | null;
  stabResults: WireObject | null;
  stateResults: WireObject | null;
  tables: WilcockSourceTable[];
  charts: WilcockChart[];
  inputIssues: WilcockInputIssue[];
  sbtmErrorKey: string | null;
  isbtmErrorKey: string | null;
  stabErrorKey: string | null;
  stateErrorKey: string | null;
}

export interface UnitCalculatorUnit extends WireObject {
  id: string;
  name: string;
  unitTypeName: string;
}

export interface UnitCalculatorLoadResult extends WireObject {
  fromUnits: UnitCalculatorUnit[];
  toUnits: UnitCalculatorUnit[];
  fromUnitId: string | null;
  fromUnitName: string | null;
  unitTypeName: string | null;
  toUnitId: string | null;
}

export interface UnitCalculatorConvertResult extends WireObject {
  value: number;
}
