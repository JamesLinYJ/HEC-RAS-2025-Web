using System;
using Utility.Hashing;

namespace Geospatial.PairedData;

public struct HydraulicProperties(double elev, double a, double tw, double wp, double centX) : IDataHash
{
	public enum AreaInterpolation
	{
		Linear,
		Trapezoidal
	}

	public double Elevation = elev;

	public double Area = a;

	public double TopWidth = tw;

	public double WettedPerimeter = wp;

	public double CentroidX = centX;

	public void Clear()
	{
		Elevation = 0.0;
		Area = 0.0;
		WettedPerimeter = 0.0;
		TopWidth = 0.0;
		CentroidX = 0.0;
	}

	public double HydraulicDepth()
	{
		if (TopWidth == 0.0)
		{
			return 0.0;
		}
		return Area / TopWidth;
	}

	public double HydraulicRadius()
	{
		if (WettedPerimeter == 0.0)
		{
			return 0.0;
		}
		return Area / WettedPerimeter;
	}

	public double Conveyance(double mannN)
	{
		return Area * Math.Pow(HydraulicRadius(), 2.0 / 3.0) / mannN;
	}

	public double ProjectedConveyance(double elevAbove, double mannN)
	{
		double elevAboveAboveElevation = elevAbove - Elevation;
		double areaTopWidthTerm = Area + TopWidth * elevAboveAboveElevation;
		double wettedPerimeterTerm = WettedPerimeter + elevAboveAboveElevation * 2.0;
		double x = areaTopWidthTerm / wettedPerimeterTerm;
		return areaTopWidthTerm * Math.Pow(x, 2.0 / 3.0) / mannN;
	}

	public HydraulicProperties MidPoint(HydraulicProperties hp, AreaInterpolation areaInter)
	{
		HydraulicProperties result = default;
		result.Elevation = (Elevation + hp.Elevation) * 0.5;
		result.TopWidth = (TopWidth + hp.TopWidth) * 0.5;
		result.WettedPerimeter = (WettedPerimeter + hp.WettedPerimeter) * 0.5;
		result.CentroidX = (CentroidX + hp.CentroidX) * 0.5;
		if (areaInter == AreaInterpolation.Linear)
		{
			result.Area = (Area + hp.Area) * 0.5;
		}
		else if (Elevation < hp.Elevation)
		{
			double elevationDifference = result.Elevation - Elevation;
			result.Area = Area + (TopWidth + result.TopWidth) * 0.5 * elevationDifference;
		}
		else
		{
			double elevationDifference2 = result.Elevation - hp.Elevation;
			result.Area = hp.Area + (hp.TopWidth + result.TopWidth) * 0.5 * elevationDifference2;
		}
		return result;
	}

	public int GetDataHash()
	{
		return StableHash.Combine(Elevation, Area, TopWidth, WettedPerimeter, CentroidX);
	}

	public override string ToString()
	{
		return "el=" + Elevation + " a=" + Area + " tw=" + TopWidth + " wp=" + WettedPerimeter + " cx=" + CentroidX;
	}
}
