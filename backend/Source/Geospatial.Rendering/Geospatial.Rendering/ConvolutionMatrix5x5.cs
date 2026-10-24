namespace Geospatial.Rendering;

public class ConvolutionMatrix5x5
{
	public int WayTopLeft;

	public int TopLeft;

	public int TopMid;

	public int TopRight;

	public int WayTopRight;

	public int WayTMLeft;

	public int TMLeft;

	public int TMMid;

	public int TMRight;

	public int WayTMRight;

	public int WayMidLeft;

	public int MidLeft;

	public int MidMid;

	public int MidRight;

	public int WayMidRight;

	public int WayMBLeft;

	public int MBLeft;

	public int MBMid;

	public int MBRight;

	public int WayMBRight;

	public int WayBotLeft;

	public int BotLeft;

	public int BotMid;

	public int BotRight;

	public int WayBotRight;

	public int Factor = 1;

	public int Offset;

	public ConvolutionMatrix5x5()
	{
		SetAll(1);
	}

	public ConvolutionMatrix5x5(int wTL, int tL, int tM, int tR, int wTR, int wTML, int tmL, int tmM, int tmR, int wTMR, int wML, int mL, int mM, int mR, int wMR, int wMBL, int mbL, int mbM, int mbR, int wmbR, int wBL, int bL, int bM, int bR, int wBR)
	{
		WayTopLeft = wTL;
		TopLeft = tL;
		TopMid = tM;
		TopRight = tR;
		WayTopRight = wTR;
		WayTMLeft = wTML;
		TMLeft = tmL;
		TMMid = tmM;
		TMRight = tmR;
		WayTMRight = wTMR;
		WayMidLeft = wML;
		MidLeft = mL;
		MidMid = mM;
		MidRight = mR;
		WayMidRight = wMR;
		WayMBLeft = wMBL;
		MBLeft = mbL;
		MBMid = mbM;
		MBRight = mbR;
		WayMBRight = wmbR;
		WayBotLeft = wBL;
		BotLeft = bL;
		BotMid = bM;
		BotRight = bR;
		WayBotRight = wBR;
	}

	public void SetAll(int nVal)
	{
		WayTopLeft = (TopLeft = (TopMid = (TopRight = (WayTopRight = (WayTMLeft = (TMLeft = (TMMid = (TMRight = (WayTMRight = (WayMidLeft = (MidLeft = (MidMid = (MidRight = (WayMidRight = (WayMBLeft = (MBLeft = (MBMid = (MBRight = (WayMBRight = (WayBotLeft = (BotLeft = (BotMid = (BotRight = (WayBotRight = nVal))))))))))))))))))))))));
	}

	public static ConvolutionMatrix5x5 Gaussian()
	{
		return new ConvolutionMatrix5x5(1, 4, 7, 4, 1, 4, 16, 26, 16, 4, 7, 26, 41, 26, 7, 4, 16, 26, 16, 4, 1, 4, 7, 4, 1);
	}
}
