import { BufferPolylineGpTool } from './BufferPolylineGpTool';
import { BufferPolygonGpTool } from './BufferPolygonGpTool';
import { ClipRasterToPolygonGpTool } from './ClipRasterToPolygonGpTool';
import { CullByLengthGpTool } from './CullByLengthGpTool';
import { DensifyPolygonsGpTool } from './DensifyPolygonsGpTool';
import { DensifyPolylinesGpTool } from './DensifyPolylinesGpTool';
import { DistanceToLinesGpTool } from './DistanceToLinesGpTool';
import { FilterGpTool } from './FilterGpTool';
import { FlatAreaDetectorGpTool } from './FlatAreaDetectorGpTool';
import { LaplacianTerrainGpTool } from './LaplacianTerrainGpTool';
import { LineOffsetGpTool } from './LineOffsetGpTool';
import { MergePolylinesGpTool } from './MergePolylinesGpTool';
import { NoDataLaplacianFillGpTool } from './NoDataLaplacianFillGpTool';
import { PolygonToPolylineGpTool } from './PolygonToPolylineGpTool';
import { PolylineToPolygonGpTool } from './PolylineToPolygonGpTool';
import { ProximityHeatmapGpTool } from './ProximityHeatmapGpTool';
import { RasterCutStatsGpTool } from './RasterCutStatsGpTool';
import { RasterizePolygonsGpTool } from './RasterizePolygonsGpTool';
import { ResampleRasterGpTool } from './ResampleRasterGpTool';
import { ReverseGpTool } from './ReverseGpTool';
import { RoundRasterGpTool } from './RoundRasterGpTool';
import { SmoothPolylineGpTool } from './SmoothPolylineGpTool';
import { SnapToHighGroundGpTool } from './SnapToHighGroundGpTool';

export const geoprocessingCatalog = [
  BufferPolylineGpTool, LineOffsetGpTool, FilterGpTool, ReverseGpTool, SmoothPolylineGpTool,
  DensifyPolylinesGpTool, DistanceToLinesGpTool, ProximityHeatmapGpTool, SnapToHighGroundGpTool,
  PolylineToPolygonGpTool, CullByLengthGpTool, MergePolylinesGpTool,
  BufferPolygonGpTool, DensifyPolygonsGpTool, RasterizePolygonsGpTool, PolygonToPolylineGpTool,
  LaplacianTerrainGpTool, ClipRasterToPolygonGpTool, RasterCutStatsGpTool,
  ResampleRasterGpTool, RoundRasterGpTool, NoDataLaplacianFillGpTool, FlatAreaDetectorGpTool,
] as const;

export type GeoprocessingToolId = (typeof geoprocessingCatalog)[number]['id'];
