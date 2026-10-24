using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class VelocityTimeSeries : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D0
	{
		public Dictionary<int, float[]> crossSectionFlows;

		public Dictionary<int, float[]> crossSectionWaterSurfaces;

		public int nTimesteps;

		public float[][] clipDepths;

		public float[][] velMag;

		public float[][] velX;

		public float[][] velY;

		public VelocityTimeSeries velocityTimeSeries;

		public _Closure_0024__8_002D0(_Closure_0024__8_002D0 other)
		{
			if (other != null)
			{
				crossSectionFlows = other.crossSectionFlows;
				crossSectionWaterSurfaces = other.crossSectionWaterSurfaces;
				nTimesteps = other.nTimesteps;
				clipDepths = other.clipDepths;
				velMag = other.velMag;
				velX = other.velX;
				velY = other.velY;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			VelocityComputer1D velocityComputer1D = new VelocityComputer1D(velocityTimeSeries._flowDistribution, velocityTimeSeries._XS, velocityTimeSeries._XSIS);
			int upstreamXSID = velocityTimeSeries._XSIS.GetUpstreamXSID(interpSurface.ID);
			int downstreamXSID = velocityTimeSeries._XSIS.GetDownstreamXSID(interpSurface.ID);
			float[] crossSectionFlow = crossSectionFlows[upstreamXSID];
			float[] crossSectionFlowArray = crossSectionFlows[downstreamXSID];
			float[] crossSectionWaterSurface = crossSectionWaterSurfaces[upstreamXSID];
			float[] crossSectionWaterSurfaceArray = crossSectionWaterSurfaces[downstreamXSID];
			checked
			{
				int count = nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					velocityComputer1D.UpdateXSIS(interpSurface.ID, crossSectionWaterSurface[i], crossSectionWaterSurfaceArray[i], crossSectionFlow[i], crossSectionFlowArray[i]);
					velocityComputer1D.ComputeVelocity(interpSurface.MapPoints, clipDepths, velMag, velX, velY, i);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D0
	{
		public MeshFV2D mesh;

		public Dictionary<int, float[]> faceVelocityLookups;

		public Dictionary<int, float[]> cellWSELLookups;

		public _Closure_0024__9_002D1 outerClosure;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				faceVelocityLookups = other.faceVelocityLookups;
				cellWSELLookups = other.cellWSELLookups;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(FlatCellMap mapCell)
		{
			int index = mapCell.Index;
			Cell cell = mesh.Cell(index);
			Vector2D[] cellVelocityTimeSeries = mesh.ComputeCellVelocityTimeSeries(index, faceVelocityLookups);
			float[] cellWSELLookup = cellWSELLookups[index];
			checked
			{
				int count = outerClosure.nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					if (cellWSELLookup[i] < cell.MinimumElevation + outerClosure.velocityTimeSeries.MinWSPlotTolerance)
					{
						continue;
					}
					Vector2D vector2D = cellVelocityTimeSeries[i];
					float magnitude = vector2D.Magnitude;
					foreach (int mapPoint in mapCell.MapPoints)
					{
						if (outerClosure.clipDepths[mapPoint][i] > 0f)
						{
							outerClosure.velX[mapPoint][i] = vector2D.X;
							outerClosure.velY[mapPoint][i] = vector2D.Y;
							outerClosure.velocities[mapPoint][i] = magnitude;
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D1
	{
		public int nTimesteps;

		public float[][] clipDepths;

		public float[][] velX;

		public float[][] velY;

		public float[][] velocities;

		public VelocityTimeSeries velocityTimeSeries;

		public _Closure_0024__9_002D1(_Closure_0024__9_002D1 other)
		{
			if (other != null)
			{
				nTimesteps = other.nTimesteps;
				clipDepths = other.clipDepths;
				velX = other.velX;
				velY = other.velY;
				velocities = other.velocities;
			}
		}
	}

	private readonly Names _velNames;

	private readonly Names _wsNames;

	private readonly Names _flowNames;

	private readonly DepthTimeSeries _dts;

	private readonly H5ColumnCache<float> _colCache;

	public static void Compute(RASResults res, RASGeometryMapPoints mps, float[] terrain, ref float[][] outVelocityMag, ref float[][] outVelocityX, ref float[][] outVelocityY, [Optional][DefaultParameterValue(null)] CacheCollection cache, [Optional][DefaultParameterValue(null)] ref float[][] outDepths)
	{
		new VelocityTimeSeries(res, cache).Compute(mps, terrain, ref outVelocityMag, ref outVelocityX, ref outVelocityY, ref outDepths);
	}

	public VelocityTimeSeries(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: true)
	{
		GetVelocityDatasetNames(ref _velNames, ref _flowNames, ref _wsNames);
		_velNames.Validate(_postProcessFilename);
		_flowNames.Validate(_postProcessFilename);
		_wsNames.Validate(_postProcessFilename);
		_colCache = _cache.GetColumnCache<float>(_postProcessFilename);
		_dts = new DepthTimeSeries(_result, cache);
	}

	public void Compute(RASGeometryMapPoints mps, float[] terrain, ref float[][] outVelocityMag, ref float[][] outVelocityX, ref float[][] outVelocityY, [Optional][DefaultParameterValue(null)] ref float[][] outDepths)
	{
		TimeSeriesInitialize(mps, -9999f, ref outVelocityMag);
		TimeSeriesInitialize(mps, -9999f, ref outVelocityX);
		TimeSeriesInitialize(mps, -9999f, ref outVelocityY);
		string dataset = null;
		string dataset2 = null;
		List<string> datasets = null;
		List<string> datasets2 = null;
		Names velNames = _velNames;
		int row = 0;
		velNames.Get2D(0, ref datasets, ref row);
		Names flowNames = _flowNames;
		row = 0;
		flowNames.Get1D(0, ref dataset2, ref row);
		Names wsNames = _wsNames;
		row = 0;
		wsNames.Get1D(0, ref dataset, ref row);
		Names wsNames2 = _wsNames;
		row = 0;
		wsNames2.Get2D(0, ref datasets2, ref row);
		_dts.Compute(mps, terrain, ref outDepths);
		List<Task> tasks = new List<Task>();
		float[][] clipDepths = outDepths;
		float[][] outVelocityMagArray = outVelocityMag;
		float[][] velX = outVelocityX;
		float[][] velY = outVelocityY;
		if (dataset2 != null && dataset != null && mps.MappedXSISs.Count > 0)
		{
			Dictionary<int, float[]> crossSectionFlows = ReadMappedXSTimeseries(mps, dataset2, _colCache);
			Dictionary<int, float[]> crossSectionWaterSurfaces = ReadMappedXSTimeseries(mps, dataset, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				ComputeVelocityInternal1DTimeSeries(crossSectionFlows, crossSectionWaterSurfaces, mps, clipDepths, outVelocityMagArray, velX, velY);
			}));
		}
		if (!datasets.IsNullOrEmpty() && !datasets2.IsNullOrEmpty() && mps.HasMapped2DCells())
		{
			Dictionary<int, Dictionary<int, float[]>> areaFaceVelocityTimeSeries = ReadMappedAndConnected2DFaceTimeseries(mps, datasets, _colCache);
			Dictionary<int, Dictionary<int, float[]>> areaCellWSELTimeSeries = ReadMappedAndConnected2DCellTimeseries(mps, datasets2, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				ComputeVelocityInternal2DTimeSeries(areaFaceVelocityTimeSeries, areaCellWSELTimeSeries, mps, clipDepths, outVelocityMagArray, velX, velY);
			}));
		}
		Task.WaitAll(tasks.ToArray());
	}

	private void ComputeVelocityInternal1DTimeSeries(Dictionary<int, float[]> crossSectionFlows, Dictionary<int, float[]> crossSectionWaterSurfaces, RASGeometryMapPoints mps, float[][] clipDepths, float[][] velMag, float[][] velX, float[][] velY)
	{
		_Closure_0024__8_002D0 capturedLocals = default;
		_Closure_0024__8_002D0 capturedLocals2 = new _Closure_0024__8_002D0(capturedLocals);
		capturedLocals2.velocityTimeSeries = this;
		capturedLocals2.crossSectionFlows = crossSectionFlows;
		capturedLocals2.crossSectionWaterSurfaces = crossSectionWaterSurfaces;
		capturedLocals2.clipDepths = clipDepths;
		capturedLocals2.velMag = velMag;
		capturedLocals2.velX = velX;
		capturedLocals2.velY = velY;
		capturedLocals2.nTimesteps = capturedLocals2.velMag[0].Length;
		Parallel.ForEach(mps.MappedXSISs, checked([SpecialName] (MappedXSIS interpSurface) =>
		{
			VelocityComputer1D velocityComputer1D = new VelocityComputer1D(capturedLocals2.velocityTimeSeries._flowDistribution, capturedLocals2.velocityTimeSeries._XS, capturedLocals2.velocityTimeSeries._XSIS);
			int upstreamXSID = capturedLocals2.velocityTimeSeries._XSIS.GetUpstreamXSID(interpSurface.ID);
			int downstreamXSID = capturedLocals2.velocityTimeSeries._XSIS.GetDownstreamXSID(interpSurface.ID);
			float[] crossSectionFlow = capturedLocals2.crossSectionFlows[upstreamXSID];
			float[] crossSectionFlowArray = capturedLocals2.crossSectionFlows[downstreamXSID];
			float[] crossSectionWaterSurface = capturedLocals2.crossSectionWaterSurfaces[upstreamXSID];
			float[] crossSectionWaterSurfaceArray = capturedLocals2.crossSectionWaterSurfaces[downstreamXSID];
			int count = capturedLocals2.nTimesteps - 1;
			for (int i = 0; i <= count; i++)
			{
				velocityComputer1D.UpdateXSIS(interpSurface.ID, crossSectionWaterSurface[i], crossSectionWaterSurfaceArray[i], crossSectionFlow[i], crossSectionFlowArray[i]);
				velocityComputer1D.ComputeVelocity(interpSurface.MapPoints, capturedLocals2.clipDepths, capturedLocals2.velMag, capturedLocals2.velX, capturedLocals2.velY, i);
			}
		}));
	}

	private void ComputeVelocityInternal2DTimeSeries(Dictionary<int, Dictionary<int, float[]>> areaFaceVelocityTimeSeries, Dictionary<int, Dictionary<int, float[]>> areaCellWSELTimeSeries, RASGeometryMapPoints mps, float[][] clipDepths, float[][] velocities, float[][] velX, float[][] velY)
	{
		_Closure_0024__9_002D1 capturedLocals = new _Closure_0024__9_002D1(null);
		capturedLocals.velocityTimeSeries = this;
		capturedLocals.clipDepths = clipDepths;
		capturedLocals.velocities = velocities;
		capturedLocals.velX = velX;
		capturedLocals.velY = velY;
		capturedLocals.nTimesteps = capturedLocals.velocities[0].Length;
		checked
		{
			_Closure_0024__9_002D0 capturedLocals2 = default;
			foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
			{
				capturedLocals2 = new _Closure_0024__9_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				int key = joinedMappedMeshe.Key;
				FlatMeshMap flatMeshMap = joinedMappedMeshe.Value.Item1;
				SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
				capturedLocals2.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(key);
				capturedLocals2.cellWSELLookups = areaCellWSELTimeSeries[key];
				capturedLocals2.faceVelocityLookups = areaFaceVelocityTimeSeries[key];
				if (flatMeshMap != null && flatMeshMap.Cells.Count > 0)
				{
					Parallel.ForEach(flatMeshMap.Cells, capturedLocals2._Lambda_0024__0);
				}
				if (slopingMeshMap == null || slopingMeshMap.Cells.Count <= 0)
				{
					continue;
				}
				float[] floatValues = new float[capturedLocals2.mesh.CellCount - 1 + 1];
				float[] floatValuesArray = new float[capturedLocals2.mesh.FaceCount - 1 + 1];
				int capturedLocals3 = capturedLocals2.outerClosure.velocities.Length;
				float[] floatValues2 = new float[capturedLocals3 - 1 + 1];
				float[] floatValues3 = new float[capturedLocals3 - 1 + 1];
				float[] floatValues4 = new float[capturedLocals3 - 1 + 1];
				float[] floatValues5 = new float[capturedLocals3 - 1 + 1];
				int count = capturedLocals2.outerClosure.nTimesteps - 1;
				for (int i = 0; i <= count; i++)
				{
					foreach (KeyValuePair<int, float[]> localValue in capturedLocals2.faceVelocityLookups)
					{
						floatValuesArray[localValue.Key] = localValue.Value[i];
					}
					foreach (KeyValuePair<int, float[]> localValueArray in capturedLocals2.cellWSELLookups)
					{
						floatValues[localValueArray.Key] = localValueArray.Value[i];
					}
					int count2 = capturedLocals3 - 1;
					for (int j = 0; j <= count2; j++)
					{
						floatValues2[j] = capturedLocals2.outerClosure.clipDepths[j][i];
						floatValues4[j] = -9999f;
						floatValues5[j] = -9999f;
						floatValues3[j] = -9999f;
					}
					Render2D_8Stencil(slopingMeshMap, floatValuesArray, floatValues4, floatValues5, floatValues3, floatValues, floatValues2);
					foreach (SlopingCellMap cell in slopingMeshMap.Cells)
					{
						foreach (SlopingCellPoint mapPoint in cell.MapPoints)
						{
							int index = mapPoint.Index;
							capturedLocals2.outerClosure.velocities[index][i] = floatValues3[index];
							capturedLocals2.outerClosure.velX[index][i] = floatValues4[index];
							capturedLocals2.outerClosure.velY[index][i] = floatValues5[index];
						}
					}
				}
			}
		}
	}

	public Dictionary<int, float[]> Read2DFaceTimeseries(int meshIdx, List<int> faces)
	{
		List<string> datasets = null;
		Names velNames = _velNames;
		int row = 0;
		velNames.Get2D(0, ref datasets, ref row);
		if (datasets.IsNullOrEmpty())
		{
			return null;
		}
		return Read2DColumns(faces, datasets[meshIdx], _colCache);
	}

	public float[] Read2DFaceTimeseries(int meshIdx, int face)
	{
		return Read2DFaceTimeseries(meshIdx, new int[1] { face }.ToList())[face];
	}
}
