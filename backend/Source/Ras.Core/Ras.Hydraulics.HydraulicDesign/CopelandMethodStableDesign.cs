using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class CopelandMethodStableDesign
{
	[Metadata("Discharge")]
	public double Discharge { get; set; }

	[ComplexMetadata("Left Side Slope")]
	public CopelandMethodSideSlope LeftSideSlope { get; set; } = new CopelandMethodSideSlope();

	[ComplexMetadata("Right Side Slope")]
	public CopelandMethodSideSlope RightSideSlope { get; set; } = new CopelandMethodSideSlope();
}
