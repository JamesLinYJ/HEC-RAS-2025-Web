namespace Ras.Engine.Structures.Culverts;

public class CulvertInletCoeff
{
	public byte EqnForm { get; set; }

	public double K { get; set; }

	public double M { get; set; }

	public double C { get; set; }

	public double Y { get; set; }

	public double SlopeCorrection { get; set; }

	public bool UseEqn26 => EqnForm == 1;

	public CulvertInletCoeff()
	{
	}

	public CulvertInletCoeff(CulvertInletCoeff other)
	{
		EqnForm = other.EqnForm;
		K = other.K;
		M = other.M;
		C = other.C;
		Y = other.Y;
		SlopeCorrection = other.SlopeCorrection;
	}

	public CulvertInletCoeff Clone()
	{
		return new CulvertInletCoeff(this);
	}
}
