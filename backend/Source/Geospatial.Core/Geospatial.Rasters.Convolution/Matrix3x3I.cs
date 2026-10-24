namespace Geospatial.Rasters.Convolution;

public struct Matrix3x3I(int tL, int tM, int tR, int mL, int mM, int mR, int bL, int bM, int bR)
{
	public int TopLeft = tL;

	public int TopMid = tM;

	public int TopRight = tR;

	public int MidLeft = mL;

	public int Pixel = mM;

	public int MidRight = mR;

	public int BotLeft = bL;

	public int BotMid = bM;

	public int BotRight = bR;

	public int Factor = 1;

	public int Offset = 0;

	public void SetAll(int nVal)
	{
		TopLeft = (TopMid = (TopRight = (MidLeft = (Pixel = (MidRight = (BotLeft = (BotMid = (BotRight = nVal))))))));
	}

	public static Matrix3x3I Gaussian()
	{
		return new Matrix3x3I(1, 2, 1, 2, 4, 2, 1, 2, 1);
	}
}
