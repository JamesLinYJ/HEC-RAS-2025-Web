using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.DataStructures;

public static class GeometryTools
{
	public static double EuclideanDistance(Point pt1, Point pt2)
	{
		return Math.Sqrt(Math.Pow(pt1.X - pt2.X, 2.0) + Math.Pow(pt1.Y - pt2.Y, 2.0));
	}

	public static bool CheckIfSquareIsCompletelyInsideCircle(Point circleCenter, double radius, Point squareCenter, double squareSideLen)
	{
		double squareCenterSquareSideLenTerm = squareCenter.X + squareSideLen / 2.0;
		double squareCenterSquareSideLenTerm2 = squareCenter.Y - squareSideLen / 2.0;
		double squareCenterSquareSideLenTerm3 = squareCenter.X - squareSideLen / 2.0;
		double squareCenterSquareSideLenTerm4 = squareCenter.Y + squareSideLen / 2.0;
		double circleCenterTerm = Math.Max(circleCenter.X - squareCenterSquareSideLenTerm3, squareCenterSquareSideLenTerm - circleCenter.X);
		double circleCenterTerm2 = Math.Max(squareCenterSquareSideLenTerm4 - circleCenter.Y, circleCenter.Y - squareCenterSquareSideLenTerm2);
		return radius * radius >= circleCenterTerm * circleCenterTerm + circleCenterTerm2 * circleCenterTerm2;
	}

	public static bool CheckIfSquareAndCircleInteresct(Point circleCenter, double circleRadius, Point squareCenter, double squareSideLen)
	{
		double circleCenterSquareCenterTerm = Math.Abs(circleCenter.X - squareCenter.X);
		double circleCenterSquareCenterTerm2 = Math.Abs(circleCenter.Y - squareCenter.Y);
		if (circleCenterSquareCenterTerm > squareSideLen / 2.0 + circleRadius)
		{
			return false;
		}
		if (circleCenterSquareCenterTerm2 > squareSideLen / 2.0 + circleRadius)
		{
			return false;
		}
		if (circleCenterSquareCenterTerm <= squareSideLen / 2.0)
		{
			return true;
		}
		if (circleCenterSquareCenterTerm2 <= squareSideLen / 2.0)
		{
			return true;
		}
		return Math.Pow(circleCenterSquareCenterTerm - squareSideLen / 2.0, 2.0) + Math.Pow(circleCenterSquareCenterTerm2 - squareSideLen / 2.0, 2.0) <= Math.Pow(circleRadius, 2.0);
	}

	public static List<Point> GetPointsOnLine(double x0, double y0, double x1, double y1)
	{
		List<Point> points = new List<Point>();
		bool y1Y0Term = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
		if (y1Y0Term)
		{
			double x02 = x0;
			x0 = y0;
			y0 = x02;
			double x12 = x1;
			x1 = y1;
			y1 = x12;
		}
		if (x0 > x1)
		{
			double x03 = x0;
			x0 = x1;
			x1 = x03;
			double y02 = y0;
			y0 = y1;
			y1 = y02;
		}
		double x1AboveX0 = x1 - x0;
		double absoluteY1AboveY0 = Math.Abs(y1 - y0);
		double computed = x1AboveX0 / 2.0;
		double y0Y1Term = ((y0 < y1) ? 1 : (-1));
		double y03 = y0;
		for (double x04 = x0; x04 <= x1; x04++)
		{
			points.Add(new Point(y1Y0Term ? y03 : x04, y1Y0Term ? x04 : y03));
			computed -= absoluteY1AboveY0;
			if (computed < 0.0)
			{
				y03 += y0Y1Term;
				computed += x1AboveX0;
			}
		}
		return points;
	}

	public static double GetAreaOfIntersectingRectangles(double xBottomLeftRect1, double yBottomLeftRect1, double xTopRightRect1, double yTopRightRect1, double xBottomLeftRect2, double yBottomLeftRect2, double xTopRightRect2, double yTopRightRect2)
	{
		double xBottomLeftRect1XBottomLeftRect2Term = Math.Max(xBottomLeftRect1, xBottomLeftRect2);
		double xTopRightRect1XTopRightRect2Term = Math.Min(xTopRightRect1, xTopRightRect2);
		double yBottomLeftRect1YBottomLeftRect2Term = Math.Max(yBottomLeftRect1, yBottomLeftRect2);
		double yTopRightRect1YTopRightRect2Term = Math.Min(yTopRightRect1, yTopRightRect2);
		if (xBottomLeftRect1XBottomLeftRect2Term < xTopRightRect1XTopRightRect2Term && yBottomLeftRect1YBottomLeftRect2Term < yTopRightRect1YTopRightRect2Term)
		{
			return (xTopRightRect1XTopRightRect2Term - xBottomLeftRect1XBottomLeftRect2Term) * (yTopRightRect1YTopRightRect2Term - yBottomLeftRect1YBottomLeftRect2Term);
		}
		return 0.0;
	}
}
