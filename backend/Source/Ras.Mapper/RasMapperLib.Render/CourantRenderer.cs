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

public class CourantRenderer : Renderer
{
	public enum CourantMethod
	{
		Courant,
		ResidenceTime
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__12_002D0
	{
		public float[] xsChannelVelocities;

		public RASXSInterpolationSurface xsis;

		public float timestep;

		public float[] clipPixelDepths;

		public float[] outCourants;

		public CourantRenderer courantRenderer;

		public _Closure_0024__12_002D0(_Closure_0024__12_002D0 other)
		{
			if (other != null)
			{
				xsChannelVelocities = other.xsChannelVelocities;
				xsis = other.xsis;
				timestep = other.timestep;
				clipPixelDepths = other.clipPixelDepths;
				outCourants = other.outCourants;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			int iD = interpSurface.ID;
			int upstreamXSID = courantRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = courantRenderer._XSIS.GetDownstreamXSID(iD);
			float xsChannelVelocity = xsChannelVelocities[upstreamXSID];
			float xsChannelVelocity2 = xsChannelVelocities[downstreamXSID];
			float computed = (xsChannelVelocity + xsChannelVelocity2) / 2f;
			float lengthsForCourant = (float)xsis.GetLengthsForCourant(iD);
			float computed2 = timestep * computed / lengthsForCourant;
			foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
			{
				if (!(clipPixelDepths[mapPoint.Index] <= 0f))
				{
					outCourants[mapPoint.Index] = computed2;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__13_002D0
	{
		public float[] xsTotalVelocities;

		public RASXS xs;

		public float timestep;

		public float[] outCourants;

		public CourantRenderer courantRenderer;

		public _Closure_0024__13_002D0(_Closure_0024__13_002D0 other)
		{
			if (other != null)
			{
				xsTotalVelocities = other.xsTotalVelocities;
				xs = other.xs;
				timestep = other.timestep;
				outCourants = other.outCourants;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			int iD = interpSurface.ID;
			int upstreamXSID = courantRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = courantRenderer._XSIS.GetDownstreamXSID(iD);
			float xsTotalVelocity = xsTotalVelocities[upstreamXSID];
			float xsTotalVelocity2 = xsTotalVelocities[downstreamXSID];
			float computed = (xsTotalVelocity + xsTotalVelocity2) / 2f;
			float channelLength = xs.GetChannelLength(upstreamXSID);
			float leftOverbankLength = xs.GetLeftOverbankLength(upstreamXSID);
			float rightOverbankLength = xs.GetRightOverbankLength(upstreamXSID);
			float channelLengthTerm = timestep * computed / channelLength;
			float leftOverbankLengthTerm = timestep * computed / leftOverbankLength;
			float rightOverbankLengthTerm = timestep * computed / rightOverbankLength;
			foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
			{
				if (mapPoint.M <= -1f)
				{
					outCourants[mapPoint.Index] = leftOverbankLengthTerm;
				}
				else if (mapPoint.M >= 1f)
				{
					outCourants[mapPoint.Index] = rightOverbankLengthTerm;
				}
				else
				{
					outCourants[mapPoint.Index] = channelLengthTerm;
				}
			}
		}
	}

	private readonly Names _flowNames;

	private readonly Names _velNames;

	private readonly Names _vel1DChanNames;

	private readonly Names _wsNames;

	private readonly H5RowCache<float> _rowCache;

	private readonly DepthRenderer _depthRenderer;

	private CourantMethod _courantMethod;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] courantValues, CacheCollection cache = null)
	{
		new CourantRenderer(res, CourantMethod.Courant, cache).Compute(profile, mps, terrain, ref courantValues);
	}

	public static void ComputeResidenceTime(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] courantValues, CacheCollection cache = null)
	{
		new CourantRenderer(res, CourantMethod.ResidenceTime, cache).Compute(profile, mps, terrain, ref courantValues);
	}

	public CourantRenderer(RASResults res, CourantMethod method, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		_courantMethod = method;
		GetVelocityDatasetNames(ref _velNames, ref _flowNames, ref _wsNames);
		Names velTotalNames = null;
		GetVelocity1DDatasetNames(ref _vel1DChanNames, ref velTotalNames);
		_velNames.Validate(_hdfFilename);
		_wsNames.Validate(_hdfFilename);
		_flowNames.Validate(_hdfFilename);
		_vel1DChanNames.Validate(_hdfFilename);
		_rowCache = _cache.GetRowCache<float>(_hdfFilename);
		_depthRenderer = new DepthRenderer(res, cache);
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outCourant)
	{
		Buffers.ArraySetAllVB(ref outCourant, -9999f, mps.InputCount);
		if (profile == int.MaxValue || profile == int.MinValue)
		{
			return;
		}
		float[] depths = null;
		_depthRenderer.Compute(profile, mps, terrain, ref depths);
		List<string> datasets = null;
		List<string> datasets2 = null;
		int row = default;
		_velNames.Get2D(profile, ref datasets, ref row);
		int row2 = default;
		_wsNames.Get2D(profile, ref datasets2, ref row2);
		string dataset = "";
		int row3 = default;
		_vel1DChanNames.Get1D(profile, ref dataset, ref row3);
		if (!string.IsNullOrEmpty(dataset))
		{
			float computeTimeStep = 0f;
			float[] computeTimeSteps = _result.BaseOutputTimes.ComputeTimeSteps;
			if (computeTimeSteps != null && computeTimeSteps.Length > profile)
			{
				computeTimeStep = computeTimeSteps[profile];
			}
			if (computeTimeStep != 0f)
			{
				float[] xsChannelVelocities = _rowCache.EnsureGetRow(dataset, row3);
				ComputeCourant1DChannel(mps, computeTimeStep, xsChannelVelocities, depths, outCourant);
			}
		}
		if (datasets.IsNullOrEmpty() || datasets2.IsNullOrEmpty() || !mps.HasMapped2DCells())
		{
			return;
		}
		Dictionary<int, float[]> floatValuesByInt = new Dictionary<int, float[]>();
		foreach (int allMappedMeshIndex in mps.AllMappedMeshIndexes())
		{
			floatValuesByInt[allMappedMeshIndex] = _result.Geometry.D2FlowArea.GetMeshComputationTimestepSeconds(allMappedMeshIndex);
		}
		float[][] meshFaceVelocities = ReadMappedMeshProfile(datasets, row, _rowCache, mps);
		float[][] meshCellWS = ReadMappedMeshProfile(datasets2, row2, _rowCache, mps);
		checked
		{
			List<Point2DCollection>[] point2DCollectionListArray = new List<Point2DCollection>[_d2FlowArea.MeshCount - 1 + 1];
			List<Point2DCollection>[] point2DCollectionListArrayList = new List<Point2DCollection>[_d2FlowArea.MeshCount - 1 + 1];
			foreach (int allMappedMeshIndex2 in mps.AllMappedMeshIndexes())
			{
				string meshName = _d2FlowAreaNames[allMappedMeshIndex2];
				MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(allMappedMeshIndex2);
				float[] floatValuesByIntArray = floatValuesByInt[allMappedMeshIndex2];
				if (floatValuesByIntArray == null || floatValuesByIntArray.Count() < profile)
				{
					continue;
				}
				float timestep = floatValuesByIntArray[profile];
				if (_courantMethod == CourantMethod.Courant)
				{
					ComputeCourantByFaceValues(allMappedMeshIndex2, meshFaceVelocities, meshCellWS, timestep, mps, depths, outCourant);
				}
				else if (_courantMethod == CourantMethod.ResidenceTime)
				{
					List<Point2DCollection> point2DCollections = (point2DCollectionListArray[allMappedMeshIndex2] = _d2FlowArea.ReadCellVolumeTables(meshName));
					List<Point2DCollection> point2DCollectionsList = (point2DCollectionListArrayList[allMappedMeshIndex2] = _d2FlowArea.ReadFaceAreaTables(meshName));
					int count = meshFV2D.NonVirtualCellCount - 1;
					for (int i = 0; i <= count; i++)
					{
						point2DCollections[i].InvertXY();
					}
					int count2 = point2DCollectionsList.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						point2DCollectionsList[j].InvertXY();
					}
					ComputeCourantByVolumeDelta(allMappedMeshIndex2, meshFaceVelocities, meshCellWS, point2DCollectionListArray, point2DCollectionListArrayList, timestep, mps, depths, outCourant);
				}
			}
		}
	}

	private void ComputeCourant1DChannel(RASGeometryMapPoints mps, float timestep, float[] xsChannelVelocities, float[] clipPixelDepths, float[] outCourants)
	{
		_Closure_0024__12_002D0 capturedLocals = default;
		_Closure_0024__12_002D0 capturedLocals2 = new _Closure_0024__12_002D0(capturedLocals);
		capturedLocals2.courantRenderer = this;
		capturedLocals2.timestep = timestep;
		capturedLocals2.xsChannelVelocities = xsChannelVelocities;
		capturedLocals2.clipPixelDepths = clipPixelDepths;
		capturedLocals2.outCourants = outCourants;
		capturedLocals2.xsis = _geometry.XSInterpolationSurface;
		_geometry.XS.FeatureTable();
		capturedLocals2.xsis.EnsureLengthsForCourant();
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			int iD = interpSurface.ID;
			int upstreamXSID = capturedLocals2.courantRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = capturedLocals2.courantRenderer._XSIS.GetDownstreamXSID(iD);
			float xsChannelVelocity = capturedLocals2.xsChannelVelocities[upstreamXSID];
			float xsChannelVelocity2 = capturedLocals2.xsChannelVelocities[downstreamXSID];
			float computed = (xsChannelVelocity + xsChannelVelocity2) / 2f;
			float lengthsForCourant = (float)capturedLocals2.xsis.GetLengthsForCourant(iD);
			float capturedLocals3 = capturedLocals2.timestep * computed / lengthsForCourant;
			foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
			{
				if (!(capturedLocals2.clipPixelDepths[mapPoint.Index] <= 0f))
				{
					capturedLocals2.outCourants[mapPoint.Index] = capturedLocals3;
				}
			}
		});
	}

