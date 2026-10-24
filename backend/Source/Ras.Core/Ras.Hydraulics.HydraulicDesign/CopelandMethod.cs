using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class CopelandMethod
{
	[Metadata("Specific Gravity")]
	public double SpecificGravity { get; set; }

	[Metadata("d84")]
	public double D84 { get; set; }

	[Metadata("d50")]
	public double D50 { get; set; }

	[Metadata("d16")]
	public double D16 { get; set; }

	[Metadata("Temperature")]
	public double Temperature { get; set; }

	[Metadata("Valley Slope", Optional = true)]
	public double ValleySlope { get; set; }

	[Metadata("Median Channel Width", Optional = true)]
	public double MedianChannelWidth { get; set; }

	[ComplexMetadata("Stable Design")]
	public CopelandMethodStableDesign StableDesign { get; set; } = new CopelandMethodStableDesign();

	[ComplexMetadata("Stable Supply")]
	public CopelandMethodStableSupply StableSupply { get; set; } = new CopelandMethodStableSupply();
}
