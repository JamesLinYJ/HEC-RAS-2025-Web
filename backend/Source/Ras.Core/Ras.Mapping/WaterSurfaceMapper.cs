using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Ras.Layers.Results;
using Ras.Output;
using Utility;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.Unsafe;

namespace Ras.Mapping;

public class WaterSurfaceMapper : Mapper
{
	private static readonly ILogger _logger = LogHelper.GetLogger("WaterSurfaceMapper");

	public override MapTypes MapType { get; } = MapTypes.WaterSurfaceElevation;

	public WaterSurfaceMapper(IHorizontalOutputBlock ob, MeshPropertyTables[] tables, MapperCache cache = null)
		: base(ob, tables, cache)
	{
	}

	public void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, out float[] result)
	{
		result = new float[mps.InputCount];
		result.Fill(-9999f);
		Compute(profile, mps, clipTerrain, result);
	}

	public override void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> waterSurfaceResults)
	{
		if (mps.InputCount < 0)
		{
			throw new ArgumentException("mps");
		}
		if (waterSurfaceResults.Length != mps.InputCount)
		{
			throw new ArgumentException("waterSurfaceResults");
		}
		ValidateProfileIndex(profile);
		waterSurfaceResults.Span.Fill(-9999f);
		if (mps.MappedPixelCount != 0)
		{
			ComputeWaterSurfaceInternal2D(mps, profile, waterSurfaceResults);
			if (!clipTerrain.IsEmpty)
			{
				Buffers.ClipToFloor(waterSurfaceResults.Span, clipTerrain.Span, RenderOptions.MinWSPlotTolerance, -9999f);
			}
		}
	}

	protected void ComputeWaterSurfaceInternal2D(GeometryMapPoints mps, int profileIdx, Memory<float> outWaterSurfaces)
	{
		_ = _geometry.FlowAreaLayer;
		if (mps.FlatMapped2DAreas != null)
		{
			foreach (FlatMeshMap flatMapped2DArea in mps.FlatMapped2DAreas)
			{
				if (flatMapped2DArea.Cells.Count == 0)
				{
					continue;
				}
				using ImOnLoan<WaterSurfaceMeshInputs> imOnLoan = _mapperCache.Get2DWaterSurfaceInputs(flatMapped2DArea.MeshIndex, profileIdx);
				if (imOnLoan == null || imOnLoan.Item == null)
				{
					throw new Exception("Error getting water surface values.");
				}
				MeshClip meshClip = new MeshClip
				{
					CellWaterSurfaces = imOnLoan.Item.CellWaterSurfaces
				};
				flatMapped2DArea.ComputeCellValuesFlat(meshClip.CellWaterSurfaces, outWaterSurfaces, meshClip, RenderOptions);
			}
		}
		if (mps.SlopingMapped2DAreas == null)
		{
			return;
		}
		foreach (SlopingMeshMap slopingMapped2DArea in mps.SlopingMapped2DAreas)
		{
			if (slopingMapped2DArea.Cells.Count == 0)
			{
				continue;
			}
			if (RenderOptions.CellRenderMode == CellRenderMethod.AsComputed)
			{
				throw new NotImplementedException();
			}
			if (RenderOptions.CellRenderMode == CellRenderMethod.Pretty)
			{
				using (ImOnLoan<WaterSurfaceMeshInputs> imOnLoan2 = _mapperCache.Get2DWaterSurfaceInputs(slopingMapped2DArea.MeshIndex, profileIdx))
				{
					if (imOnLoan2 == null || imOnLoan2.Item == null)
					{
						throw new Exception("Error getting water surface values.");
					}
					slopingMapped2DArea.Render2D_NodeDonatedWeights_WaterSurface(imOnLoan2.Item, outWaterSurfaces, RenderOptions);
				}
				continue;
			}
			throw new NotImplementedException();
		}
	}

	public static float[] ComputeTimeSeries(PostProcessor pp, Point pt, float terrainElevation, RenderOptions renderOptions = null)
	{
		float[] timeSeries = new float[pp.ProfileCount];
		ComputeTimeSeries(output: new Memory<float>[1] { timeSeries }, pp: pp, mapPts: new GeometryMapPoints(pp.Geometry, pt), terrain: new float[1] { terrainElevation }, renderOptions: renderOptions);
		return timeSeries;
	}

	public static void ComputeTimeSeries(PostProcessor pp, GeometryMapPoints mapPts, Memory<float> terrain, Memory<float>[] output, RenderOptions renderOptions = null)
	{
		ComputeTimeSeries(pp, mapPts, terrain, 0, pp.ProfileCount, output, renderOptions);
	}

	public static void ComputeTimeSeries(PostProcessor pp, GeometryMapPoints mapPoints, Memory<float> terrain, int bandStart, int bandCount, Memory<float>[] output, RenderOptions renderOptions = null)
	{
		if (mapPoints == null || mapPoints.InputCount < 0)
		{
			throw new ArgumentException("mapPoints");
		}
		if (output == null || output.Length != mapPoints.InputCount)
		{
			throw new ArgumentException("output must be non-null and match the size of mapPoints.InputCount");
		}
		if (bandStart != 0 || bandCount != pp.ProfileCount)
		{
			throw new ArgumentException("ComputeTimeSeries currently only supports querying the full profile count.");
		}
		if (terrain.IsEmpty || terrain.Length != mapPoints.InputCount)
		{
			throw new ArgumentException("terrain incorrect size, expected " + mapPoints.InputCount);
		}
		if (renderOptions == null)
		{
			renderOptions = new RenderOptions();
		}
		for (int i = 0; i < output.Length; i++)
		{
			output[i].Span.Fill(-9999f);
		}
		ComputeWaterSurfaceTimeSeriesInternal2D(pp, mapPoints, output, renderOptions);
		Span<float> terrainSpan = terrain.Span;
		for (int j = 0; j < output.Length; j++)
		{
			Span<float> outputSpan = output[j].Span;
			float clipValue = terrainSpan[j] + renderOptions.MinWSPlotTolerance;
			Buffers.ClipToFloor(outputSpan, clipValue, -9999f);
		}
	}

	protected static void ComputeWaterSurfaceTimeSeriesInternal2D(PostProcessor pp, GeometryMapPoints mps, Memory<float>[] outWaterSurfaces, RenderOptions renderOptions)
	{
		_ = mps.Geometry.FlowAreaLayer;
		if (mps.FlatMapped2DAreas != null)
		{
			foreach (FlatMeshMap flatMapped2DArea in mps.FlatMapped2DAreas)
			{
				if (flatMapped2DArea.Cells.Count != 0)
				{
					throw new NotSupportedException("ComputeWaterSurfaceTimeSeriesInternal2D doesn't support flat-mapped meshes yet.");
				}
			}
		}
		if (mps.SlopingMapped2DAreas == null)
		{
			return;
		}
		foreach (SlopingMeshMap slopingMapped2DArea in mps.SlopingMapped2DAreas)
		{
			if (slopingMapped2DArea.Cells.Count != 0)
			{
				if (renderOptions.CellRenderMode == CellRenderMethod.AsComputed)
				{
					throw new NotImplementedException();
				}
				if (renderOptions.CellRenderMode != CellRenderMethod.Pretty)
				{
					throw new NotImplementedException();
				}
				WaterSurfaceMeshTimeSeriesInputs inputs = BuildWaterSurfaceTimeSeriesCache(slopingMapped2DArea, pp);
				slopingMapped2DArea.Render2D_NodeDonatedWeights_WaterSurface(inputs, outWaterSurfaces, renderOptions);
			}
		}
	}

	public static WaterSurfaceMeshTimeSeriesInputs BuildWaterSurfaceTimeSeriesCache(SlopingMeshMap map, PostProcessor pp)
	{
		WaterSurfaceMeshTimeSeriesInputs waterSurfaceMeshTimeSeriesInputs = new WaterSurfaceMeshTimeSeriesInputs(map.Mesh, pp.Geometry.FlowAreaLayer[map.MeshIndex].PropertyTables, pp.ProfileCount);
		Memory<int> listBackingBuffer = UnsafeHelpers.GetListBackingBuffer(map.UniqueCells());
		waterSurfaceMeshTimeSeriesInputs.Add(listBackingBuffer, (Memory<int> cIdxs, Memory<float>[] buffer) =>
		{
			pp.GetCellWaterSurfaceTimeSeries(pp.MappedOutputBlock, cIdxs, buffer);
		});
		return waterSurfaceMeshTimeSeriesInputs;
	}
}
