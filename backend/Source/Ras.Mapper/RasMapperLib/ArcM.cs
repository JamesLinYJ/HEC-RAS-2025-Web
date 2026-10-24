using System;

namespace RasMapperLib;

public struct ArcM
{
	public PointM CenterPoint;

	public PointM StartPoint;

	public PointM EndPoint;

	public bool IsCCW;

	public double Radius;

	public double Angle;

	public double DeltaAngle;

	public static double AngleDiscritization = Math.PI / 72.0;

	public void Assign(PointM centerPt, PointM startPt, PointM endPt, bool ccw)
	{
		CenterPoint = centerPt;
		Radius = CenterPoint.DistanceTo(startPt);
		StartPoint = startPt;
		EndPoint = endPt;
		IsCCW = ccw;
	}

	public void GetPointMs(bool includeStartPtEndPt, PointMs pointMs)
	{
		SegmentM segmentM = default;
		segmentM.Assign(CenterPoint, StartPoint);
		double bearing = segmentM.Bearing();
		segmentM.Assign(CenterPoint, EndPoint);
		double bearing2 = segmentM.Bearing();
		if (IsCCW)
		{
			if (bearing2 < bearing)
			{
				bearing2 += Math.PI * 2.0;
			}
		}
		else if (bearing2 > bearing)
		{
			bearing2 -= Math.PI * 2.0;
		}
		Angle = bearing2 - bearing;
		checked
		{
			int angleAngleDiscritizationTerm = (int)Math.Ceiling(Math.Abs(Angle) / AngleDiscritization);
			DeltaAngle = Angle / (double)angleAngleDiscritizationTerm;
			if (includeStartPtEndPt)
			{
				pointMs.Add(StartPoint);
			}
			double bearing3 = bearing;
			int count = angleAngleDiscritizationTerm - 1;
			for (int i = 1; i <= count; i++)
			{
				bearing3 += DeltaAngle;
				pointMs.Add(CenterPoint.X + Radius * Math.Cos(bearing3), CenterPoint.Y + Radius * Math.Sin(bearing3));
			}
			if (includeStartPtEndPt)
			{
				pointMs.Add(EndPoint);
			}
		}
	}
}
