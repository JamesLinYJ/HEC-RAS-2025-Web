using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Rasters;

namespace Geospatial.Vectors;

public class SurfaceQuery
{
	public struct Layer(int elementIdx, int pyramidLvl, int tileIdx, SpatialCoverage cov)
	{
		public FilePyramidTileIndex Element = new FilePyramidTileIndex(elementIdx, pyramidLvl, tileIdx);

		public SpatialCoverage Coverage = cov;
	}

	private struct TinWrapper(Tin tin, Extent ext, int priority)
	{
		public Tin Tin = tin;

		public int Priority = priority;

		public Extent Extent = ext;
	}

	private struct RasterWrapper
	{
		public IList<RasterDefinition> PyramidTileDefs;

		public Func<PyramidTileIndex, SpatialCoverage> TileDataCoverage;

		public int Priority;

		public Extent Extent;

		public RasterWrapper(IList<RasterDefinition> pyramidTileDefs, Func<PyramidTileIndex, SpatialCoverage> tileDataCoverage, int priority)
		{
			PyramidTileDefs = pyramidTileDefs;
			Extent = PyramidTileDefs.First().Extent;
			TileDataCoverage = tileDataCoverage;
			Priority = priority;
		}
	}

	private SpatialIndex<Layer> _spIdx;

	private readonly List<TinWrapper> _tins = new List<TinWrapper>();

	private readonly List<RasterWrapper> _rasters = new List<RasterWrapper>();

	public SpatialIndex<Layer> SpatialIndex => _spIdx;

	private int Count => _tins.Count + _rasters.Count;

	public int TargetSpatialIndexBinCount()
	{
		return 4096;
	}

	public SurfaceQuery(Extent? seedExtent = null)
	{
		_ = seedExtent.HasValue;
	}

	public void Add(ComplexPolygon cpg)
	{
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory();
		delaunayTinFactory.AddPerimeter(cpg);
		Tin tin = delaunayTinFactory.ComputeTIN();
		Extent extent = cpg.Extent;
		_tins.Add(new TinWrapper(tin, extent, Count));
		Rebuild();
	}

	public void Add(Tin tin)
	{
		_tins.Add(new TinWrapper(tin, tin.Extent, Count));
		Rebuild();
	}

	public void Add<T>(IPyramidTileProvider<T> raster, Func<PyramidTileIndex, SpatialCoverage> tileDataCoverage) where T : unmanaged, IEquatable<T>
	{
		_rasters.Add(new RasterWrapper(raster.PyramidLevel.Select((ITileProvider<T> pl) => pl.TiledRasterDefinition).ToArray(), tileDataCoverage, Count));
		Rebuild();
	}

	private void Rebuild()
	{
		Extent a = Extent.Union(_tins.Select((TinWrapper t) => t.Extent));
		a = Extent.Union(a, Extent.Union(_rasters.Select((RasterWrapper r) => r.Extent))).RatioAroundCenter(1.01);
		_spIdx = new SpatialIndex<Layer>(a, TargetSpatialIndexBinCount());
		RasterDefinition raster = _spIdx.Raster;
		for (int intervalIndex = 0; intervalIndex < _tins.Count; intervalIndex++)
		{
			TinWrapper tinWrapper = _tins[intervalIndex];
			Extent extent = tinWrapper.Extent;
			Tin tin = tinWrapper.Tin;
			int priority = tinWrapper.Priority;
			PixelRange pixelRange = raster.GetPixelRange(extent);
			for (int minRow = pixelRange.MinRow; minRow <= pixelRange.MaxRow; minRow++)
			{
				for (int minCol = pixelRange.MinCol; minCol <= pixelRange.MaxCol; minCol++)
				{
					int cellIndex = raster.CellIndex(minRow, minCol);
					Extent ext = raster.CellExtent(minRow, minCol);
					SpatialCoverage spatialCoverage = tin.ExtentCoverage(ext);
					if (spatialCoverage != SpatialCoverage.NoCoverage)
					{
						_spIdx.Bins[cellIndex].Add(new Layer(priority, 0, 0, spatialCoverage));
					}
				}
			}
		}
		for (int intervalIndex2 = 0; intervalIndex2 < _rasters.Count; intervalIndex2++)
		{
			RasterWrapper rasterWrapper = _rasters[intervalIndex2];
			IList<RasterDefinition> pyramidTileDefs = rasterWrapper.PyramidTileDefs;
			for (int intervalIndex3 = 0; intervalIndex3 < pyramidTileDefs.Count; intervalIndex3++)
			{
				RasterDefinition rasterDefinition = pyramidTileDefs[intervalIndex3];
				for (int ordinal = 0; ordinal < rasterDefinition.CellCount; ordinal++)
				{
					Extent ext2 = rasterDefinition.CellExtent(ordinal);
					rasterWrapper.TileDataCoverage(new PyramidTileIndex(intervalIndex3, ordinal));
					_spIdx.Raster.GetPixelRange(ext2, PixelRangeOptions.CompletePixelCovered);
					_spIdx.Raster.GetPixelRange(ext2);
				}
			}
		}
	}
}
