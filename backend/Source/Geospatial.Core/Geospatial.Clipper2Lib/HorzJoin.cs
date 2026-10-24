namespace Geospatial.Clipper2Lib;

internal class HorzJoin
{
	public OutPt? op1;

	public OutPt? op2;

	public HorzJoin(OutPt ltor, OutPt rtol)
	{
		op1 = ltor;
		op2 = rtol;
	}
}
