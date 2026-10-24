namespace Geospatial.Rasters.Convolution;

public struct ScaledMatrix3x3F(float tL, float tM, float tR, float mL, float mM, float mR, float bL, float bM, float bR)
{
	public float TopLeft = tL;

	public float TopMid = tM;

	public float TopRight = tR;

	public float MidLeft = mL;

	public float Pixel = mM;

	public float MidRight = mR;

	public float BotLeft = bL;

	public float BotMid = bM;

	public float BotRight = bR;

	public float Factor = 1f;

	public float Offset = 0f;

	public void SetAll(float nVal)
	{
		TopLeft = (TopMid = (TopRight = (MidLeft = (Pixel = (MidRight = (BotLeft = (BotMid = (BotRight = nVal))))))));
	}

	public static ScaledMatrix3x3F Gaussian()
	{
		return new ScaledMatrix3x3F(1f, 2f, 1f, 2f, 4f, 2f, 1f, 2f, 1f);
	}
}
