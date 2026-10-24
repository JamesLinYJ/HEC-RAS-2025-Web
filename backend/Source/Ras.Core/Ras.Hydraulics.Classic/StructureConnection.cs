using Utility.ORM;

namespace Ras.Hydraulics.Classic;

public class StructureConnection
{
	[Metadata("Type")]
	public StructureConnectionType Type { get; set; }

	[Metadata("River")]
	public string RiverName { get; set; }

	[Metadata("Reach")]
	public string ReachName { get; set; }

	[Metadata("RS")]
	public string RiverStation { get; set; }

	[Metadata("SA/2D")]
	public string ConnectedElementName { get; set; }
}
