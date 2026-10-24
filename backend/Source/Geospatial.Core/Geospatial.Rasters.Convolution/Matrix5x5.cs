namespace Geospatial.Rasters.Convolution;

public struct Matrix5x5(int wTL, int tL, int tM, int tR, int wTR, int wTML, int tmL, int tmM, int tmR, int wTMR, int wML, int mL, int mM, int mR, int wMR, int wMBL, int mbL, int mbM, int mbR, int wmbR, int wBL, int bL, int bM, int bR, int wBR)
{
	public int WayTopLeft = wTL;

	public int TopLeft = tL;

	public int TopMid = tM;

	public int TopRight = tR;

	public int WayTopRight = wTR;

	public int WayTMLeft = wTML;

	public int TMLeft = tmL;

	public int TMMid = tmM;

	public int TMRight = tmR;

	public int WayTMRight = wTMR;

	public int WayMidLeft = wML;

	public int MidLeft = mL;

	public int MidMid = mM;

	public int MidRight = mR;

	public int WayMidRight = wMR;

	public int WayMBLeft = wMBL;

	public int MBLeft = mbL;

	public int MBMid = mbM;

	public int MBRight = mbR;

	public int WayMBRight = wmbR;

	public int WayBotLeft = wBL;

	public int BotLeft = bL;

	public int BotMid = bM;

	public int BotRight = bR;

	public int WayBotRight = wBR;

	public int Factor = 1;

	public int Offset = 0;

	public void SetAll(int nVal)
	{
		WayTopLeft = (TopLeft = (TopMid = (TopRight = (WayTopRight = (WayTMLeft = (TMLeft = (TMMid = (TMRight = (WayTMRight = (WayMidLeft = (MidLeft = (MidMid = (MidRight = (WayMidRight = (WayMBLeft = (MBLeft = (MBMid = (MBRight = (WayMBRight = (WayBotLeft = (BotLeft = (BotMid = (BotRight = (WayBotRight = nVal))))))))))))))))))))))));
	}

	public static Matrix5x5 Gaussian()
	{
		return new Matrix5x5(1, 4, 7, 4, 1, 4, 16, 26, 16, 4, 7, 26, 41, 26, 7, 4, 16, 26, 16, 4, 1, 4, 7, 4, 1);
	}
}
