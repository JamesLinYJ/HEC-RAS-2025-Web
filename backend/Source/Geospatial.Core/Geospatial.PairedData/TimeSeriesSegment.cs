using Utility;

namespace Geospatial.PairedData;

public struct TimeSeriesSegment(TimeSeriesPoint aPt, TimeSeriesPoint bPt)
{
	public TimeSeriesPoint A = aPt;

	public TimeSeriesPoint B = bPt;

	public double Acceleration => (B.Velocity - A.Velocity) / (B.Time - A.Time);

	public void TimeAtPosition(double yValue, out double time1, out double time2)
	{
		double acceleration = Acceleration;
		double yValueAbovePosition = yValue - A.Position;
		MathHelpers.Quadratic(0.5 * acceleration, A.Velocity, 0.0 - yValueAbovePosition, out var x, out var x2);
		time1 = x + A.Time;
		time2 = x2 + A.Time;
	}
}
