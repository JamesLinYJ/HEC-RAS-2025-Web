namespace Geospatial.PairedData;

public struct TimeSeriesPoint(double t, double pos, double vel)
{
	public double Time = t;

	public double Position = pos;

	public double Velocity = vel;
}
