using System;
using System.Linq;
using System.Numerics;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers.BoundaryConditions;
using Utility.Logging;
using Utility.Units;

namespace Ras.Layers;

public class WindLayer : SpatialVectorBoundaryConditionBase
{
	public const string DefaultHDFGroupName = "/Boundary Conditions/Meteorology/Wind";

	public override string LayerTypeName => "Wind";

	public WindLayer(BoundaryCondition bc)
		: base(bc, "/Boundary Conditions/Meteorology/Wind")
	{
	}

	public override OperationResult PrepareForCompute()
	{
		return base.PrepareForCompute();
	}

	public override bool IsReallyEnabledForCompute()
	{
		if (!IsEnabled)
		{
			return false;
		}
		if (SpatialDataType == SpatialDataType.Constant && ConstantVx == 0f && ConstantVy == 0f)
		{
			return false;
		}
		return true;
	}

	public override OperationResult InitializeComputeDriver(ComputeControl computeControl, BoundaryConditionCollection bcs)
	{
		if (!IsReallyEnabledForCompute())
		{
			return true;
		}
		Result result = BoundaryCondition.Result;
		Mesh mesh = result.Geometry.Mesh;
		if (mesh == null)
		{
			throw new Exception("No mesh found.");
		}
		if (result == null)
		{
			throw new Exception("Cannot initialize solver without a Plan.");
		}
		Plan plan = result.Plan;
		BoundaryCondition.LoadBestAvailableProjectMetadata(out var _, out var _, out var units);
		if (SpatialDataType == SpatialDataType.Constant)
		{
			Vector2[] constBuf = new Vector2[1]
			{
				new Vector2(ConstantVx, ConstantVy)
			};
			bcs.SetWind(new DateTime[2] { BoundaryCondition.StartTime, BoundaryCondition.EndTime }, plan.StartTime, (int i) => constBuf, GetAirDensity, 1f, JaggedWeights<float>.ConstantField(mesh.FaceCount), isLagrangian: false, isWindStress: true);
			return true;
		}
		if (SpatialDataType == SpatialDataType.Point)
		{
			_ = Resampler;
			return false;
		}
		if (SpatialDataType == SpatialDataType.Gridded)
		{
			OperationResult nativeGriddedRaster = GetNativeGriddedRaster(out var nativeData, out var projectToNativeTransformer);
			if (!nativeGriddedRaster.Result)
			{
				return nativeGriddedRaster;
			}
			Extent ext = mesh.Extent.RatioAroundCenter(1.05);
			Point[] cellCenters = mesh.CellCenters;
			if (projectToNativeTransformer != null)
			{
				ext = ext.Transform(projectToNativeTransformer);
				cellCenters = projectToNativeTransformer.Transform(cellCenters);
			}
			ITimeSeriesVelocityRasterizer<float> clipped = nativeData.AsClipped(nativeData.RasterDefinition.GetPixelRange(ext));
			bcs.SetWind(nativeData.Times.ToArray(), plan.StartTime, (int i) => clipped.GetBandAsVec2(i), GetAirDensity, 1f, clipped.RasterDefinition.GetBilinearInterpolationWeights<float>(cellCenters), isLagrangian: false, isWindStress: true);
			return true;
		}
		throw new NotSupportedException();
		float GetAirDensity(int fIdx)
		{
			return (float)((units == UnitSystem.SI) ? 1.225 : 0.0765);
		}
	}
}
