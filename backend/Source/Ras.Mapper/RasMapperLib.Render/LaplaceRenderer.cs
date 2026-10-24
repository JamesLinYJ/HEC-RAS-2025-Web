using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist.Caching.VB;
using RasMapperLib.Mapping;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class LaplaceRenderer : Renderer
{
	private readonly Names _lpUNames;

	private readonly Names _lpVNames;

	private readonly Names _wsNames;

	private readonly H5RowCache<float> _rowCache;

	private readonly DepthRenderer _depthRenderer;

	public LaplaceRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		GetLPSurfaceDatasetNames(ref _wsNames, ref _lpUNames, ref _lpVNames);
		_wsNames.Validate(_hdfFilename);
		_lpUNames.Validate(_hdfFilename);
		_lpVNames.Validate(_hdfFilename);
		_rowCache = _cache.GetRowCache<float>(_hdfFilename);
		_depthRenderer = new DepthRenderer(res, cache);
	}

	public static void Compute(RASResults res, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] laplacianValues, bool isLPU, CacheCollection cache = null)
	{
		LaplaceRenderer laplaceRenderer = new LaplaceRenderer(res, cache);
		float[] outLPU = null;
		float[] outLPV = null;
		laplaceRenderer.Compute(profile, mps, terrain, ref outLPU, ref outLPV);
		if (isLPU)
		{
			laplacianValues = outLPU;
		}
		else
		{
			laplacianValues = outLPV;
		}
	}

	public void Compute(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] outLPU, ref float[] outLPV)
	{
		Buffers.ArraySetAllVB(ref outLPU, -9999f, mps.InputCount);
		Buffers.ArraySetAllVB(ref outLPV, -9999f, mps.InputCount);
		float[] depths = null;
		_depthRenderer.Compute(profile, mps, terrain, ref depths);
		List<string> datasets = null;
		List<string> datasets2 = null;
		List<string> datasets3 = null;
		int row = default;
		_lpUNames.Get2D(profile, ref datasets2, ref row);
		int row2 = default;
		_lpVNames.Get2D(profile, ref datasets3, ref row2);
		int row3 = default;
		_wsNames.Get2D(profile, ref datasets, ref row3);
		if (!datasets2.IsNullOrEmpty() && !datasets3.IsNullOrEmpty() && !datasets.IsNullOrEmpty() && mps.HasMapped2DCells())
		{
			float[][] meshFaceLaplacianU = ReadMappedMeshProfile(datasets2, row, _rowCache, mps);
			float[][] meshFaceLaplacianV = ReadMappedMeshProfile(datasets3, row2, _rowCache, mps);
			float[][] meshCellWS = ReadMappedMeshProfile(datasets, row3, _rowCache, mps);
			ComputeVelocity2D(meshFaceLaplacianU, meshFaceLaplacianV, meshCellWS, mps, depths, outLPU, outLPV);
		}
	}

	protected void ComputeVelocity2D(float[][] meshFaceLaplacianU, float[][] meshFaceLaplacianV, float[][] meshCellWS, RASGeometryMapPoints mps, float[] clipPixelDepths, float[] outLaplacianU, float[] outLaplacianV)
	{
		foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
		{
			int key = joinedMappedMeshe.Key;
			_ = joinedMappedMeshe.Value.Item1;
			SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
			float[] clipCellWSEL = meshCellWS[key];
			float[] faceScalarValues = meshFaceLaplacianU[key];
			float[] faceScalarValues2 = meshFaceLaplacianV[key];
			float[] fpValues = AverageFaceScalarsToFacepointValuesSA(_d2FlowArea.Meshes().ElementAtOrDefault(slopingMeshMap.MeshIndex), faceScalarValues, slopingMeshMap.UniqueFacePoints);
			float[] fpValues2 = AverageFaceScalarsToFacepointValuesSA(_d2FlowArea.Meshes().ElementAtOrDefault(slopingMeshMap.MeshIndex), faceScalarValues2, slopingMeshMap.UniqueFacePoints);
			ComputeFromFacepointValues(slopingMeshMap, fpValues, outLaplacianU, clipPixelDepths, clipCellWSEL);
			ComputeFromFacepointValues(slopingMeshMap, fpValues2, outLaplacianV, clipPixelDepths, clipCellWSEL);
		}
	}
}
