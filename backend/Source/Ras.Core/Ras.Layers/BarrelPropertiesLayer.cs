using System.Diagnostics;
using Ras.Hydraulics.Culverts;
using Utility.Logging;

namespace Ras.Layers;

[DebuggerDisplay("{Count} Barrel Properties")]
public class BarrelPropertiesLayer : GeometryLayerBase<BarrelProperties>
{
	public const string HDF5GroupName = "/Geometry/Culverts/Barrel Types";

	protected override string HDF5Group => "/Geometry/Culverts/Barrel Types";

	public BarrelPropertiesLayer(Geometry geom)
		: base(geom)
	{
	}

	public OperationResult CanCompute()
	{
		OperationResult operationResult = true;
		for (int i = 0; i < Features.Count; i++)
		{
			OperationResult operationResult2 = Features[i].IsValid();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}

	public BarrelProperties TryFindByName(string name)
	{
		if (Count == 0)
		{
			return null;
		}
		for (int i = 0; i < Count; i++)
		{
			BarrelProperties barrelProperties = Features[i];
			if (barrelProperties.Name == name)
			{
				return barrelProperties;
			}
		}
		return null;
	}
}
