import { createOsmClause, osmKindTokens } from './OsmClauseViewModel';
import type { OsmClauseViewModel } from './OsmClauseViewModel';
import { buildOsmFilterQl, createOsmTagFilter, trimOsmTag } from './OsmTagFilterViewModel';
import type { OsmTagOperator } from './OsmTagOperatorOption';

export interface OsmQueryBuilderViewModel {
  label: string;
  timeoutSeconds: number;
  clauses: OsmClauseViewModel[];
}

export type OsmBuilderError = { code: 'noClause' | 'noKinds' | 'missingValue' | 'noValues'; key?: string };
export type OsmBuilderQlResult = { ql: string; error: null } | { ql: null; error: OsmBuilderError };

/** Mirrors OverpassQuery.TryBuildTemplateQl; the Host replaces {{bbox}} at query time. */
export function buildOsmTemplateQl(builder: OsmQueryBuilderViewModel): OsmBuilderQlResult {
  if (!builder.clauses.length) return { ql: null, error: { code: 'noClause' } };
  const lines = [`[out:json][timeout:${builder.timeoutSeconds}];`, '('];
  for (const clause of builder.clauses) {
    const kinds = osmKindTokens(clause);
    if (!kinds.length) return { ql: null, error: { code: 'noKinds' } };
    let filters = '';
    for (const filter of clause.filters) {
      if (!trimOsmTag(filter.key)) continue;
      const result = buildOsmFilterQl(filter);
      if (result.error) return { ql: null, error: { code: result.error, key: filter.key } };
      filters += result.ql;
    }
    for (const kind of kinds) lines.push(`  ${kind}${filters}({{bbox}});`);
  }
  lines.push(');', 'out geom;');
  return { ql: lines.join('\n'), error: null };
}

type PresetClause = { nodes?: boolean; ways?: boolean; relations?: boolean; key: string; operator: OsmTagOperator; value?: string };
export interface OsmPreset {
  id: string;
  nameZh: string;
  nameEn: string;
  descriptionZh: string;
  descriptionEn: string;
  label: string;
  clauses: readonly PresetClause[];
}

