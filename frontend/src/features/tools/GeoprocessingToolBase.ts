export type ToolLocale = { zh: string; en: string };
export type ToolGroupDefinition = { id: string; title: ToolLocale; ids: string[] };
export type GeoprocessingFieldValues = Record<string, string | boolean>;
export type ToolField = {
  key: string;
  label: ToolLocale;
  kind: 'number' | 'text' | 'select' | 'toggle';
  initial: string | boolean;
  options?: string[];
  description?: ToolLocale;
  showWhen?: { key: string; value: string | boolean };
  min?: number;
  step?: number;
};

export type GeoprocessingToolDefinition = {
  id: string;
  operation: string;
  title: ToolLocale;
  description: ToolLocale;
  behaviorFile: string;
  sourceFile: string;
  sourceDirectory: string;
  inputKind: 'Polyline' | 'Polygon' | 'Point' | 'Raster' | 'Mixed';
  outputKind: 'Polyline' | 'Polygon' | 'Point' | 'Raster' | 'Text' | 'Mixed';
  fields: ToolField[];
};

const optionLabelsZh: Record<string, string> = {
  Standard: '标准', MovingAverage: '移动平均', EDT: '欧氏距离变换（EDT）', SpatialIndex: '空间索引',
  FourSides: '四邻接', EightCorners: '八邻接', Both: '两侧', Left: '左侧', Right: '右侧',
  PreferClosest: '优先连接最近端点', RejectBranching: '遇到分支时拒绝', Patch: '修补范围', Full: '完整范围',
  Perpendicular: '垂直方向', Radial: '径向', Highest: '最高值', Lowest: '最低值', None: '不取整',
  Tenths: '十分位', Hundredths: '百分位', Thousandths: '千分位', Sixteenths: '1/16', ThirtySeconds: '1/32',
  SixtyFourths: '1/64', HundredTwentyEighths: '1/128', ThousandTwentyFourths: '1/1024',
  Allow: '允许', WriteNDV: '写入 NoData 值', WriteZero: '写入 0',
  Clipboard: '剪贴板', Shapefile: 'Shapefile', 'Map Layer': '地图图层', Tiff: 'TIFF', 'CSV File': 'CSV 文件', 'Text File': '文本文件',
  Raster: '栅格', Polyline: '折线', Polygon: '多边形', Point: '点', Text: '文本', Mixed: '混合要素',
};

export function displayToolOption(value: string, language: 'zh' | 'en'): string {
  return language === 'zh' ? optionLabelsZh[value] ?? value : value;
}

export function displaySourceKind(value: string, language: 'zh' | 'en'): string {
  if (language !== 'zh') return value;
  const kind = value.toLowerCase();
  if (!kind || kind === 'unknown') return '未知';
  if (kind.includes('result')) return '结果';
  if (kind.includes('boundary')) return '边界条件';
  if (kind.includes('corelayer')) return '核心图层';
  if (kind.includes('maplayer')) return '地图图层';
  if (kind.includes('geometry')) return '几何';
  if (kind.includes('terrain')) return '地形';
  if (kind.includes('plan')) return '计划';
  if (kind.includes('raster')) return '栅格';
  if (kind.includes('polyline')) return '折线';
  if (kind.includes('polygon')) return '多边形';
  if (kind.includes('point')) return '点';
  if (kind.includes('folder')) return '文件夹';
  if (kind.includes('project')) return '工程';
  return value;
}

export const gpField = (
  key: string,
  zh: string,
  en: string,
  initial: string | boolean,
  kind: ToolField['kind'] = 'number',
  options?: string[],
  min?: number,
  step?: number,
  showWhen?: { key: string; value: string | boolean },
): ToolField => ({ key, label: { zh, en }, initial, kind, options, min, step, showWhen });

export const geoprocessingSource = {
  directory: 'Visual.Mapping.WinUI.Geoprocessing',
  behaviorDirectory: 'Ras.Interface.WinUI.Behaviors',
};
