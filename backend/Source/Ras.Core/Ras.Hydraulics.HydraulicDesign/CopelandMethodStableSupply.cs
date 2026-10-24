using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class CopelandMethodStableSupply
{
	[Metadata("Sediment Concentration")]
	public double SedimentConcentration { get; set; }

	[Metadata("Channel Width")]
	public double ChannelWidth { get; set; }

	[Metadata("Channel Bank Height")]
	public double ChannelBankHeight { get; set; }

	[Metadata("Channel Slope")]
	public double ChannelSlope { get; set; }

	[ComplexMetadata("Left Side Slope")]
	public CopelandMethodSideSlope LeftSideSlope { get; set; } = new CopelandMethodSideSlope();

	[ComplexMetadata("Right Side Slope")]
	public CopelandMethodSideSlope RightSideSlope { get; set; } = new CopelandMethodSideSlope();
}
