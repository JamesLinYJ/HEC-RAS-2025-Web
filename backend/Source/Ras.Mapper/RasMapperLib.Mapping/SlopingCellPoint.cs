using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[ComVisible(false)]
public struct SlopingCellPoint
{
	public int Index;

	public float[] FPPrevWeights;

	public float[] VelocityWeights;

	public SlopingCellPoint(int index, float[] fpWeights, float[] velocityWeights = null)
	{
		this = default;
		Index = index;
		FPPrevWeights = fpWeights;
		VelocityWeights = velocityWeights;
	}
}
