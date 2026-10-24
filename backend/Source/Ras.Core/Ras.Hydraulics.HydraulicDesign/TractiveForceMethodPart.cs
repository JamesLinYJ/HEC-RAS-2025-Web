using Ras.Layers;
using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class TractiveForceMethodPart
{
	[Metadata("d")]
	public double D { get; set; }

	[Metadata("Angle of Repose")]
	public double AngleOfRepose { get; set; }

	[Metadata("Roughness")]
	public double Roughness { get; set; }

	[Metadata("Specific Gravity")]
	public double SpecificGravity { get; set; }

	[Metadata("Shield's Parameter")]
	public double ShieldsParameter { get; set; }

	[Metadata("Equation")]
	public BankEquation Equation { get; set; }
}
