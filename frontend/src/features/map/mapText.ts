import type { Language } from '../../shared/workbench';

const documentKinds: Record<string, readonly [string, string]> = {
  Project: ['工程', 'Project'],
  Buildable: ['可建造', 'Buildable'],
  Geometry: ['几何', 'Geometry'],
  Plan: ['方案', 'Plan'],
  PointsVector: ['点图层', 'Point layer'],
  PolylinesVector: ['线图层', 'Line layer'],
  PolygonsVector: ['面图层', 'Polygon layer'],
  GdbDataset: ['地理数据库', 'Geodatabase'],
  GdbVectorLayer: ['GDB 矢量', 'GDB vector'],
  SingleBandRasterMapLayer: ['单波段栅格', 'Single-band raster'],
  ImageRasterMapLayer: ['影像栅格', 'Image raster'],
  DiscreteRasterMapLayer: ['离散栅格', 'Discrete raster'],
};

const geometryTypes: Record<string, readonly [string, string]> = {
  Point: ['点', 'Point'],
  MultiPoint: ['多点', 'MultiPoint'],
  LineString: ['线', 'LineString'],
  MultiLineString: ['多线', 'MultiLineString'],
  Polygon: ['多边形', 'Polygon'],
  MultiPolygon: ['多面', 'MultiPolygon'],
  GeometryCollection: ['几何集合', 'GeometryCollection'],
};

export function mapDocumentKind(language: Language, value: string): string {
  const known = documentKinds[value];
  return known ? known[language === 'zh' ? 0 : 1] : language === 'zh' ? '工程地图' : value;
}

export function mapGeometryType(language: Language, value: string): string {
  const known = geometryTypes[value];
  return known ? known[language === 'zh' ? 0 : 1] : language === 'zh' ? '几何' : value;
}

export function mapCRS(language: Language, value: string): string {
  return language === 'zh' && value === 'Unknown CRS' ? '未知坐标系' : value;
}

export function mapError(language: Language, value: unknown): string {
  if (value && typeof value === 'object') {
    const localizedMessage = (value as { localizedMessage?: unknown }).localizedMessage;
    if (typeof localizedMessage === 'function') {
      try { return localizedMessage.call(value, language); }
      catch { /* Fall through to the stable error message. */ }
    }
  }
  const raw = value instanceof Error ? value.message : String(value ?? '');
  if (language !== 'zh' || !raw) return raw;

  if (/后端尚未发布操作：map\.document\.create/i.test(raw)) return '当前服务端尚不支持创建地图文档。';
  if (/The saved map catalog response is invalid|The saved map catalog response is incomplete|The saved map catalog contains an invalid map entry|The saved map catalog contains an incomplete map entry/i.test(raw)) return '服务端返回的已保存地图列表格式无效。';
  if (/The project changed while its saved maps were being read/i.test(raw)) return '读取已保存地图期间工程发生变化，请重新载入工程后重试。';
  if (/The project or saved map list changed while its layer trees were being read/i.test(raw)) return '读取地图图层期间，工程或地图列表发生变化，请重新读取地图。';
  if (/The saved map catalog has no visualization revision/i.test(raw)) return '服务端未提供地图文件版本，无法安全读取图层树。';
  if (/The saved map changed while its layer tree was being read/i.test(raw)) return '读取图层树期间地图文件发生变化，请重新读取地图。';
  if (/Open a saved \.ras project before loading saved maps/i.test(raw)) return '请先打开已保存的 .ras 工程，再读取已保存地图。';
  if (/The saved project revision is required to list its map documents/i.test(raw)) return '当前工程尚无可用于校验地图列表的版本信息，请重新载入已保存的工程。';
  if (/后端尚未发布操作：map\.tree\.(?:load|perform)/i.test(raw)) return '当前服务端尚不支持读取或修改地图图层树。';
  if (/Could not read GeoJSON:/i.test(raw)) return '无法解析 GeoJSON 文件，请检查 JSON 格式。';
  if (/Map extent is outside the numeric range that can be displayed/i.test(raw)) return '地图坐标范围超出当前画布可显示的数值范围。';
  if (/Map extent is too extreme to render at the available numeric precision/i.test(raw)) return '地图坐标范围过大，当前画布无法稳定绘制；原有图层未更改。';
  if (/coordinate that is not a finite x\/y pair/i.test(raw)) return 'GeoJSON 中包含无效坐标；每个坐标必须是有限的 X、Y 数值。';
  if (/GeoJSON (?:MultiPoint|MultiLineString|MultiPolygon) coordinates must be an array/i.test(raw)) return 'GeoJSON 坐标格式无效，应为坐标数组。';
  if (/line coordinates must be an array/i.test(raw)) return 'GeoJSON 线坐标格式无效，应为坐标数组。';
  if (/polygon coordinates must be an array of rings/i.test(raw)) return 'GeoJSON 面坐标格式无效，应包含环坐标数组。';
  if (/feature has no supported geometry/i.test(raw)) return 'GeoJSON 要素缺少支持的几何类型。';
  if (/GeometryCollection requires a geometries array/i.test(raw)) return 'GeoJSON 几何集合缺少几何数组。';
  if (/FeatureCollection contains a non-Feature item/i.test(raw)) return 'GeoJSON 要素集合中包含无效项目。';
  if (/does not contain any drawable features/i.test(raw)) return 'GeoJSON 中没有可绘制的要素。';
  if (/Choose a GeoJSON Feature/i.test(raw)) return '请选择有效的 GeoJSON 要素、要素集合或几何文档。';
  if (/has no finite coordinates/i.test(raw)) return 'GeoJSON 中没有可显示的有限坐标。';
  if (/Unsupported GeoJSON geometry:/i.test(raw)) return raw.replace(/Unsupported GeoJSON geometry:/i, '不支持的 GeoJSON 几何类型：').replace(/^(.+：) /, '$1').replace(/\.$/, '。');
  if (/cannot verify the project XML snapshot/i.test(raw)) return '当前浏览器无法校验工程 XML 快照。';
  if (/Open a saved \.ras project before loading its map tree/i.test(raw)) return '请先打开已保存的 .ras 工程，再读取地图图层树。';
  if (/Open a saved \.ras project before creating a map document/i.test(raw)) return '请先打开已保存的 .ras 工程，再创建地图文档。';
  if (/backend map tree reader is unavailable/i.test(raw)) return '当前后端未提供地图图层树读取功能。';
  if (/Failed to fetch|NetworkError|fetch failed/i.test(raw)) return '无法连接后端服务，请检查服务状态和网络连接。';
  if (/map\.tree\.(?:load|perform)|map\.document\.create/i.test(raw)) return /not available|unavailable/i.test(raw) ? '当前服务端尚不支持此地图操作。' : `地图操作未能完成：${raw}`;
  return `操作未能完成：${raw}`;
}
