using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class CourantTimeSeries : Renderer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__10_002D0
	{
		public RASXSInterpolationSurface xsis;

		public Dictionary<int, float[]> xsChannelVelocities;

		public float[] timesteps;

		public float[][] clipPixelDepths;

		public float[][] outCourants;

		public CourantTimeSeries courantTimeSeries;

		public _Closure_0024__10_002D0(_Closure_0024__10_002D0 other)
		{
			if (other != null)
			{
				xsis = other.xsis;
				xsChannelVelocities = other.xsChannelVelocities;
				timesteps = other.timesteps;
				clipPixelDepths = other.clipPixelDepths;
				outCourants = other.outCourants;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			int iD = interpSurface.ID;
			int upstreamXSID = courantTimeSeries._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = courantTimeSeries._XSIS.GetDownstreamXSID(iD);
			float lengthsForCourant = (float)xsis.GetLengthsForCourant(iD);
			float[] xsChannelVelocity = xsChannelVelocities[upstreamXSID];
			float[] xsChannelVelocityArray = xsChannelVelocities[downstreamXSID];
			checked
			{
				int count = timesteps.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					float timestep = timesteps[i];
					float computed = (xsChannelVelocity[i] + xsChannelVelocityArray[i]) / 2f;
					float computed2 = timestep * computed / lengthsForCourant;
					foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
					{
						if (!(clipPixelDepths[mapPoint.Index][i] <= 0f))
						{
							outCourants[mapPoint.Index][i] = computed2;
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D0
	{
		public RASGeometryMapPoints mps;

		public float[] timesteps;

		public float[][] outDepths;

		public float[][] lOC;

		public CourantTimeSeries courantTimeSeries;

		public _Closure_0024__9_002D0(_Closure_0024__9_002D0 other)
		{
			if (other != null)
			{
				mps = other.mps;
				timesteps = other.timesteps;
				outDepths = other.outDepths;
				lOC = other.lOC;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D1
	{
		public Dictionary<int, float[]> chanVelocityXSMap;

		public _Closure_0024__9_002D0 outerClosure;

		public _Closure_0024__9_002D1(_Closure_0024__9_002D1 other)
		{
			if (other != null)
			{
				chanVelocityXSMap = other.chanVelocityXSMap;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			outerClosure.courantTimeSeries.ComputeCourant1DChannelTimeSeries(outerClosure.mps, outerClosure.timesteps, chanVelocityXSMap, outerClosure.outDepths, outerClosure.lOC);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__9_002D2
	{
		public Dictionary<int, Dictionary<int, float[]>> faceVelocities;

		public Dictionary<int, Dictionary<int, float[]>> cellWaterSurfaces;

		public Dictionary<int, float[]> perMeshTimesteps;

		public _Closure_0024__9_002D0 outerClosure;

		public _Closure_0024__9_002D2(_Closure_0024__9_002D2 other)
		{
			if (other != null)
			{
				faceVelocities = other.faceVelocities;
				cellWaterSurfaces = other.cellWaterSurfaces;
				perMeshTimesteps = other.perMeshTimesteps;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.courantTimeSeries.ComputeCourantTimeSeries(faceVelocities, cellWaterSurfaces, perMeshTimesteps, outerClosure.mps, outerClosure.outDepths, outerClosure.lOC);
		}
	}

	private readonly Names _flowNames;

	private readonly Names _velNames;

	private readonly Names _vel1DChanNames;

	private readonly Names _wsNames;


	private readonly DepthTimeSeries _dts;

	private readonly H5ColumnCache<float> _colCache;

	public static void Compute(RASResults res, RASGeometryMapPoints mps, float[] terrain, ref float[][] outCourant, CacheCollection cache = null)
	{
		new CourantTimeSeries(res, cache).Compute(mps, terrain, ref outCourant);
	}

	public CourantTimeSeries(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: true)
	{
		GetVelocityDatasetNames(ref _velNames, ref _flowNames, ref _wsNames);
		Names velTotalNames = null;
		GetVelocity1DDatasetNames(ref _vel1DChanNames, ref velTotalNames);
		_result.GetPostProcessor().EnsureExists(_vel1DChanNames.Profile1D, popup: true);
		_velNames.Validate(_postProcessFilename);
		_flowNames.Validate(_postProcessFilename);
		_wsNames.Validate(_postProcessFilename);
		_vel1DChanNames.Validate(_postProcessFilename);
		_colCache = _cache.GetColumnCache<float>(_postProcessFilename);
		_dts = new DepthTimeSeries(_result, cache);
	}

	public void Compute(RASGeometryMapPoints mps, float[] terrain, ref float[][] outCourants)
	{
		_Closure_0024__9_002D0 capturedLocals = new _Closure_0024__9_002D0(null);
		capturedLocals.courantTimeSeries = this;
		capturedLocals.mps = mps;
		TimeSeriesInitialize(capturedLocals.mps, -9999f, ref outCourants);
		List<string> datasets = null;
		List<string> datasets2 = null;
		Names velNames = _velNames;
		int row = 0;
		velNames.Get2D(0, ref datasets, ref row);
		Names wsNames = _wsNames;
		row = 0;
		wsNames.Get2D(0, ref datasets2, ref row);
		string dataset = "";
		Names vel1DChanNames = _vel1DChanNames;
		row = 0;
		vel1DChanNames.Get1D(0, ref dataset, ref row);
		capturedLocals.outDepths = null;
		_dts.Compute(capturedLocals.mps, terrain, ref capturedLocals.outDepths);
		capturedLocals.timesteps = _result.BaseOutputTimes.ComputeTimeSteps;
		List<Task> tasks = new List<Task>();
		capturedLocals.lOC = outCourants;
		if (!string.IsNullOrEmpty(dataset) && capturedLocals.mps.MappedXSISs.Count > 0)
		{
			_Closure_0024__9_002D1 capturedLocals2 = default;
			_Closure_0024__9_002D1 capturedLocals3 = new _Closure_0024__9_002D1(capturedLocals2);
			capturedLocals3.outerClosure = capturedLocals;
			capturedLocals3.chanVelocityXSMap = ReadMappedXSTimeseries(capturedLocals3.outerClosure.mps, dataset, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals3.outerClosure.courantTimeSeries.ComputeCourant1DChannelTimeSeries(capturedLocals3.outerClosure.mps, capturedLocals3.outerClosure.timesteps, capturedLocals3.chanVelocityXSMap, capturedLocals3.outerClosure.outDepths, capturedLocals3.outerClosure.lOC);
			}));
		}
		if (!datasets.IsNullOrEmpty() && !datasets2.IsNullOrEmpty() && capturedLocals.mps.HasMapped2DCells())
		{
			_Closure_0024__9_002D2 capturedLocals4 = default;
			_Closure_0024__9_002D2 capturedLocals5 = new _Closure_0024__9_002D2(capturedLocals4);
			capturedLocals5.outerClosure = capturedLocals;
			capturedLocals5.perMeshTimesteps = new Dictionary<int, float[]>();
			foreach (int allMappedMeshIndex in capturedLocals5.outerClosure.mps.AllMappedMeshIndexes())
			{
				capturedLocals5.perMeshTimesteps[allMappedMeshIndex] = _result.Geometry.D2FlowArea.GetMeshComputationTimestepSeconds(allMappedMeshIndex);
			}
			capturedLocals5.faceVelocities = ReadMappedAndConnected2DFaceTimeseries(capturedLocals5.outerClosure.mps, datasets, _colCache);
			capturedLocals5.cellWaterSurfaces = ReadMappedAndConnected2DCellTimeseries(capturedLocals5.outerClosure.mps, datasets2, _colCache);
			tasks.Add(Task.Factory.StartNew([SpecialName] () =>
			{
				capturedLocals5.outerClosure.courantTimeSeries.ComputeCourantTimeSeries(capturedLocals5.faceVelocities, capturedLocals5.cellWaterSurfaces, capturedLocals5.perMeshTimesteps, capturedLocals5.outerClosure.mps, capturedLocals5.outerClosure.outDepths, capturedLocals5.outerClosure.lOC);
			}));
		}
		Task.WaitAll(tasks.ToArray());
	}

	private void ComputeCourant1DChannelTimeSeries(RASGeometryMapPoints mps, float[] timesteps, Dictionary<int, float[]> xsChannelVelocities, float[][] clipPixelDepths, float[][] outCourants)
	{
		_Closure_0024__10_002D0 capturedLocals = default;
		_Closure_0024__10_002D0 capturedLocals2 = new _Closure_0024__10_002D0(capturedLocals);
		capturedLocals2.courantTimeSeries = this;
		capturedLocals2.timesteps = timesteps;
		capturedLocals2.xsChannelVelocities = xsChannelVelocities;
		capturedLocals2.clipPixelDepths = clipPixelDepths;
		capturedLocals2.outCourants = outCourants;
		capturedLocals2.xsis = _geometry.XSInterpolationSurface;
		_geometry.XS.FeatureTable();
		capturedLocals2.xsis.EnsureLengthsForCourant();
		Parallel.ForEach(mps.MappedXSISs, checked([SpecialName] (MappedXSIS interpSurface) =>
		{
			int iD = interpSurface.ID;
			int upstreamXSID = capturedLocals2.courantTimeSeries._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = capturedLocals2.courantTimeSeries._XSIS.GetDownstreamXSID(iD);
			float lengthsForCourant = (float)capturedLocals2.xsis.GetLengthsForCourant(iD);
			float[] xsChannelVelocity = capturedLocals2.xsChannelVelocities[upstreamXSID];
			float[] xsChannelVelocityArray = capturedLocals2.xsChannelVelocities[downstreamXSID];
			int count = capturedLocals2.timesteps.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				float timestep = capturedLocals2.timesteps[i];
				float computed = (xsChannelVelocity[i] + xsChannelVelocityArray[i]) / 2f;
				float lengthsForCourantTerm = timestep * computed / lengthsForCourant;
				foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
				{
					if (!(capturedLocals2.clipPixelDepths[mapPoint.Index][i] <= 0f))
					{
						capturedLocals2.outCourants[mapPoint.Index][i] = lengthsForCourantTerm;
					}
				}
			}
		}));
	}

	protected void ComputeCourantTimeSeries(Dictionary<int, Dictionary<int, float[]>> meshFaceVelocityTimeSeries, Dictionary<int, Dictionary<int, float[]>> meshCellWSELTimeSeries, Dictionary<int, float[]> meshTimeSteps, RASGeometryMapPoints mps, float[][] clipDepths, float[][] outCourants)
	{
		checked
		{
			foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
			{
				int key = joinedMappedMeshe.Key;
				_ = joinedMappedMeshe.Value.Item1;
				_ = joinedMappedMeshe.Value.Item2;
				MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(key);
				float[] meshTimeStep = meshTimeSteps[key];
				_ = meshCellWSELTimeSeries[key];
				Dictionary<int, float[]> meshFaceVelocityTimeSery = meshFaceVelocityTimeSeries[key];
				int count = meshTimeStep.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					float meshTimeStep2 = meshTimeStep[i];
					foreach (FlatCellMap allMappedCell in mps.AllMappedCells(key))
					{
						int index = allMappedCell.Index;
						Cell cell = meshFV2D.Cell(index);
						float outCourants2 = 0f;
						int count2 = cell.Faces.Count - 1;
						for (int j = 0; j <= count2; j++)
						{
							int faceIndex = cell.Faces[j];
							Face face = meshFV2D.Faces[faceIndex];
							float distanceTo = (float)meshFV2D.Cell(face.cellA).Point.DistanceTo(meshFV2D.Cell(face.cellB).Point);
							outCourants2 = Math.Max(Math.Abs(meshFaceVelocityTimeSery[faceIndex][i]) * meshTimeStep2 / distanceTo, outCourants2);
						}
						foreach (int mapPoint in allMappedCell.MapPoints)
						{
							if (clipDepths[mapPoint][i] > 0f)
							{
								outCourants[mapPoint][i] = outCourants2;
							}
							else
							{
								outCourants[mapPoint][i] = -9999f;
							}
						}
					}
				}
			}
		}
	}
}
