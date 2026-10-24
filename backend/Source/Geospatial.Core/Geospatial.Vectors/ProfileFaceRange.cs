namespace Geospatial.Vectors;

public struct ProfileFaceRange(int f, int fpStart, int fpEnd, double sStart, double sEnd)
{
	public int Face = f;

	public int FPStartIdx = fpStart;

	public int FPEndIdx = fpEnd;

	public double StationStart = sStart;

	public double StationEnd = sEnd;
}