/** Preset labels and filters reproduce Geospatial.IO.Network.OSM.OsmPresets. */
export const OSM_PRESETS: readonly OsmPreset[] = [
  { id: 'Buildings', nameZh: '建筑物', nameEn: 'Buildings', descriptionZh: '建筑物轮廓，可用于建筑清单和水流障碍分析。', descriptionEn: 'Building footprints, for structure inventory and hydraulic obstructions.', label: 'Buildings', clauses: [{ ways: true, relations: true, key: 'building', operator: 'exists' }] },
  { id: 'MajorRoads', nameZh: '主要道路', nameEn: 'Major Roads', descriptionZh: '高速、公路主干线、一二级道路及连接匝道，可用于分析水流障碍和疏散路线。', descriptionEn: 'Motorways, trunk, primary, and secondary roads (and their link ramps) - key hydraulic barriers and evacuation routes.', label: 'MajorRoads', clauses: [{ ways: true, key: 'highway', operator: 'oneOf', value: 'motorway;trunk;primary;secondary;motorway_link;trunk_link;primary_link;secondary_link' }] },
  { id: 'AllRoads', nameZh: '全部道路', nameEn: 'All Roads', descriptionZh: '所有带道路标签的线路，包括高速公路和人行道；大范围查询可能返回大量数据。', descriptionEn: 'Every tagged road, from motorways to footpaths. Can return a very large dataset over wide extents.', label: 'AllRoads', clauses: [{ ways: true, key: 'highway', operator: 'exists' }] },
  { id: 'Railways', nameZh: '铁路', nameEn: 'Railways', descriptionZh: '铁路、轻轨、地铁、有轨电车和窄轨线路。', descriptionEn: 'Rail, light rail, subway, tram, and narrow-gauge tracks.', label: 'Railways', clauses: [{ ways: true, key: 'railway', operator: 'oneOf', value: 'rail;light_rail;subway;tram;narrow_gauge' }] },
  { id: 'Bridges', nameZh: '桥梁', nameEn: 'Bridges', descriptionZh: '标记为桥梁的结构，包括跨越道路、铁路或水道的桥梁。', descriptionEn: 'Structures tagged as bridges, spanning roads, railways, or waterways.', label: 'Bridges', clauses: [{ ways: true, key: 'bridge', operator: 'exists' }] },
  { id: 'Culverts', nameZh: '涵洞', nameEn: 'Culverts', descriptionZh: '标记为涵洞的线路，道路、铁路或水道可从地下通过。', descriptionEn: 'Ways tagged as culverts, where a road, rail, or waterway passes underground.', label: 'Culverts', clauses: [{ ways: true, key: 'tunnel', operator: 'equals', value: 'culvert' }] },
  { id: 'DamsAndWeirs', nameZh: '水坝与堰', nameEn: 'Dams & Weirs', descriptionZh: '以横断线或面表示的水坝与堰。', descriptionEn: 'Dams and weirs, mapped either as crossing lines or as areas.', label: 'DamsAndWeirs', clauses: [{ ways: true, relations: true, key: 'waterway', operator: 'oneOf', value: 'dam;weir' }] },
  { id: 'LeveesAndEmbankments', nameZh: '堤防与路堤', nameEn: 'Levees & Embankments', descriptionZh: '防洪堤、防洪堤坝以及修筑在路堤上的道路或铁路。', descriptionEn: 'Flood-control levees/dykes, and roads or rail built up on embankments.', label: 'LeveesAndEmbankments', clauses: [{ ways: true, relations: true, key: 'man_made', operator: 'oneOf', value: 'dyke;embankment' }, { ways: true, key: 'embankment', operator: 'equals', value: 'yes' }] },
  { id: 'Waterways', nameZh: '水道', nameEn: 'Waterways', descriptionZh: '河流、溪流、运河、排水渠和沟渠的中心线。', descriptionEn: 'River, stream, canal, drain, and ditch centerlines.', label: 'Waterways', clauses: [{ ways: true, key: 'waterway', operator: 'oneOf', value: 'river;stream;canal;drain;ditch' }] },
  { id: 'WaterBodies', nameZh: '水体', nameEn: 'Water Bodies', descriptionZh: '以面表示的湖泊、池塘和水库。', descriptionEn: 'Lakes, ponds, and reservoirs mapped as areas.', label: 'WaterBodies', clauses: [{ ways: true, relations: true, key: 'natural', operator: 'equals', value: 'water' }, { ways: true, relations: true, key: 'landuse', operator: 'equals', value: 'reservoir' }] },
  { id: 'Wetlands', nameZh: '湿地', nameEn: 'Wetlands', descriptionZh: '沼泽、湿沼和其他湿地区域。', descriptionEn: 'Marsh, swamp, and other wetland areas.', label: 'Wetlands', clauses: [{ ways: true, relations: true, key: 'natural', operator: 'equals', value: 'wetland' }] },
  { id: 'LandUse', nameZh: '土地利用', nameEn: 'Land Use', descriptionZh: '土地利用面，可用于分配糙率值。', descriptionEn: 'Land use polygons, useful for assigning roughness values.', label: 'LandUse', clauses: [{ ways: true, relations: true, key: 'landuse', operator: 'exists' }] },
  { id: 'AdministrativeBoundaries', nameZh: '行政边界', nameEn: 'Administrative Boundaries', descriptionZh: '行政边界关系，包括国家、省州、县和市镇。', descriptionEn: 'Administrative boundary relations (countries, states, counties, municipalities).', label: 'AdministrativeBoundaries', clauses: [{ relations: true, key: 'boundary', operator: 'equals', value: 'administrative' }] },
  { id: 'Places', nameZh: '地点', nameEn: 'Places', descriptionZh: '城市、镇、村和聚落的位置点。', descriptionEn: 'City, town, village, and hamlet location points.', label: 'Places', clauses: [{ nodes: true, key: 'place', operator: 'oneOf', value: 'city;town;village;hamlet' }] },
];

export function osmBuilderFromPreset(preset: OsmPreset): OsmQueryBuilderViewModel {
  return {
    label: preset.label,
    timeoutSeconds: 180,
    clauses: preset.clauses.map(clause => createOsmClause(Boolean(clause.nodes), Boolean(clause.ways),
      Boolean(clause.relations), [createOsmTagFilter(clause.key, clause.operator, clause.value ?? '')])),
  };
}
