using System.Diagnostics;
using Ras.Hydraulics.Culverts;
using Utility.Logging;

namespace Ras.Layers;

[DebuggerDisplay("{Count} Opening Properties")]
public class OpeningPropertiesLayer : GeometryLayerBase<OpeningProperties>
{
	public const string HDF5GroupName = "/Geometry/Culverts/Opening Types";

	protected override string HDF5Group => "/Geometry/Culverts/Opening Types";

	public OpeningPropertiesLayer(Geometry geom)
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

	public OpeningProperties TryFindByName(string name)
	{
		if (Count == 0)
		{
			return null;
		}
		for (int i = 0; i < Count; i++)
		{
			OpeningProperties openingProperties = Features[i];
			if (openingProperties.Name == name)
			{
				return openingProperties;
			}
		}
		return null;
	}
}
