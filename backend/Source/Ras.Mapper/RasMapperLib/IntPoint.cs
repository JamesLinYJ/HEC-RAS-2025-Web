using System;

namespace RasMapperLib;

public struct IntPoint
{
	public long X;

	public long Y;

	public IntPoint(int xx, int yy)
	{
		this = default;
		X = xx;
		Y = yy;
	}

	public IntPoint(long xx, long yy)
	{
		this = default;
		X = xx;
		Y = yy;
	}

	public IntPoint(double xx, double yy)
	{
		this = default;
		checked
		{
			X = (long)Math.Round(xx);
			Y = (long)Math.Round(yy);
		}
	}
}
