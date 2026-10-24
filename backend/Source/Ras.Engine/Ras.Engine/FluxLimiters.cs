using System;
using Geospatial.Vectors;

namespace Ras.Engine;

public static class FluxLimiters
{
	public static double MinMod(double r)
	{
		if (!(r > 0.0))
		{
			return 0.0;
		}
		return Math.Min(1.0, r);
	}

	public static double Superbee(double r)
	{
		if (!(r > 0.0))
		{
			return 0.0;
		}
		return Math.Max(Math.Min(2.0 * r, 1.0), Math.Min(r, 2.0));
	}

	public static double VanLeer(double r)
	{
		if (!(r > 0.0))
		{
			return 0.0;
		}
		return 2.0 - 2.0 / (1.0 + r);
	}

	public static double Limit(FluxLimiter limiter, double r)
	{
		return limiter switch
		{
			FluxLimiter.MinMod => MinMod(r), 
			FluxLimiter.Superbee => Superbee(r), 
			FluxLimiter.VanLeer => VanLeer(r), 
			_ => throw new ArgumentOutOfRangeException("limiter"), 
		};
	}

	public static double ConvectiveRatio(double gradUpwindDotDCD, double cUpwind, double cDownwind)
	{
		if (!double.IsFinite(gradUpwindDotDCD) || !double.IsFinite(cUpwind) || !double.IsFinite(cDownwind))
		{
			return 0.0;
		}
		double cDownwindAboveCUpwind = cDownwind - cUpwind;
		if (cDownwindAboveCUpwind == 0.0 || !double.IsFinite(cDownwindAboveCUpwind))
		{
			return 0.0;
		}
		double gradUpwindDotDCDTerm = 2.0 * (gradUpwindDotDCD / cDownwindAboveCUpwind) - 1.0;
		if (!double.IsNaN(gradUpwindDotDCDTerm))
		{
			return gradUpwindDotDCDTerm;
		}
		return 0.0;
	}

	public static double ConvectiveRatio(Vector gradUpwind, double dX, double dY, double cUpwind, double cDownwind)
	{
		return ConvectiveRatio(gradUpwind.DeltaX * dX + gradUpwind.DeltaY * dY, cUpwind, cDownwind);
	}

	public static double TVDVirtualPointConvectiveRatio(double cVirtual, double cUpwind, double cDownwind)
	{
		return UpstreamValueConvectiveRatio(cVirtual, cUpwind, cDownwind);
	}

	internal static double UpstreamValueConvectiveRatio(double cFarUpstream, double cUpwind, double cDownwind)
	{
		if (!double.IsFinite(cFarUpstream) || !double.IsFinite(cUpwind) || !double.IsFinite(cDownwind))
		{
			return 0.0;
		}
		double cDownwindAboveCUpwind = cDownwind - cUpwind;
		if (cDownwindAboveCUpwind == 0.0 || !double.IsFinite(cDownwindAboveCUpwind))
		{
			return 0.0;
		}
		double cUpwindCFarUpstreamTerm = (cUpwind - cFarUpstream) / cDownwindAboveCUpwind;
		if (!double.IsNaN(cUpwindCFarUpstreamTerm))
		{
			return cUpwindCFarUpstreamTerm;
		}
		return 0.0;
	}
}
