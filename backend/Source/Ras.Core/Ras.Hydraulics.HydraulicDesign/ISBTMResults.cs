namespace Ras.Hydraulics.HydraulicDesign;

public class ISBTMResults
{
	public double UStar { get; set; }

	public double[] SurfaceCdf { get; set; }

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

	public int MaxIterations { get; set; }

	public int Iterations { get; set; }

	public double Tolerance { get; set; }

	public double Dres { get; set; }

	public double MaxFres { get; set; }

	public double Fsres { get; set; }

	public double Qres { get; set; }

	public double Ures { get; set; }
}
