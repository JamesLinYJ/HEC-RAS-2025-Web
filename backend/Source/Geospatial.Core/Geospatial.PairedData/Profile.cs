namespace Geospatial.PairedData;

public class Profile
{
	public double[] X;

	public double[] Y;

	public int PointCount => X.Length;

	public static Profile FromBuffers(double[] x, double[] y)
	{
		return new Profile
		{
			X = x,
			Y = y
		};
	}
}
