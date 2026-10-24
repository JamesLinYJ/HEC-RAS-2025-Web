using System.Diagnostics;
using Utility.Logging;

namespace Ras.Layers.HydraulicLayers;

[DebuggerDisplay("{Count} Structure Properties")]
public class StructurePropertiesLayer : GeometryLayerBase<StructureProperties>
{
	public const string HDF5GroupName = "/Geometry/Structures/Structure Types";

	protected override string HDF5Group => "/Geometry/Structures/Structure Types";

	public StructurePropertiesLayer(Geometry geom)
		: base(geom)
	{
	}

	public OperationResult CanCompute()
	{
		OperationResult operationResult = true;
		for (int i = 0; i < Features.Count; i++)
		{
			bool isValid = Features[i].IsValid();
			if (!isValid)
			{
				operationResult.AddFailure(isValid);
			}
		}
		return operationResult;
	}

	public StructureProperties TryFindByName(string name)
	{
		if (Count == 0)
		{
			return null;
		}
		for (int i = 0; i < Count; i++)
		{
			StructureProperties structureProperties = Features[i];
			if (structureProperties.Name == name)
			{
				return structureProperties;
			}
		}
		return null;
	}
}
