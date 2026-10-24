using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Breaklines;

public class PointGenerationProperties : IDataHash
{
	[HashContributor]
	[Metadata("Cell Spacing Near")]
	public float CellSpacingNear { get; set; }

	[HashContributor]
	[Metadata("Cell Spacing Far")]
	public float CellSpacingFar { get; set; }

	[HashContributor]
	[Metadata("Near Repeats")]
	public int NearRepeats { get; set; }

	[HashContributor]
	[Metadata("Protection Radius")]
	public bool EnforceOneCellProtectionRadius { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
