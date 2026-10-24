namespace Ras.Hydraulics.HydraulicDesign;

public class SBTMResults
{
	public double TransportRate { get; set; }

	public double[] CumulativeBedloadDistribution { get; set; }

	public double SurfaceDMin { get; set; }

	public double SurfaceDMax { get; set; }

	public double Ds50 { get; set; }

	public double Dsm { get; set; }

	public double SSig { get; set; }

	public double SFs { get; set; }

	public double BedloadDMin { get; set; }

	public double BedloadDMax { get; set; }

	public double Db50 { get; set; }

	public double Dbm { get; set; }

	public double BSig { get; set; }

	public double BFs { get; set; }
}
