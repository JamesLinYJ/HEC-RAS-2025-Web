import type { Language } from '../../shared/workbench';

/** Literal section identifiers exposed by Ras.Visual.WinUI.Tree.ProjectTreeModeSectionTitles. */
export const ProjectTreeModeSectionTitles = {
  Geometries: 'Geometries',
  Plans: 'Plans',
  Results: 'Results',
  Terrains: 'Terrains',
  SurfaceLayers: 'Surface Layers',
  MapLayers: 'Map Layers',
  Features: 'Features',
  BoundaryConditions: 'Boundary Conditions',
} as const;

export type ProjectTreeModeSectionTitle = typeof ProjectTreeModeSectionTitles[keyof typeof ProjectTreeModeSectionTitles];

const localizedSectionTitles: Partial<Record<string, readonly [string, string]>> = {
  [ProjectTreeModeSectionTitles.Geometries]: ['几何', 'Geometries'],
  [ProjectTreeModeSectionTitles.Plans]: ['计划', 'Plans'],
  [ProjectTreeModeSectionTitles.Results]: ['结果', 'Results'],
  [ProjectTreeModeSectionTitles.Terrains]: ['地形', 'Terrains'],
  [ProjectTreeModeSectionTitles.SurfaceLayers]: ['地表图层', 'Surface Layers'],
  [ProjectTreeModeSectionTitles.MapLayers]: ['地图图层', 'Map Layers'],
  [ProjectTreeModeSectionTitles.Features]: ['要素', 'Features'],
  [ProjectTreeModeSectionTitles.BoundaryConditions]: ['边界条件', 'Boundary Conditions'],
  Layers: ['图层', 'Layers'],
  'External Connections': ['外部连接', 'External Connections'],
  Maps: ['地图', 'Maps'],
  NValues: ['糙率图层', 'N-Value Layers'],
  Classifications: ['分类图层', 'Classifications'],
  'Focused Plans': ['聚焦计划', 'Focused Plans'],
  'Focused Geometries': ['聚焦几何', 'Focused Geometries'],
  'Matching Plan Layers': ['匹配计划图层', 'Matching Plan Layers'],
  'Matching Geometry Layers': ['匹配几何图层', 'Matching Geometry Layers'],
  'Matching Layers': ['匹配图层', 'Matching Layers'],
  Backups: ['备份', 'Backups'],
  Project: ['工程', 'Project'],
  'Conceptual Mesh': ['概念网格', 'Conceptual Mesh'],
  'Computational Mesh': ['计算网格', 'Computational Mesh'],
  'Culvert Barrels': ['涵洞管段', 'Culvert Barrels'],
  'Boundary Condition Lines': ['边界条件线', 'Boundary Condition Lines'],
  Precipitation: ['降雨', 'Precipitation'],
  'Wind Forces': ['风力', 'Wind Forces'],
  Topology: ['拓扑', 'Topology'],
  'Cross Sections': ['横断面', 'Cross Sections'],
};

export function localizeProjectTreeSectionTitle(language: Language, title: string): string {
  const labels = localizedSectionTitles[title];
  return labels ? labels[language === 'zh' ? 0 : 1] : title;
}

export function localizeProjectTreeSourceClass(language: Language, sourceClass: string): string {
  const names: Record<string, readonly [string, string]> = {
    ObservableGeometry: ['几何', 'Geometry'],
    ObservableTerrain: ['地形', 'Terrain'],
    ObservableBoundaryCondition: ['边界条件', 'Boundary condition'],
    ObservablePlan: ['计划', 'Plan'],
    ObservableResult: ['结果', 'Result'],
    ObservableClassificationLayer: ['分类图层', 'Classification layer'],
    ObservableNValueLayer: ['糙率图层', 'N-value layer'],
    Geometry: ['几何', 'Geometry'],
    Terrain: ['地形', 'Terrain'],
    BoundaryCondition: ['边界条件', 'Boundary condition'],
    Plan: ['计划', 'Plan'],
    Result: ['结果', 'Result'],
    ClassificationLayer: ['分类图层', 'Classification layer'],
    NValueLayer: ['糙率图层', 'N-value layer'],
    MultiResultNodeVM: ['多项结果', 'Multiple results'],
    BackupNodeVM: ['备份', 'Backup'],
  };
  const direct = names[sourceClass];
  if (direct) return direct[language === 'zh' ? 0 : 1];
  if (language !== 'zh') return sourceClass;
  const value = sourceClass.toLocaleLowerCase();
  if (value.includes('boundary')) return '边界条件';
  if (value.includes('geometry')) return '几何';
  if (value.includes('terrain')) return '地形';
  if (value.includes('result')) return '结果';
  if (value.includes('plan')) return '计划';
  if (value.includes('classification')) return '分类图层';
  if (value.includes('nvalue') || value.includes('n_value')) return '糙率图层';
  if (value.includes('map')) return '地图图层';
  if (value.includes('connection')) return '外部连接';
  if (value.includes('backup')) return '备份';
  if (value.includes('layer')) return '图层';
  return '工程对象';
}

export function localizeProjectTreeStatus(language: Language, status: string, kind: 'diagnostic' | 'compute'): string {
  if (language !== 'zh') return status;
  const diagnostic: Record<string, string> = {
    loaded: '已加载',
    unavailable: '不可用',
    readFailed: '读取失败',
    sourceChanged: '源文件已变化',
  };
  const compute: Record<string, string> = { completed: '已完成', failed: '失败' };
  return (kind === 'diagnostic' ? diagnostic : compute)[status] ?? '未知状态';
}
