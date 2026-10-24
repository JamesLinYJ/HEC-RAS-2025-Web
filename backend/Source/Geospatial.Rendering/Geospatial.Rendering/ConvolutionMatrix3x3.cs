namespace Geospatial.Rendering;

public class ConvolutionMatrix3x3
{
	public int TopLeft;

	public int TopMid;

	public int TopRight;

	public int MidLeft;

	public int Pixel = 1;

	public int MidRight;

	public int BotLeft;

	public int BotMid;

	public int BotRight;

	public int Factor = 1;

	public int Offset;

	public ConvolutionMatrix3x3()
	{
		SetAll(1);
	}

	public ConvolutionMatrix3x3(int setAll)
	{
		SetAll(setAll);
	}

	public ConvolutionMatrix3x3(int tL, int tM, int tR, int mL, int mM, int mR, int bL, int bM, int bR)
	{
		TopLeft = tL;
		TopMid = tM;
		TopRight = tR;
		MidLeft = mL;
		Pixel = mM;
		MidRight = mR;
		BotLeft = bL;
		BotMid = bM;
		BotRight = bR;
	}

	public void SetAll(int nVal)
	{
		TopLeft = (TopMid = (TopRight = (MidLeft = (Pixel = (MidRight = (BotLeft = (BotMid = (BotRight = nVal))))))));
	}

	public static ConvolutionMatrix3x3 Gaussian()
	{
		return new ConvolutionMatrix3x3(1, 2, 1, 2, 4, 2, 1, 2, 1)
		{
			Factor = 16,
			Offset = 0
		};
	}
}
