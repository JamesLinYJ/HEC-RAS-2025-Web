using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using H5Assist.VB;
using RasMapperLib.Mapping;
using Utility;

namespace RasMapperLib.Render;

public class FractionInundatedRenderer : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D0
	{
		public float[] usxS_WaterSurfaceTimeSeries;

		public float[] dsxS_WaterSurfaceTimeSeries;

		public LeveeProperties usLevee;

		public LeveeProperties dsLevee;

		public int nTimesteps;

		public Func<XSISMapPoint, int, bool> isPixelWet;

		public _Closure_0024__5_002D1 outerClosure;

		public _Closure_0024__5_002D0(_Closure_0024__5_002D0 other)
		{
			if (other != null)
			{
				usxS_WaterSurfaceTimeSeries = other.usxS_WaterSurfaceTimeSeries;
				dsxS_WaterSurfaceTimeSeries = other.dsxS_WaterSurfaceTimeSeries;
				usLevee = other.usLevee;
				dsLevee = other.dsLevee;
				nTimesteps = other.nTimesteps;
				isPixelWet = other.isPixelWet;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XSISMapPoint pixel, int time)
		{
			float usxS_WaterSurfaceTimeSery = usxS_WaterSurfaceTimeSeries[time];
			float dsxS_WaterSurfaceTimeSery = dsxS_WaterSurfaceTimeSeries[time];
			bool condition = false;
			bool condition2 = false;
			if (usLevee.LeftExists && dsLevee.LeftExists && usLevee.LeftElevation > usxS_WaterSurfaceTimeSery && dsLevee.LeftElevation > dsxS_WaterSurfaceTimeSery)
			{
				condition = true;
			}
			if (usLevee.RightExists && dsLevee.RightExists && usLevee.RightElevation > usxS_WaterSurfaceTimeSery && dsLevee.RightElevation > dsxS_WaterSurfaceTimeSery)
			{
				condition2 = true;
			}
			float pixelTerm = 1f - pixel.Z;
			float pixelTerm2 = usxS_WaterSurfaceTimeSery * pixel.Z + dsxS_WaterSurfaceTimeSery * pixelTerm;
			if (condition)
			{
				double leftMPixelTerm = usLevee.LeftM * pixel.Z + dsLevee.LeftM * pixelTerm;
				if ((double)pixel.M < leftMPixelTerm)
				{
					return false;
				}
			}
			if (condition2)
			{
				double rightMPixelTerm = usLevee.RightM * pixel.Z + dsLevee.RightM * pixelTerm;
				if ((double)pixel.M > rightMPixelTerm)
				{
					return false;
				}
			}
			if (pixelTerm2 > outerClosure.terrain[pixel.Index] + outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}

		[SpecialName]
		internal void _Lambda_0024__1(XSISMapPoint pixel)
		{
			if (outerClosure.terrain[pixel.Index] == -9999f)
			{
				return;
			}
			int ordinal = 0;
			int arrivalStartIndex = outerClosure.arrivalStartIndex;
			checked
			{
				int count = nTimesteps - 1;
				for (int i = arrivalStartIndex; i <= count; i++)
				{
					if (isPixelWet(pixel, i))
					{
						ordinal++;
					}
				}
				if (ordinal != 0)
				{
					outerClosure.fracInundated[pixel.Index] = (float)((double)(100 * ordinal) / (double)(nTimesteps - outerClosure.arrivalStartIndex));
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D1
	{
		public float[] terrain;

		public float depthThreshold;

		public int arrivalStartIndex;

		public float[] fracInundated;

		public _Closure_0024__5_002D1(_Closure_0024__5_002D1 other)
		{
			if (other != null)
			{
				terrain = other.terrain;
				depthThreshold = other.depthThreshold;
				arrivalStartIndex = other.arrivalStartIndex;
				fracInundated = other.fracInundated;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public float[] cellTimeSeries;

		public float cellMinElev;

		public _Closure_0024__7_002D1 outerClosure;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				cellTimeSeries = other.cellTimeSeries;
				cellMinElev = other.cellMinElev;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(int pixelIdx, int time)
		{
			float cellTimeSery = cellTimeSeries[time];
			if (cellTimeSery > cellMinElev + outerClosure.depthThreshold && cellTimeSery - outerClosure.terrain[pixelIdx] > outerClosure.depthThreshold)
			{
				return true;
			}
			return false;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D1
	{
		public float depthThreshold;

		public float[] terrain;

		public _Closure_0024__7_002D1(_Closure_0024__7_002D1 other)
		{
			if (other != null)
			{
				depthThreshold = other.depthThreshold;
				terrain = other.terrain;
			}
		}
	}

	private Names _wsNames;

	private H5ColumnCache<float> _colCache;

	public static void Compute(RASResults res, RASGeometryMapPoints mps, float[] terrain, double startSimTime, float depthThreshold, ref float[] fracInundated, CacheCollection cache = null)
	{
		new FractionInundatedRenderer(res, cache).Compute(mps, terrain, startSimTime, depthThreshold, ref fracInundated);
	}

	public FractionInundatedRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: false, needsPostProcessFile: true)
	{
		_wsNames = GetWaterSurfaceDatasetNames();
		_wsNames.Validate(_postProcessFilename);
		_colCache = new H5ColumnCache<float>(_postProcessFilename);
	}

	public void Compute(RASGeometryMapPoints mps, float[] terrain, double startSimTime, float depthThreshold, ref float[] fracInundated)
	{
		Buffers.ArraySetAllVB(ref fracInundated, -9999f, mps.InputCount);
		float minWSPlotTolerance = MinWSPlotTolerance;
		if (!float.IsNaN(depthThreshold) && depthThreshold > minWSPlotTolerance)
		{
			minWSPlotTolerance = depthThreshold;
		}
		string dataset = null;
		string dataset2 = null;
		List<string> datasets = null;
		Names wsNames = _wsNames;
		int row = 0;
		wsNames.Get1D(0, ref dataset, ref row);
		Names wsNames2 = _wsNames;
		row = 0;
		wsNames2.GetSA(0, ref dataset2, ref row);
		Names wsNames3 = _wsNames;
		row = 0;
		wsNames3.Get2D(0, ref datasets, ref row);
		int binarySearch = Array.BinarySearch(_result.ProfileSimulationTimes, startSimTime);
		if (binarySearch < 0)
		{
			binarySearch = ~binarySearch;
		}
		if (binarySearch == _result.ProfileSimulationTimes.Count())
		{
			return;
		}
		if (dataset != null && mps.MappedXSISs.Count > 0)
		{
			ComputeFractionInundated1D(dataset, mps, terrain, binarySearch, minWSPlotTolerance, _colCache, fracInundated);
		}
		if (dataset2 != null && mps.MappedSAs.Count > 0)
		{
			float[,] values = null;
			using (H5Reader h5Reader = new H5Reader(_postProcessFilename))
			{
				h5Reader.ReadDataset(dataset2, ref values);
			}
			ComputeFractionInundatedSA(mps, values, terrain, binarySearch, minWSPlotTolerance, fracInundated);
		}
		if (datasets != null && datasets.Count > 0 && mps.AllMappedMeshIndexes().Count() > 0)
		{
			ComputeFractionInundated2D(datasets, mps, terrain, binarySearch, minWSPlotTolerance, _colCache, fracInundated);
		}
	}

	private void ComputeFractionInundated1D(string crossSectionDataset, RASGeometryMapPoints mps, float[] terrain, int arrivalStartIndex, float depthThreshold, H5ColumnCache<float> cache, float[] fracInundated)
	{
		_Closure_0024__5_002D1 capturedLocals = new _Closure_0024__5_002D1(null);
		capturedLocals.terrain = terrain;
		capturedLocals.arrivalStartIndex = arrivalStartIndex;
		capturedLocals.depthThreshold = depthThreshold;
		capturedLocals.fracInundated = fracInundated;
		if (!cache.Datasets.Contains(crossSectionDataset))
		{
			cache.EnsureDataset(crossSectionDataset);
		}
		int index = cache.GetIndex(crossSectionDataset);
		_Closure_0024__5_002D0 capturedLocals2 = default;
		foreach (MappedXSIS mappedXSIS in mps.MappedXSISs)
		{
			capturedLocals2 = new _Closure_0024__5_002D0(capturedLocals2);
			capturedLocals2.outerClosure = capturedLocals;
			int iD = mappedXSIS.ID;
			int upstreamXSID = _XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = _XSIS.GetDownstreamXSID(iD);
			capturedLocals2.usLevee = _XS.GetLeveeProperties(upstreamXSID);
			capturedLocals2.dsLevee = _XS.GetLeveeProperties(downstreamXSID);
			capturedLocals2.usxS_WaterSurfaceTimeSeries = cache.Item(index, upstreamXSID);
			capturedLocals2.dsxS_WaterSurfaceTimeSeries = cache.Item(index, downstreamXSID);
			capturedLocals2.nTimesteps = capturedLocals2.usxS_WaterSurfaceTimeSeries.Count();
			capturedLocals2.isPixelWet = capturedLocals2._Lambda_0024__0;
			System.Threading.Tasks.Parallel.ForEach(mappedXSIS.MapPoints, capturedLocals2._Lambda_0024__1);
		}
	}

	private void ComputeFractionInundatedSA(RASGeometryMapPoints mps, float[,] storageAreaWaterSurfaces, float[] terrain, int arrivalStartIndex, float depthThreshold, float[] fracInundated)
	{
		int length = storageAreaWaterSurfaces.GetLength(0);
		RASStorageArea storageArea = _geometry.StorageArea;
		checked
		{
			foreach (SAMap mappedSA in mps.MappedSAs)
			{
				float minElevationStorageAreaTerm = storageArea.MinElevation(mappedSA.ID) + depthThreshold;
				foreach (int mapPoint in mappedSA.MapPoints)
				{
					if (terrain[mapPoint] == -9999f)
					{
						continue;
					}
					int ordinal = 0;
					int count = length - 1;
					for (int i = arrivalStartIndex; i <= count; i++)
					{
						float storageAreaWaterSurfacesIdTerm = storageAreaWaterSurfaces[i, mappedSA.ID];
						if (storageAreaWaterSurfacesIdTerm > minElevationStorageAreaTerm && (double)(storageAreaWaterSurfacesIdTerm - terrain[mapPoint] - depthThreshold) > 0.0)
						{
							ordinal++;
						}
					}
					if (ordinal != 0)
					{
						fracInundated[mapPoint] = (float)((double)(100 * ordinal) / (double)(length - arrivalStartIndex));
					}
				}
			}
		}
	}

	private void ComputeFractionInundated2D(List<string> twoD_Datasets, RASGeometryMapPoints mps, float[] terrain, int arrivalStartIndex, float depthThreshold, H5ColumnCache<float> cache, float[] fracInundated)
	{
		_Closure_0024__7_002D1 capturedLocals = new _Closure_0024__7_002D1(null);
		capturedLocals.terrain = terrain;
		capturedLocals.depthThreshold = depthThreshold;
		checked
		{
			_Closure_0024__7_002D0 capturedLocals2 = default;
			foreach (int allMappedMeshIndex in mps.AllMappedMeshIndexes())
			{
				MeshFV2D meshFV2D = (MeshFV2D)_geometry.D2FlowArea.Feature(allMappedMeshIndex);
				string twoD_Dataset = twoD_Datasets[allMappedMeshIndex];
				if (!cache.Datasets.Contains(twoD_Dataset))
				{
					cache.EnsureDataset(twoD_Dataset);
				}
				int index = cache.GetIndex(twoD_Dataset);
				foreach (FlatCellMap allMappedCell in mps.AllMappedCells(allMappedMeshIndex))
				{
					capturedLocals2 = new _Closure_0024__7_002D0(capturedLocals2);
					capturedLocals2.outerClosure = capturedLocals;
					int index2 = allMappedCell.Index;
					capturedLocals2.cellTimeSeries = cache.Item(index, index2);
					int capturedLocals3 = capturedLocals2.cellTimeSeries.Count();
					capturedLocals2.cellMinElev = meshFV2D.Cell(index2).MinimumElevation;
					Func<int, int, bool> func = capturedLocals2._Lambda_0024__0;
					foreach (int mapPoint in allMappedCell.MapPoints)
					{
						if (capturedLocals2.outerClosure.terrain[mapPoint] == -9999f)
						{
							continue;
						}
						int ordinal = 0;
						int count = capturedLocals3 - 1;
						for (int i = arrivalStartIndex; i <= count; i++)
						{
							if (func(mapPoint, i))
							{
								ordinal++;
							}
						}
						if (ordinal != 0)
						{
							fracInundated[mapPoint] = (float)((double)(100 * ordinal) / (double)(capturedLocals3 - arrivalStartIndex));
						}
					}
				}
			}
		}
	}
}
