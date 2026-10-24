using System;

namespace Geospatial.GDALAssist;

public class GDALProgressTracker
{
	private char[] GDALRecentChars;

	private bool has10Happened;

	public GDALProgressTracker()
	{
		GDALRecentChars = new char[4];
		GDALRecentChars[0] = ' ';
		GDALRecentChars[1] = ' ';
		GDALRecentChars[2] = ' ';
		GDALRecentChars[3] = ' ';
	}

	public int? ParseGDALStdOut(char c)
	{
		GDALRecentChars[3] = GDALRecentChars[2];
		GDALRecentChars[2] = GDALRecentChars[1];
		GDALRecentChars[1] = GDALRecentChars[0];
		GDALRecentChars[0] = c;
		if (GDALRecentChars[3] == '.' && GDALRecentChars[2] == '.' && char.IsNumber(GDALRecentChars[1]) && char.IsNumber(GDALRecentChars[0]))
		{
			int ordinal = Convert.ToInt32(Convert.ToString(GDALRecentChars[1]) + Convert.ToString(GDALRecentChars[0]));
			if (ordinal > 19)
			{
				has10Happened = true;
			}
			if (ordinal == 10 && has10Happened)
			{
				ordinal = 100;
				has10Happened = false;
			}
			return ordinal;
		}
		return null;
	}
}
