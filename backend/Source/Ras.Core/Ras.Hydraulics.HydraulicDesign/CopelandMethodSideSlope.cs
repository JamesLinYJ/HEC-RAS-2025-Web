using Ras.Layers;
using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class CopelandMethodSideSlope
{
	[Metadata("Value")]
	public double Value { get; set; }

	[Metadata("Equation")]
	public BankEquation Equation { get; set; }

	[Metadata("Roughness")]
	public double Roughness { get; set; }
}
