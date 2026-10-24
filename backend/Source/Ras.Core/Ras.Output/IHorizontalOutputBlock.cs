using System;
using Geospatial;

namespace Ras.Output;

public interface IHorizontalOutputBlock : IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps
{
	bool TryReadPerMesh(OutputVariable variable, int profile, Memory<float>[] values);

	bool TryReadSingleMesh(OutputVariable variable, int profile, int meshIdx, Memory<float> values)
	{
		Memory<float>[] floatSpanArray = new Memory<float>[Geometry.FlowAreaLayer.Count];
		floatSpanArray[meshIdx] = values;
		return TryReadPerMesh(variable, profile, floatSpanArray);
	}
}
