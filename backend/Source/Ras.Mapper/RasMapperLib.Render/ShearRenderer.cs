using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using RasMapperLib.Names;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class ShearRenderer : Renderer
{
	[CompilerGenerated]
private sealed class _Closure_0024__23_002D0
	{
		public Vector2D[] shearVecs;

		public float[] lSS;

		public _Closure_0024__23_002D0(_Closure_0024__23_002D0 other)
		{
			if (other != null)
			{
				shearVecs = other.shearVecs;
				lSS = other.lSS;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				shearVecs[i] = Vector2D.NoData;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (shearVecs[i] == Vector2D.NoData)
				{
					lSS[i] = -9999f;
				}
				else
				{
					lSS[i] = shearVecs[i].Magnitude;
				}
			}
		}
	}

	private const bool UsePostProcessShear = true;

	private readonly Names _shearNames;

	private readonly Names _velNames;

	private readonly Names _flowNames;

	private readonly Names _wsNames;

	private readonly H5RowCache<float> _ppCache;

	private readonly float[][] _meshFaceManns;

	private readonly DepthRenderer _depthRenderer;

	private string _ws1D;

	private string _flow1D;

	private string _shear1D;

	private int _ws1DRow;

	private int _flow1DRow;

	private int _shear1DRow;

	private List<string> _ws2D;

	private List<string> _vel2D;

	private List<string> _shear2D;

	private int _ws2DRow;

	private int _vel2DRow;

	private int _shear2DRow;

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] shearStress, CacheCollection cache = null)
	{
		new ShearRenderer(res, cache).Compute(profile, mps, terrain, ref shearStress);
	}

	public ShearRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: true)
	{
		_postProcessor.EnsureHydraulicFacePropertiesPopup();
		_postProcessor.EnsureShearPopup();
		GetDatasetNames(ref _shearNames, ref _velNames, ref _flowNames, ref _wsNames);
		_velNames.Validate(_hdfFilename);
		_flowNames.Validate(_hdfFilename);
		_wsNames.Validate(_hdfFilename);
		_shearNames.Validate(_postProcessFilename);
		if (File.Exists(_postProcessFilename))
		{
			_ppCache = new H5RowCache<float>(_postProcessFilename);
		}
		int meshCount = _d2FlowArea.MeshCount;
		checked
		{
			_meshFaceManns = new float[meshCount - 1 + 1][];
			int count = meshCount - 1;
			for (int i = 0; i <= count; i++)
			{
				string featureName = _d2FlowArea.GetFeatureName(i);
				_meshFaceManns[i] = _d2FlowArea.ReadFaceMannValues(featureName);
			}
			_depthRenderer = new DepthRenderer(res, cache);
		}
	}

	private void LoadAllNames(int profile)
	{
		_wsNames.Get1D(profile, ref _ws1D, ref _ws1DRow);
		_wsNames.Get2D(profile, ref _ws2D, ref _ws2DRow);
		_velNames.Get2D(profile, ref _vel2D, ref _vel2DRow);
		_flowNames.Get1D(profile, ref _flow1D, ref _flow1DRow);
		_shearNames.Get1D(profile, ref _shear1D, ref _shear1DRow);
		_shearNames.Get2D(profile, ref _shear2D, ref _shear2DRow);
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] shearStress)
	{
		LoadAllNames(profile);
		Buffers.ArraySetAllVB(ref shearStress, -9999f, mps.InputCount);
		H5RowCache<float> rowCache = _cache.GetRowCache<float>(_hdfFilename);
		float[] depths = null;
		_depthRenderer.Compute(profile, mps, terrain, ref depths);
		if (profile == int.MaxValue || profile == int.MinValue)
		{
			if (!string.IsNullOrEmpty(_shear1D))
			{
				Dictionary<int, float[]> xsSubsectionValues = ReadMappedXSProfile(mps, _shear1D, _ppCache);
				Compute1DFromSubsections(xsSubsectionValues, mps, depths, shearStress);
			}
			if (!_shear2D.IsNullOrEmpty() && !_ws2D.IsNullOrEmpty())
			{
				float[][] meshCellWaterSurfaces = ReadMappedMeshProfile(_ws2D, _ws2DRow, rowCache, mps);
				float[][] mappedMeshProfile = null;
				mappedMeshProfile = ReadMappedMeshProfile(_shear2D, _shear2DRow, _ppCache, mps);
				ComputeFromFaceScalarValues(mps, mappedMeshProfile, meshCellWaterSurfaces, Coverage.Wet, shearStress, depths);
			}
			return;
		}
		if (_ws1D != null && _flow1D != null && _flowDistribution != null)
		{
			float[] xsFlows = rowCache.EnsureGetRow(_flow1D, _flow1DRow);
			float[] xsWSELs = rowCache.EnsureGetRow(_ws1D, _ws1DRow);
			ComputeShearInternal1DFromFlow(xsFlows, xsWSELs, mps, depths, shearStress);
		}
		if (_vel2D.IsNullOrEmpty() || _ws2D.IsNullOrEmpty())
		{
			return;
		}
		_Closure_0024__23_002D0 capturedLocals = default;
		_Closure_0024__23_002D0 capturedLocals2 = new _Closure_0024__23_002D0(capturedLocals);
		float[][] meshCellWaterSurfaces2 = ReadMappedMeshProfile(_ws2D, _ws2DRow, rowCache, mps);
		List<string> meshDatasets = PostProcessDatasets.Profile.FlowAreas.HydraulicRadius(_d2FlowAreaNames);
		float[][] meshFacePerpendicularVelocities = ReadMappedMeshProfile(_vel2D, _vel2DRow, rowCache, mps);
		float[][] meshHydrRadius = ReadMappedMeshProfile(meshDatasets, profile, _ppCache, mps);
		checked
		{
			capturedLocals2.shearVecs = new Vector2D[shearStress.Count() - 1 + 1];
			Utility.Parallel.SmartFor(shearStress.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.shearVecs[i] = Vector2D.NoData;
				}
			});
			ComputeShearInternal2D(mps, meshHydrRadius, meshCellWaterSurfaces2, meshFacePerpendicularVelocities, _meshFaceManns, capturedLocals2.shearVecs, depths);
			capturedLocals2.lSS = shearStress;
			Utility.Parallel.SmartFor(shearStress.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals2.shearVecs[i] == Vector2D.NoData)
					{
						capturedLocals2.lSS[i] = -9999f;
					}
					else
					{
						capturedLocals2.lSS[i] = capturedLocals2.shearVecs[i].Magnitude;
					}
				}
			});
		}
	}

	private void ComputeShearInternal1DFromFlow(float[] xsFlows, float[] xsWSELs, RASGeometryMapPoints mps, float[] clipDepths, float[] values)
	{
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			VelocityComputer1D velocityComputer1D = new VelocityComputer1D(_flowDistribution, _XS, _XSIS);
			velocityComputer1D.UpdateXSIS(interpSurface.ID, xsWSELs, xsFlows);
			velocityComputer1D.ComputeShear(interpSurface.MapPoints, clipDepths, values);
		});
	}

	private void ComputeShearInternal2D(RASGeometryMapPoints mps, float[][] meshHydrRadius, float[][] meshCellWaterSurfaces, float[][] meshFacePerpendicularVelocities, float[][] meshFaceMann, Vector2D[] outShear, float[] clipPixelDepths)
	{
		foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
		{
			int key = joinedMappedMeshe.Key;
			FlatMeshMap flatMeshMap = joinedMappedMeshe.Value.Item1;
			SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
			float[] cellWaterSurface = meshCellWaterSurfaces[key];
			float[] meshFacePerpendicularVelocity = meshFacePerpendicularVelocities[key];
			float[] faceMann = meshFaceMann[key];
			float[] hydrRadius = meshHydrRadius[key];
			if (slopingMeshMap != null && slopingMeshMap.Cells.Count > 0)
			{
				ComputeShearInternal2D(slopingMeshMap, cellWaterSurface, meshFacePerpendicularVelocity, hydrRadius, faceMann, outShear, clipPixelDepths);
			}
			if (flatMeshMap != null && flatMeshMap.Cells.Count > 0)
			{
				ComputeShearInternal2D(flatMeshMap, cellWaterSurface, meshFacePerpendicularVelocity, hydrRadius, faceMann, outShear, clipPixelDepths);
			}
		}
	}

	private void ComputeShearInternal2D(SlopingMeshMap slopedArea, float[] cellWaterSurface, float[] faceNormalVelocities, float[] hydrRadius, float[] faceMann, Vector2D[] outShear, float[] clipPixelDepths)
	{
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		FaceValues[] slopingWSFaceFactors = SlopingFactors.ComputeSlopingWSFaceFactors(_d2FlowArea, cellWaterSurface, slopedArea);
		List<int> uniqueFaceList = Get1RingFPsAroundFaces(meshFV2D, slopedArea);
		FacePointVels[] fpVels = meshFV2D.ComputeSlopingFacepointVelocities_LeastSquares(faceNormalVelocities, slopingWSFaceFactors, uniqueFaceList);
		Vector2D[] faceTrueVels = meshFV2D.ComputeFaceTrueVelocities(slopingWSFaceFactors, fpVels, slopedArea);
		Vector2D[] faceVectors = meshFV2D.ComputeFaceTrueShear(faceTrueVels, faceMann, hydrRadius, slopedArea);
		Vector2D[] fpValues = AverageFaceVectorsToFacepointValuesSA(meshFV2D, faceVectors, slopedArea.UniqueFacePoints);
		ComputeFromFacepointValues(slopedArea, fpValues, outShear, clipPixelDepths, cellWaterSurface);
	}

	private void ComputeShearInternal2D(FlatMeshMap flatArea, float[] cellWaterSurface, float[] facePerpendicularVelocities, float[] hydrRadius, float[] faceMann, Vector2D[] outShear, float[] clipPixelDepths)
	{
		int meshIndex = flatArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Mesh(meshIndex);
		_d2FlowArea.GetFeatureName(meshIndex);
		SlopingMeshMap meshMap = new SlopingMeshMap(meshIndex, new List<SlopingCellMap>(), null, null);
		FaceValues[] slopingWSFaceFactors = SlopingFactors.ComputeSlopingWSFaceFactors(_d2FlowArea, cellWaterSurface, meshMap);
		FacePointVels[] fpVels = meshFV2D.ComputeSlopingFacepointVelocities_LeastSquares(facePerpendicularVelocities, slopingWSFaceFactors);
		Vector2D[] faceTrueVels = meshFV2D.ComputeFaceTrueVelocities(slopingWSFaceFactors, fpVels);
		Vector2D[] faceVectors = meshFV2D.ComputeFaceTrueShear(faceTrueVels, faceMann, hydrRadius);
		Vector2D[] cellInputValues = AverageFaceVectorsToCellValues(meshFV2D, faceVectors, flatArea.UniqueCells());
		ComputeCellValuesFlat(flatArea, cellInputValues, outShear, clipPixelDepths, cellWaterSurface);
	}

	protected void GetDatasetNames(ref Names shearNames, ref Names velNames, ref Names flowNames, ref Names wsNames)
	{
		flowNames = new Names();
		wsNames = new Names();
		velNames = new Names();
		shearNames = new Names();
		if (_result.UnsteadyHydraulics)
		{
			wsNames.Max2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MaxWaterSurface.Name(_d2FlowAreaNames);
			wsNames.Max2DRow = 0;
			shearNames.Max1D = PostProcessDatasets.Profile.CrossSections.MaxShear();
			shearNames.Min1D = PostProcessDatasets.Profile.CrossSections.MinShear();
			shearNames.Max2D = PostProcessDatasets.Profile.FlowAreas.MaxShear(_d2FlowAreaNames);
			shearNames.Max2DRow = 0;
			shearNames.Min2D = PostProcessDatasets.Profile.FlowAreas.MinShear(_d2FlowAreaNames);
			shearNames.Min2DRow = 0;
			wsNames.Min2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MinWaterSurface.Name(_d2FlowAreaNames);
			wsNames.Min2DRow = 0;
			flowNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";
			wsNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
			velNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceVelocity(_d2FlowAreaNames);
			wsNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(_d2FlowAreaNames);
		}
		else if (_result.SteadyHydraulics)
		{
			flowNames.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow";
			wsNames.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";
		}
	}
}
