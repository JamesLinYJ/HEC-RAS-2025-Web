using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class WaterSurfaceTimeSeries : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D0
	{
		public Dictionary<int, float[]> xsWs;

		public _Closure_0024__4_002D1 outerClosure;

		public _Closure_0024__4_002D0(_Closure_0024__4_002D0 other)
		{
			if (other != null)
			{
				xsWs = other.xsWs;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			outerClosure.waterSurfaceTimeSeries.ComputeWaterSurfaceInternal1DTimeSeries(xsWs, outerClosure.mps, outerClosure.lWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D1
	{
		public RASGeometryMapPoints mps;

		public float[][] lWaterSurfaces;

		public int nTimesteps;

		public float[] clipTerrain;

		public WaterSurfaceTimeSeries waterSurfaceTimeSeries;

		public _Closure_0024__4_002D1(_Closure_0024__4_002D1 other)
		{
			if (other != null)
			{
				mps = other.mps;
				lWaterSurfaces = other.lWaterSurfaces;
				nTimesteps = other.nTimesteps;
				clipTerrain = other.clipTerrain;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D2
	{
		public Dictionary<int, float[]> saWs;

		public _Closure_0024__4_002D1 outerClosure;

		public _Closure_0024__4_002D2(_Closure_0024__4_002D2 other)
		{
			if (other != null)
			{
				saWs = other.saWs;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.waterSurfaceTimeSeries.ComputeWaterSurfaceInternalSATimeSeries(saWs, outerClosure.mps, outerClosure.lWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D3
	{
		public Dictionary<int, Dictionary<int, float[]>> ws2D;

		public _Closure_0024__4_002D1 outerClosure;

		public _Closure_0024__4_002D3(_Closure_0024__4_002D3 other)
		{
			if (other != null)
			{
				ws2D = other.ws2D;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.waterSurfaceTimeSeries.ComputeWaterSurfaceInternal2DTimeSeries(ws2D, outerClosure.mps, outerClosure.lWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D4
	{
		public float[][] lWS;

		public _Closure_0024__4_002D1 outerClosure;

		public _Closure_0024__4_002D4(_Closure_0024__4_002D4 other)
		{
			if (other != null)
			{
				lWS = other.lWS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					float[] lWS = this.lWS[i];
					int count = outerClosure.nTimesteps - 1;
					for (int j = 0; j <= count; j++)
					{
						if (outerClosure.clipTerrain[i] == -9999f || lWS[j] < outerClosure.clipTerrain[i] + outerClosure.waterSurfaceTimeSeries.MinWSPlotTolerance)
						{
							lWS[j] = -9999f;
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__5_002D0
	{
		public Dictionary<int, float[]> xsWaterSurfaces;

		public int nTimesteps;

		public float[][] waterSurfaces;

		public WaterSurfaceTimeSeries waterSurfaceTimeSeries;

		public _Closure_0024__5_002D0(_Closure_0024__5_002D0 other)
		{
			if (other != null)
			{
				xsWaterSurfaces = other.xsWaterSurfaces;
				nTimesteps = other.nTimesteps;
				waterSurfaces = other.waterSurfaces;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			int iD = interpSurface.ID;
			int upstreamXSID = waterSurfaceTimeSeries._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = waterSurfaceTimeSeries._XSIS.GetDownstreamXSID(iD);
			float[] xsWaterSurface = xsWaterSurfaces[upstreamXSID];
			float[] xsWaterSurfaceArray = xsWaterSurfaces[downstreamXSID];
			checked
			{
				int count = nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					float xsWaterSurface2 = xsWaterSurface[i];
					float xsWaterSurface3 = xsWaterSurfaceArray[i];
					LeveeProperties leveeProperties = waterSurfaceTimeSeries._XS.GetLeveeProperties(upstreamXSID);
					LeveeProperties leveeProperties2 = waterSurfaceTimeSeries._XS.GetLeveeProperties(downstreamXSID);
					bool condition = false;
					if (leveeProperties.LeftExists && leveeProperties2.LeftExists && leveeProperties.LeftElevation > xsWaterSurface2 && leveeProperties2.LeftElevation > xsWaterSurface3)
					{
						condition = true;
					}
					bool condition2 = false;
					if (leveeProperties.RightExists && leveeProperties2.RightExists && leveeProperties.RightElevation > xsWaterSurface2 && leveeProperties2.RightElevation > xsWaterSurface3)
					{
						condition2 = true;
					}
					foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
					{
						float mapPointTerm = 1f - mapPoint.Z;
						float mapPointTerm2 = xsWaterSurface2 * mapPoint.Z + xsWaterSurface3 * mapPointTerm;
						if (condition)
						{
							double leftMLeveePropertiesTerm = leveeProperties.LeftM * mapPoint.Z + leveeProperties2.LeftM * mapPointTerm;
							if ((double)mapPoint.M < leftMLeveePropertiesTerm)
							{
								continue;
							}
						}
						if (condition2)
						{
							double rightMLeveePropertiesTerm = leveeProperties.RightM * mapPoint.Z + leveeProperties2.RightM * mapPointTerm;
							if ((double)mapPoint.M > rightMLeveePropertiesTerm)
							{
								continue;
							}
						}
						waterSurfaces[mapPoint.Index][i] = mapPointTerm2;
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public Dictionary<int, float[]> cellWSELLookups;

		public MeshFV2D mesh;

		public _Closure_0024__7_002D1 outerClosure;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				cellWSELLookups = other.cellWSELLookups;
				mesh = other.mesh;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(FlatCellMap mapCell)
		{
			float[] cellWSELLookup = cellWSELLookups[mapCell.Index];
			checked
			{
				int count = outerClosure.nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					float cellWSELLookup2 = cellWSELLookup[i];
					if (!(cellWSELLookup2 > mesh.Cell(mapCell.Index).MinimumElevation))
					{
						continue;
					}
					foreach (int mapPoint in mapCell.MapPoints)
					{
						outerClosure.waterSurfaces[mapPoint][i] = cellWSELLookup2;
					}
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(SlopingCellMap mapCell)
		{
			float[] cellWSELLookup = cellWSELLookups[mapCell.Index];
			checked
			{
				int count = outerClosure.nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					float cellWSELLookup2 = cellWSELLookup[i];
					if (!(cellWSELLookup2 > mesh.Cell(mapCell.Index).MinimumElevation))
					{
						continue;
					}
					foreach (int ordinal in mapCell.MapPoints.Select([SpecialName] (SlopingCellPoint pa) => pa.Index))
					{
						outerClosure.waterSurfaces[ordinal][i] = cellWSELLookup2;
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D1
	{
		public int nTimesteps;

		public float[][] waterSurfaces;

		public _Closure_0024__7_002D1(_Closure_0024__7_002D1 other)
		{
			if (other != null)
			{
				nTimesteps = other.nTimesteps;
				waterSurfaces = other.waterSurfaces;
			}
		}
	}

	private Names _wsNames;

	private H5ColumnCache<float> _colCache;

	public static void Compute(RASResults res, RASGeometryMapPoints mps, ref float[][] waterSurfaces, CacheCollection cache = null, float[] clipTerrain = null)
	{
		new WaterSurfaceTimeSeries(res, cache).Compute(mps, ref waterSurfaces, clipTerrain);
	}

	public WaterSurfaceTimeSeries(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: false, needsPostProcessFile: true)
	{
		_wsNames = GetWaterSurfaceDatasetNames();
		_wsNames.Validate(_postProcessFilename);
		_colCache = _cache.GetColumnCache<float>(_postProcessFilename);
	}

	public void Compute(RASGeometryMapPoints mps, ref float[][] waterSurfaces, float[] clipTerrain = null)
	{
		_Closure_0024__4_002D1 capturedLocals = new _Closure_0024__4_002D1(null);
		capturedLocals.waterSurfaceTimeSeries = this;
		capturedLocals.mps = mps;
		capturedLocals.clipTerrain = clipTerrain;
		TimeSeriesInitialize(capturedLocals.mps, -9999f, ref waterSurfaces);
		capturedLocals.nTimesteps = waterSurfaces[0].Length;
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
		List<Task> tasks = new List<Task>();
		capturedLocals.lWaterSurfaces = waterSurfaces;
		if (!string.IsNullOrEmpty(dataset) && capturedLocals.mps.MappedXSISs.Count > 0)
		{
			_Closure_0024__4_002D0 capturedLocals2 = default;
			_Closure_0024__4_002D0 capturedLocals3 = new _Closure_0024__4_002D0(capturedLocals2);
			capturedLocals3.outerClosure = capturedLocals;
			capturedLocals3.xsWs = ReadMappedXSTimeseries(capturedLocals3.outerClosure.mps, dataset, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals3.outerClosure.waterSurfaceTimeSeries.ComputeWaterSurfaceInternal1DTimeSeries(capturedLocals3.xsWs, capturedLocals3.outerClosure.mps, capturedLocals3.outerClosure.lWaterSurfaces);
			}));
		}
		if (!string.IsNullOrEmpty(dataset2) && capturedLocals.mps.MappedSAs.Count > 0)
		{
			_Closure_0024__4_002D2 capturedLocals4 = default;
			_Closure_0024__4_002D2 capturedLocals5 = new _Closure_0024__4_002D2(capturedLocals4);
			capturedLocals5.outerClosure = capturedLocals;
			capturedLocals5.saWs = ReadMappedSATimeseries(capturedLocals5.outerClosure.mps, dataset2, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals5.outerClosure.waterSurfaceTimeSeries.ComputeWaterSurfaceInternalSATimeSeries(capturedLocals5.saWs, capturedLocals5.outerClosure.mps, capturedLocals5.outerClosure.lWaterSurfaces);
			}));
		}
		if (!datasets.IsNullOrEmpty() && capturedLocals.mps.HasMapped2DCells())
		{
			_Closure_0024__4_002D3 capturedLocals6 = default;
			_Closure_0024__4_002D3 capturedLocals7 = new _Closure_0024__4_002D3(capturedLocals6);
			capturedLocals7.outerClosure = capturedLocals;
			capturedLocals7.ws2D = ReadMappedAndConnected2DCellTimeseries(capturedLocals7.outerClosure.mps, datasets, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals7.outerClosure.waterSurfaceTimeSeries.ComputeWaterSurfaceInternal2DTimeSeries(capturedLocals7.ws2D, capturedLocals7.outerClosure.mps, capturedLocals7.outerClosure.lWaterSurfaces);
			}));
		}
		Task.WaitAll(tasks.ToArray());
		if (capturedLocals.clipTerrain == null)
		{
			return;
		}
		_Closure_0024__4_002D4 capturedLocals8 = default;
		_Closure_0024__4_002D4 capturedLocals9 = new _Closure_0024__4_002D4(capturedLocals8);
		capturedLocals9.outerClosure = capturedLocals;
		capturedLocals9.lWS = waterSurfaces;
		Utility.Parallel.SmartFor(waterSurfaces.Count(), checked([SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				float[] lWS = capturedLocals9.lWS[i];
				int count = capturedLocals9.outerClosure.nTimesteps - 1;
				for (int j = 0; j <= count; j++)
				{
					if (capturedLocals9.outerClosure.clipTerrain[i] == -9999f || lWS[j] < capturedLocals9.outerClosure.clipTerrain[i] + capturedLocals9.outerClosure.waterSurfaceTimeSeries.MinWSPlotTolerance)
					{
						lWS[j] = -9999f;
					}
				}
			}
		}));
	}

	private void ComputeWaterSurfaceInternal1DTimeSeries(Dictionary<int, float[]> xsWaterSurfaces, RASGeometryMapPoints mps, float[][] waterSurfaces)
	{
		_Closure_0024__5_002D0 capturedLocals = default;
		_Closure_0024__5_002D0 capturedLocals2 = new _Closure_0024__5_002D0(capturedLocals);
		capturedLocals2.waterSurfaceTimeSeries = this;
		capturedLocals2.xsWaterSurfaces = xsWaterSurfaces;
		capturedLocals2.waterSurfaces = waterSurfaces;
		capturedLocals2.nTimesteps = capturedLocals2.waterSurfaces[0].Length;
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, checked([SpecialName] (MappedXSIS interpSurface) =>
		{
			int iD = interpSurface.ID;
			int upstreamXSID = capturedLocals2.waterSurfaceTimeSeries._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = capturedLocals2.waterSurfaceTimeSeries._XSIS.GetDownstreamXSID(iD);
			float[] xsWaterSurface = capturedLocals2.xsWaterSurfaces[upstreamXSID];
			float[] xsWaterSurfaceArray = capturedLocals2.xsWaterSurfaces[downstreamXSID];
			int count = capturedLocals2.nTimesteps - 1;
			for (int i = 0; i <= count; i++)
			{
				float computed = xsWaterSurface[i];
				float computed2 = xsWaterSurfaceArray[i];
				LeveeProperties leveeProperties = capturedLocals2.waterSurfaceTimeSeries._XS.GetLeveeProperties(upstreamXSID);
				LeveeProperties leveeProperties2 = capturedLocals2.waterSurfaceTimeSeries._XS.GetLeveeProperties(downstreamXSID);
				bool condition = false;
				if (leveeProperties.LeftExists && leveeProperties2.LeftExists && leveeProperties.LeftElevation > computed && leveeProperties2.LeftElevation > computed2)
				{
					condition = true;
				}
				bool condition2 = false;
				if (leveeProperties.RightExists && leveeProperties2.RightExists && leveeProperties.RightElevation > computed && leveeProperties2.RightElevation > computed2)
				{
					condition2 = true;
				}
				foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
				{
					float mapPointTerm = 1f - mapPoint.Z;
					float mapPointTerm2 = computed * mapPoint.Z + computed2 * mapPointTerm;
					if (condition)
					{
						double leftMLeveePropertiesTerm = leveeProperties.LeftM * mapPoint.Z + leveeProperties2.LeftM * mapPointTerm;
						if ((double)mapPoint.M < leftMLeveePropertiesTerm)
						{
							continue;
						}
					}
					if (condition2)
					{
						double rightMLeveePropertiesTerm = leveeProperties.RightM * mapPoint.Z + leveeProperties2.RightM * mapPointTerm;
						if ((double)mapPoint.M > rightMLeveePropertiesTerm)
						{
							continue;
						}
					}
					capturedLocals2.waterSurfaces[mapPoint.Index][i] = mapPointTerm2;
				}
			}
		}));
	}

	private void ComputeWaterSurfaceInternalSATimeSeries(Dictionary<int, float[]> saWaterSurfaces, RASGeometryMapPoints mps, float[][] waterSurfaces)
	{
		_ = saWaterSurfaces.ElementAt(0).Value.Length;
		foreach (SAMap mappedSA in mps.MappedSAs)
		{
			float[] saWaterSurface = saWaterSurfaces[mappedSA.ID];
			foreach (int mapPoint in mappedSA.MapPoints)
			{
				Buffer.BlockCopy(saWaterSurface, 0, waterSurfaces[mapPoint], 0, checked(4 * saWaterSurface.Count()));
			}
		}
	}

	private void ComputeWaterSurfaceInternal2DTimeSeries(Dictionary<int, Dictionary<int, float[]>> meshCellWaterSurfaces, RASGeometryMapPoints mps, float[][] waterSurfaces)
	{
		_Closure_0024__7_002D1 capturedLocals = new _Closure_0024__7_002D1(null);
		capturedLocals.waterSurfaces = waterSurfaces;
		capturedLocals.nTimesteps = capturedLocals.waterSurfaces[0].Length;
		checked
		{
			_Closure_0024__7_002D0 capturedLocals2 = default;
			foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
			{
				capturedLocals2 = new _Closure_0024__7_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				int key = joinedMappedMeshe.Key;
				FlatMeshMap flatMeshMap = joinedMappedMeshe.Value.Item1;
				SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
				capturedLocals2.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(key);
				capturedLocals2.cellWSELLookups = meshCellWaterSurfaces[key];
				if (flatMeshMap != null && flatMeshMap.Cells.Count > 0)
				{
					System.Threading.Tasks.Parallel.ForEach(flatMeshMap.Cells, capturedLocals2._Lambda_0024__0);
				}
				if (slopingMeshMap == null || slopingMeshMap.Cells.Count <= 0)
				{
					continue;
				}
				if (SharedData.CellRenderMode == SharedData.CellRenderMethod.AsComputed)
				{
					System.Threading.Tasks.Parallel.ForEach(slopingMeshMap.Cells, capturedLocals2._Lambda_0024__1);
					continue;
				}
				float[] floatValues = new float[capturedLocals2.mesh.CellCount - 1 + 1];
				FaceValues[] faceWS = new FaceValues[capturedLocals2.mesh.FaceCount - 1 + 1];
				float[][] facePointValues = new float[capturedLocals2.mesh.FacePointCount - 1 + 1][];
				float[] floatValuesArray = new float[capturedLocals2.outerClosure.waterSurfaces.Length - 1 + 1];
				int count = capturedLocals2.outerClosure.nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					foreach (KeyValuePair<int, float[]> localValue in capturedLocals2.cellWSELLookups)
					{
						floatValues[localValue.Key] = localValue.Value[i];
					}
					Buffers.ArraySetAllVB(floatValuesArray, -9999f);
					SlopingFactors.ComputeSlopingWSFPValues(_d2FlowArea, slopingMeshMap, floatValues, ref faceWS, ref facePointValues);
					ComputeFromFacepointValues_SpecialWaterSurfaceMode(slopingMeshMap, floatValues, faceWS, facePointValues, floatValuesArray);
					foreach (SlopingCellMap cell in slopingMeshMap.Cells)
					{
						foreach (SlopingCellPoint mapPoint in cell.MapPoints)
						{
							capturedLocals2.outerClosure.waterSurfaces[mapPoint.Index][i] = floatValuesArray[mapPoint.Index];
						}
					}
				}
			}
		}
	}

	public Dictionary<int, float[]> Read2DCellTimeseries(int meshIdx, List<int> cells)
	{
		List<string> datasets = null;
		Names wsNames = _wsNames;
		int row = 0;
		wsNames.Get2D(0, ref datasets, ref row);
		if (datasets.IsNullOrEmpty())
		{
			return null;
		}
		return Read2DColumns(cells, datasets[meshIdx], _colCache);
	}

	public float[] Read2DCellTimeseries(int meshIdx, int cell)
	{
		return Read2DCellTimeseries(meshIdx, new int[1] { cell }.ToList())[cell];
	}
}