	private void ComputeCourant1DAveraged(RASGeometryMapPoints mps, float timestep, float[] xsTotalVelocities, float[] clipPixelDepths, float[] outCourants)
	{
		_Closure_0024__13_002D0 capturedLocals = default;
		_Closure_0024__13_002D0 capturedLocals2 = new _Closure_0024__13_002D0(capturedLocals);
		capturedLocals2.courantRenderer = this;
		capturedLocals2.timestep = timestep;
		capturedLocals2.xsTotalVelocities = xsTotalVelocities;
		capturedLocals2.outCourants = outCourants;
		_ = _geometry.XSInterpolationSurface;
		capturedLocals2.xs = _geometry.XS;
		capturedLocals2.xs.FeatureTable();
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			int iD = interpSurface.ID;
			int upstreamXSID = capturedLocals2.courantRenderer._XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = capturedLocals2.courantRenderer._XSIS.GetDownstreamXSID(iD);
			float xsTotalVelocity = capturedLocals2.xsTotalVelocities[upstreamXSID];
			float xsTotalVelocity2 = capturedLocals2.xsTotalVelocities[downstreamXSID];
			float computed = (xsTotalVelocity + xsTotalVelocity2) / 2f;
			float channelLength = capturedLocals2.xs.GetChannelLength(upstreamXSID);
			float leftOverbankLength = capturedLocals2.xs.GetLeftOverbankLength(upstreamXSID);
			float rightOverbankLength = capturedLocals2.xs.GetRightOverbankLength(upstreamXSID);
			float capturedLocals3 = capturedLocals2.timestep * computed / channelLength;
			float capturedLocals4 = capturedLocals2.timestep * computed / leftOverbankLength;
			float capturedLocals5 = capturedLocals2.timestep * computed / rightOverbankLength;
			foreach (XSISMapPoint mapPoint in interpSurface.MapPoints)
			{
				if (mapPoint.M <= -1f)
				{
					capturedLocals2.outCourants[mapPoint.Index] = capturedLocals4;
				}
				else if (mapPoint.M >= 1f)
				{
					capturedLocals2.outCourants[mapPoint.Index] = capturedLocals5;
				}
				else
				{
					capturedLocals2.outCourants[mapPoint.Index] = capturedLocals3;
				}
			}
		});
	}

	protected void ComputeCourantByVolumeDelta(int meshIdx, float[][] meshFaceVelocities, float[][] meshCellWS, List<Point2DCollection>[] meshCellVolTables, List<Point2DCollection>[] meshFaceAreaTables, float timestep, RASGeometryMapPoints mps, float[] clipPixelDepths, float[] outCourant)
	{
		Tuple<FlatMeshMap, SlopingMeshMap> tuple = mps.JoinedMappedMeshes()[meshIdx];
		FlatMeshMap flatMeshMap = tuple.Item1;
		SlopingMeshMap slopingMeshMap = tuple.Item2;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIdx);
		float[] meshCellWSArray = meshCellWS[meshIdx];
		float[] perpFaceVels = meshFaceVelocities[meshIdx];
		List<Point2DCollection> faceAreaTables = meshFaceAreaTables[meshIdx];
		List<Point2DCollection> meshCellVolTable = meshCellVolTables[meshIdx];
		FaceValues[] faceWSs = SlopingFactors.ComputeSlopingWSFaceFactors(_d2FlowArea, meshCellWSArray, slopingMeshMap);
		float[] faceVolumeTransfer = ComputeVolumeAcrossFaces(meshFV2D, faceWSs, perpFaceVels, faceAreaTables, timestep);
		checked
		{
			float[] floatValues = new float[meshFV2D.CellCount - 1 + 1];
			SumVolumeLeaving(faceVolumeTransfer, meshFV2D, slopingMeshMap, floatValues);
			SumVolumeLeaving(faceVolumeTransfer, meshFV2D, flatMeshMap, floatValues);
			int count = meshFV2D.NonVirtualCellCount - 1;
			for (int i = 0; i <= count; i++)
			{
				floatValues[i] /= meshCellVolTable[i].QueryWithLinearExtrapolation(meshCellWSArray[i], (float)meshFV2D.CellPolygonArea(i));
			}
			ComputeCellValuesFlat(slopingMeshMap, floatValues, outCourant, clipPixelDepths, meshCellWSArray);
			ComputeCellValuesFlat(flatMeshMap, floatValues, outCourant, clipPixelDepths, meshCellWSArray);
		}
	}

	protected void ComputeCourantByFaceValues(int meshIdx, float[][] meshFaceVelocities, float[][] meshCellWS, float timestep, RASGeometryMapPoints mps, float[] clipPixelDepths, float[] outCourant)
	{
		Tuple<FlatMeshMap, SlopingMeshMap> tuple = mps.JoinedMappedMeshes()[meshIdx];
		FlatMeshMap flatMeshMap = tuple.Item1;
		SlopingMeshMap slopingMeshMap = tuple.Item2;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIdx);
		float[] clipCellWSEL = meshCellWS[meshIdx];
		float[] meshFaceVelocity = meshFaceVelocities[meshIdx];
		checked
		{
			float[] floatValues = new float[meshFV2D.FaceCount - 1 + 1];
			int count = meshFV2D.FaceCount - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = meshFV2D.Faces[i];
				float distanceTo = (float)meshFV2D.Cell(face.cellA).Point.DistanceTo(meshFV2D.Cell(face.cellB).Point);
				floatValues[i] = Math.Abs(meshFaceVelocity[i]) * timestep / distanceTo;
			}
			float[] floatValuesArray = new float[meshFV2D.NonVirtualCellCount - 1 + 1];
			MaximumABSFaceScalarsToCellValues(meshFV2D, floatValues, floatValuesArray, slopingMeshMap.UniqueCells());
			MaximumABSFaceScalarsToCellValues(meshFV2D, floatValues, floatValuesArray, flatMeshMap.UniqueCells());
			ComputeCellValuesFlat(slopingMeshMap, floatValuesArray, outCourant, clipPixelDepths, clipCellWSEL);
			ComputeCellValuesFlat(flatMeshMap, floatValuesArray, outCourant, clipPixelDepths, clipCellWSEL);
		}
	}

	private void SumVolumeLeaving(float[] faceVolumeTransfer, MeshFV2D mesh, MeshMap meshmap, float[] outCellVolSum)
	{
		foreach (int uniqueCellIndex in meshmap.UniqueCells())
		{
			Cell cell = mesh.Cell(uniqueCellIndex);
			float outCellVolSum2 = 0f;
			foreach (int face in cell.Faces)
			{
				float faceVolumeTransfer2 = faceVolumeTransfer[face];
				if (mesh.Faces[face].cellA == uniqueCellIndex)
				{
					if (faceVolumeTransfer2 > 0f)
					{
						outCellVolSum2 += faceVolumeTransfer2;
					}
				}
				else if (faceVolumeTransfer2 < 0f)
				{
					outCellVolSum2 += -1f * faceVolumeTransfer2;
				}
			}
			outCellVolSum[uniqueCellIndex] = outCellVolSum2;
		}
	}

	private float[] ComputeVolumeAcrossFaces(MeshFV2D mesh, FaceValues[] faceWSs, float[] perpFaceVels, List<Point2DCollection> faceAreaTables, float timestep)
	{
		checked
		{
			float[] volumeAcrossFaces = new float[faceWSs.Length - 1 + 1];
			int count = faceWSs.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				FaceValues faceValues = faceWSs[i];
				if (faceValues.HydraulicConnection == HydraulicConnection.None)
				{
					volumeAcrossFaces[i] = 0f;
					continue;
				}
				float xInput = Math.Max(faceValues.ValueA, faceValues.ValueB);
				float queryWithLinearExtrapolation = faceAreaTables[i].QueryWithLinearExtrapolation(xInput, (float)mesh.FaceSimpleLength(i));
				volumeAcrossFaces[i] = queryWithLinearExtrapolation * timestep * perpFaceVels[i];
			}
			return volumeAcrossFaces;
		}
	}

	private float[] ComputeCourantFactorAcrossFaces(MeshFV2D mesh, FaceValues[] faceWSs, float[] cellWaterSurface, float[] perpFaceVels, List<Point2DCollection> faceAreaTables, List<Point2DCollection> cellVolTables, double timeSecs)
	{
		checked
		{
			float[] courantFactorAcrossFaces = new float[faceWSs.Length - 1 + 1];
			int count = faceWSs.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				FaceValues faceValues = faceWSs[i];
				if (faceValues.HydraulicConnection == HydraulicConnection.None)
				{
					courantFactorAcrossFaces[i] = 0f;
					continue;
				}
				float xInput = Math.Max(faceValues.ValueA, faceValues.ValueB);
				float queryWithLinearExtrapolation = faceAreaTables[i].QueryWithLinearExtrapolation(xInput, (float)mesh.FacePolylineLength(i));
				Face face = mesh.Faces[i];
				int cellA = face.cellA;
				int cellB = face.cellB;
				float queryWithLinearExtrapolation2 = cellVolTables[cellA].QueryWithLinearExtrapolation(cellWaterSurface[cellA], (float)mesh.CellPolygonArea(cellA));
				if (mesh.CellIsVirtual(cellB))
				{
					courantFactorAcrossFaces[i] = (float)((double)(queryWithLinearExtrapolation * perpFaceVels[i]) * timeSecs / (double)queryWithLinearExtrapolation2);
					continue;
				}
				float queryWithLinearExtrapolation3 = cellVolTables[cellB].QueryWithLinearExtrapolation(cellWaterSurface[cellB], (float)mesh.CellPolygonArea(cellB));
				courantFactorAcrossFaces[i] = (float)((double)(queryWithLinearExtrapolation * perpFaceVels[i]) * timeSecs / (double)Math.Min(queryWithLinearExtrapolation2, queryWithLinearExtrapolation3));
			}
			return courantFactorAcrossFaces;
		}
	}
}
