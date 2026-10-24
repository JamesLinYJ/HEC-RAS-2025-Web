namespace Geospatial.Vectors;

public struct FaceUVAB
{
	public double UA;

	public double VA;

	public double UB;

	public double VB;

	public double NormalA => UA;

	public double TangentA => VA;

	public double NormalB => UB;

	public double TangentB => VB;

	public FaceUVAB(double ua, double va, double ub, double vb)
	{
		UA = ua;
		VA = va;
		UB = ub;
		VB = vb;
	}
}
