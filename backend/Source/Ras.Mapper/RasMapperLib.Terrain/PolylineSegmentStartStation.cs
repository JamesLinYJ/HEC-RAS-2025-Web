namespace RasMapperLib.Terrain;

public struct PolylineSegmentStartStation
{
	public int PolylineIndex;

	public int SegmentIndex;

	public double SegmentStartingStation;

	public PolylineSegmentStartStation(int p, int s, double segStartStation)
	{
		this = default;
		PolylineIndex = p;
		SegmentIndex = s;
		SegmentStartingStation = segStartStation;
	}
}
