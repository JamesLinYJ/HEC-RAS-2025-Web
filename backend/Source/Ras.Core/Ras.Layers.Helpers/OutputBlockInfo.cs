using Ras.Output;

namespace Ras.Layers.Helpers;

public class OutputBlockInfo
{
	public string Name { get; set; }

	public int ProfileCount { get; set; }

	public OutputBlockInfo()
	{
	}

	public OutputBlockInfo(OutputBlock ob)
	{
		Name = ob.Name;
		ProfileCount = ob.ProfileCount;
	}
}
