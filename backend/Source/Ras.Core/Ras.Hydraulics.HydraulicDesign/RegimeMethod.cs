using Utility.ORM;

namespace Ras.Hydraulics.HydraulicDesign;

public class RegimeMethod
{
	[Metadata("d50")]
	public double D50 { get; set; }

	[Metadata("Discharge")]
	public double Discharge { get; set; }

	[Metadata("Sediment Concentration")]
	public double SedimentConcentration { get; set; }

	[Metadata("Side Factor")]
	public double SideFactor { get; set; }

	[Metadata("Temperature")]
	public double Temperature { get; set; }
}
