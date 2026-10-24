namespace Geospatial.Vectors;

public struct GradientUVXY
{
	public static GradientUVXY Zero => new GradientUVXY(0.0, 0.0, 0.0, 0.0);

	public double DU_DX { get; }

	public double DU_DY { get; }

	public double DV_DX { get; }

	public double DV_DY { get; }

	public GradientUVXY(double dudx, double dudy, double dvdx, double dvdy)
	{
		DU_DX = dudx;
		DU_DY = dudy;
		DV_DX = dvdx;
		DV_DY = dvdy;
	}
}
