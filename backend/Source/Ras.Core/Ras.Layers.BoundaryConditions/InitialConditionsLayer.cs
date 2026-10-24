using System;
using System.Collections.Generic;
using System.IO;
using Geospatial.IO;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Layers.BoundaryConditions;

public class InitialConditionsLayer : LayerBase, IBoundaryConditionLayer
{
	public const string HDFInitialConditionsGroup = "/Boundary Conditions/Initial Conditions";

	public const string HDFInitialConditionsDataset = "/Boundary Conditions/Initial Conditions/IC Raster (SHIM)";

	protected static readonly ILogger _logger = LogHelper.GetLogger("InitialConditionsLayer");

	private IResample<float> _initialWaterSurface;

	private IResample<float> _initialVelocity;

	private IResample<float> _initialVelocityX;

	private IResample<float> _initialVelocityY;

	protected override string HDF5Group => "/Boundary Conditions/Initial Conditions";

	public BoundaryCondition BoundaryCondition { get; set; }

	public InitialStageLayer Stage { get; }

	public InitialVelocityLayer Velocity { get; }

	public IResample<float> InitialWaterSurface
	{
		get
		{
			IResample<float> resample = _initialWaterSurface;
			if (resample == null)
			{
				InitialStageLayer stage = Stage;
				if (stage == null)
				{
					return null;
				}
				resample = stage.Surface;
			}
			return resample;
		}
		set
		{
			_initialWaterSurface = value;
		}
	}

	public IResample<float> InitialVelocity
	{
		get
		{
			return _initialVelocity;
		}
		set
		{
			_initialVelocity = value;
		}
	}

	public IResample<float> InitialVelocityX
	{
		get
		{
			IResample<float> resample = _initialVelocityX;
			if (resample == null)
			{
				InitialVelocityLayer velocity = Velocity;
				if (velocity == null)
				{
					return null;
				}
				resample = velocity.SurfaceVx;
			}
			return resample;
		}
		set
		{
			_initialVelocityX = value;
		}
	}

	public IResample<float> InitialVelocityY
	{
		get
		{
			IResample<float> resample = _initialVelocityY;
			if (resample == null)
			{
				InitialVelocityLayer velocity = Velocity;
				if (velocity == null)
				{
					return null;
				}
				resample = velocity.SurfaceVy;
			}
			return resample;
		}
		set
		{
			_initialVelocityY = value;
		}
	}

	public InitialConditionsLayer(BoundaryCondition bc)
	{
		BoundaryCondition = bc;
		Stage = new InitialStageLayer(bc);
		Velocity = new InitialVelocityLayer(bc);
	}

	public IEnumerable<IInitialConditionLayer> AllLayers()
	{
		yield return Stage;
		yield return Velocity;
	}

	public void SetExternalSource(string connectionName, string timestep)
	{
		Stage.SetExternalSource(connectionName, "", timestep);
		Velocity.SetExternalSource(connectionName, "", timestep);
	}

	public OperationResult SetRASResultExternalSource(string filename, string timestepSuggestion)
	{
		if (!Result.IsValid(filename))
		{
			return OperationResult.Fail("Not a valid RAS Result.");
		}
		Project project = BoundaryCondition?.Project;
		if (project == null)
		{
			return OperationResult.Fail("Does not belong to a project.");
		}
		string name = Path.GetFileNameWithoutExtension(filename) + "-" + Guid.NewGuid().ToString().Substring(0, 6);
		FileExternalConnection fileExternalConnection = project.ConnectionSource.AddFileConnection(name, filename);
		Stage.SetExternalSource(fileExternalConnection.Name, "", timestepSuggestion);
		Velocity.SetExternalSource(fileExternalConnection.Name, "", timestepSuggestion);
		return true;
	}

	public OperationResult PrepareForCompute(ProgressReporter pr = null)
	{
		OperationResult operationResult = Stage.IngestExternalSource(pr);
		OperationResult operationResult2 = Velocity.IngestExternalSource(pr);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		return operationResult;
	}

	protected override void LoadMetadataInternal(H5Reader hr)
	{
		base.LoadMetadataInternal(hr);
		Stage.Load(hr);
		Velocity.Load(hr);
		if (Stage.IsDefault())
		{
			_initialWaterSurface = H5RasterWriter.TryReadRaster<float>(hr, "/Boundary Conditions/Initial Conditions/IC Raster (SHIM)");
		}
	}

	public void Save(H5Writer hw)
	{
		GeospatialH5Metadata.Write(this, hw, HDF5Group);
		Stage.Save(hw);
		Velocity.Save(hw);
	}

	public void CopyDataToInternal(H5Writer hw)
	{
		Stage.CopyDataToInternal(hw);
		Velocity.CopyDataToInternal(hw);
	}

	public override bool NeedsSave()
	{
		return false;
	}
}
