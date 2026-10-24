namespace Geospatial.Rasters.Convolution;

public struct Matrix3x3<T>(T tL, T tM, T tR, T mL, T mM, T mR, T bL, T bM, T bR) where T : struct
{
	public T TopLeft = tL;

	public T TopMid = tM;

	public T TopRight = tR;

	public T MidLeft = mL;

	public T Pixel = mM;

	public T MidRight = mR;

	public T BotLeft = bL;

	public T BotMid = bM;

	public T BotRight = bR;

	public void SetAll(T nVal)
	{
		TopLeft = (TopMid = (TopRight = (MidLeft = (Pixel = (MidRight = (BotLeft = (BotMid = (BotRight = nVal))))))));
	}
}
