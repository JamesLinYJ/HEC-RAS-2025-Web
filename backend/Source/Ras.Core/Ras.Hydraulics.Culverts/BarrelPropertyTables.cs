using Utility.Logging;

namespace Ras.Hydraulics.Culverts;

public class BarrelPropertyTables
{
	public int UpstreamCellIndex { get; set; }

	public int DownstreamCellIndex { get; set; }

	public OperationResult IsValid()
	{
		if (UpstreamCellIndex == -1)
		{
			return OperationResult.Fail("No upstream cell index found.");
		}
		if (DownstreamCellIndex == -1)
		{
			return OperationResult.Fail("No downstream cell index found.");
		}
		if (UpstreamCellIndex == DownstreamCellIndex)
		{
			return OperationResult.Fail("Upstream cell index matches downstream (barrel starts and ends in the same cell).");
		}
		return true;
	}
}
