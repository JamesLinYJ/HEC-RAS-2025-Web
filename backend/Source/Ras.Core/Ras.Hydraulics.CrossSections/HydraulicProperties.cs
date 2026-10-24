using System.Diagnostics;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.CrossSections;

[DebuggerDisplay("{VerticalSliceCount} Points, {LOBSliceCount}/{ChannelSliceCount}/{ROBSliceCount}")]
public class HydraulicProperties : IDataHash
{
	[HashContributor]
	[Metadata("HP Count")]
	public int VerticalSliceCount { get; set; }

	[HashContributor]
	[Metadata("HP Start Elev")]
	public double StartElevation { get; set; }

	[HashContributor]
	[Metadata("HP Vert Incr")]
	public double VerticalIncrement { get; set; }

	[HashContributor]
	[Metadata("HP LOB Slices")]
	public int LOBSliceCount { get; set; }

	[HashContributor]
	[Metadata("HP Chan Slices")]
	public int ChannelSliceCount { get; set; }

	[HashContributor]
	[Metadata("HP ROB Slices")]
	public int ROBSliceCount { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public bool Matches(HydraulicProperties other)
	{
		if (VerticalSliceCount == other.VerticalSliceCount && StartElevation == other.StartElevation && VerticalIncrement == other.VerticalIncrement && LOBSliceCount == other.LOBSliceCount && ChannelSliceCount == other.ChannelSliceCount)
		{
			return ROBSliceCount == other.ROBSliceCount;
		}
		return false;
	}
}
