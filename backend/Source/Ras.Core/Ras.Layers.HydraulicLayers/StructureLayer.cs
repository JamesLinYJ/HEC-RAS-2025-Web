using System.Collections.Generic;
using System.Diagnostics;
using Utility.Logging;

namespace Ras.Layers.HydraulicLayers;

[DebuggerDisplay("{Count} Structure")]
public class StructureLayer : GeometryLayerBase<Structure>
{
	public const string StructureBaseGroup = "/Geometry/Structures";

	public const string HDF5GroupName = "/Geometry/Structures/Structures";

	protected override string HDF5Group => "/Geometry/Structures/Structures";

	public StructureLayer(Geometry geom)
		: base(geom)
	{
	}

	public override void Add(Structure localValue)
	{
		base.Add(localValue);
		localValue.Geometry = Geometry;
	}

	protected override List<Structure> LoadFeaturesInternal()
	{
		List<Structure> loadFeaturesInternal = base.LoadFeaturesInternal();
		for (int i = 0; i < loadFeaturesInternal.Count; i++)
		{
			loadFeaturesInternal[i].Geometry = Geometry;
		}
		return loadFeaturesInternal;
	}

	public OperationResult IsReadyToSolve()
	{
		if (Count == 0)
		{
			return true;
		}
		OperationResult operationResult = new OperationResult(success: true);
		for (int i = 0; i < Count; i++)
		{
			OperationResult operationResult2 = Features[i].IsValid();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}

	public OperationResult CanCompute()
	{
		OperationResult operationResult = true;
		foreach (Structure feature in Features)
		{
			OperationResult operationResult2 = feature.IsValid();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}

	public OperationResult PrepareForCompute()
	{
		OperationResult operationResult = IsReadyToSolve();
		if (!operationResult)
		{
			return operationResult;
		}
		return true;
	}
}
