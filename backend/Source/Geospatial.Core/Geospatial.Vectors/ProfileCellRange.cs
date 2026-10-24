namespace Geospatial.Vectors;

public struct ProfileCellRange(int c, double sStart, double sEnd)
{
	public int Cell = c;

	public double StationStart = sStart;

	public double StationEnd = sEnd;

	public static int CompareStationStart(ProfileCellRange a, ProfileCellRange b)
	{
		if (a.StationStart < b.StationStart)
		{
			return -1;
		}
		if (a.StationStart == b.StationStart)
		{
			return 0;
		}
		return 1;
	}
}
