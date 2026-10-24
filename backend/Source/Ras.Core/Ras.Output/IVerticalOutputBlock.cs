using System;
using Geospatial;

namespace Ras.Output;

public interface IVerticalOutputBlock : IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps
{
	bool TryReadMeshValues(OutputVariable variable, int meshIndex, Memory<int> elementIndexes, Memory<float>[] values);
}
