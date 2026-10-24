using System;

namespace Ras.Engine.Structures.Culverts;

public class CulvertXSection
{
	public double Qtotal { get; set; }

	public double Depth { get; set; }

	public double Area { get; set; }

	public double WettedPerim { get; set; }

	public double TopWidth { get; set; }

	public double Vel { get; set; }

	public double VelHead { get; set; }

	public double HydRad { get; set; }

	public double Mann { get; set; }

	public double Conveyance { get; set; }

	public double YCentroid { get; set; }

	public double SpecificForce { get; set; }

	public double FrictionSlope { get; set; }

	public void Compute(ICulvertGeometry culvert, CulvertSolverControl control)
	{
		Depth = Math.Min(Depth, culvert.FullRise);
		if (Depth <= 0.0)
		{
			ZeroOut();
			return;
		}
		switch (culvert.Shape)
		{
		case CulvertShape.Box:
			ComputeBox(culvert);
			break;
		case CulvertShape.Circular:
			ComputeCircular(culvert);
			break;
		case CulvertShape.SemiCircular:
			ComputeSemiCircular(culvert);
			break;
		case CulvertShape.Ellipse:
			ComputeEllipse(culvert);
			break;
		case CulvertShape.Arch:
			ComputeArch(culvert);
			break;
		default:
			throw new NotImplementedException($"Culvert shape {culvert.Shape} not implemented in XSection properties compute");
		}
		if (Area <= 0.0)
		{
			ZeroOut();
			return;
		}
		HydRad = Area / WettedPerim;
		double hydRadTerm = Math.Pow(HydRad, 2.0 / 3.0);
		Conveyance = control.MannCoef / Mann * Area * hydRadTerm;
		Vel = Qtotal / Area;
		VelHead = Vel * Vel / (2.0 * control.Grav);
		SpecificForce = Qtotal * Qtotal / (control.Grav * Area) + YCentroid * Area;
		FrictionSlope = Mann * Mann * Vel * Vel / (control.MannCoef * control.MannCoef * hydRadTerm * hydRadTerm);
	}

	private void ComputeBox(ICulvertGeometry culvert)
	{
		Area = Depth * culvert.Span;
		TopWidth = culvert.Span;
		WettedPerim = 2.0 * Depth + culvert.Span;
		if (Depth >= culvert.FullRise)
		{
			WettedPerim += culvert.Span;
		}
		YCentroid = Depth / 2.0;
	}

	private void ComputeCircular(ICulvertGeometry culvert)
	{
		double scaledFullRise = culvert.FullRise * 0.5;
		(Area, WettedPerim, TopWidth) = ComputeCircularProps(scaledFullRise, Depth);
		double topWidthAreaTerm;
		if (Depth <= scaledFullRise)
		{
			topWidthAreaTerm = scaledFullRise - TopWidth * TopWidth * TopWidth / (12.0 * Area);
		}
		else if (Depth >= culvert.FullRise)
		{
			topWidthAreaTerm = scaledFullRise;
		}
		else
		{
			double piTerm = Math.PI * scaledFullRise * scaledFullRise;
			double areaTerm = piTerm - Area;
			topWidthAreaTerm = (piTerm * scaledFullRise - TopWidth * TopWidth * TopWidth / 12.0 - areaTerm * scaledFullRise) / Area;
		}
		YCentroid = Depth - topWidthAreaTerm;
	}

	private static (double area, double wp, double topWidth) ComputeCircularProps(double radius, double depth)
	{
		double depthRadiusTerm = Math.Asin((depth - radius) / radius);
		double piTerm = Math.PI + 2.0 * depthRadiusTerm;
		double radiusTerm = piTerm * radius;
		double radiusTerm2 = 2.0 * radius * Math.Cos(depthRadiusTerm);
		return (area: 0.5 * radius * radius * (piTerm - Math.Sin(piTerm)), wp: radiusTerm, topWidth: radiusTerm2);
	}

	private void ComputeSemiCircular(ICulvertGeometry culvert)
	{
		double fullRise = culvert.FullRise;
		double depth = fullRise + Depth;
		(double area, double wp, double topWidth) tuple = ComputeCircularProps(fullRise, depth);
		double area = tuple.area;
		double wp = tuple.wp;
		double topWidth = tuple.topWidth;
		Area = area - Math.PI / 2.0 * fullRise * fullRise;
		WettedPerim = wp - Math.PI * fullRise + 2.0 * fullRise;
		TopWidth = topWidth;
		double fullRiseCulvertTerm = (culvert.FullRise * culvert.FullRise * culvert.FullRise - TopWidth * TopWidth * TopWidth) / (12.0 * Area);
		YCentroid = Depth - fullRiseCulvertTerm;
	}

	private void ComputeArch(ICulvertGeometry culvert)
	{
		double fullRise = culvert.FullRise;
		double scaledSpan = culvert.Span / 2.0;
		double ellipseCentroidNumeratorFullRiseTerm;
		if (Depth >= culvert.FullRise)
		{
			Area = Math.PI / 2.0 * fullRise * scaledSpan;
			WettedPerim = 2.0 * EllipseArcLength(0.0, Math.PI / 2.0, fullRise, scaledSpan) + 2.0 * scaledSpan;
			TopWidth = 0.0;
			ellipseCentroidNumeratorFullRiseTerm = EllipseCentroidNumerator(fullRise, 2.0 * fullRise, fullRise, scaledSpan) / Area - fullRise;
		}
		else
		{
			double depth = Depth;
			double fullRiseDepthTerm = scaledSpan / fullRise * Math.Sqrt(fullRise * fullRise - depth * depth);
			double depthFullRiseTerm = Math.Asin(depth / fullRise);
			double computed = Math.Asin(1.0);
			double fullRiseDepthTerm2 = Math.Sqrt(fullRise * fullRise - depth * depth);
			double fullRiseDepthTerm3 = scaledSpan / fullRise * (fullRise * fullRise * computed - depth * fullRiseDepthTerm2 - fullRise * fullRise * depthFullRiseTerm);
			Area = Math.PI / 2.0 * fullRise * scaledSpan - fullRiseDepthTerm3;
			double phi = Math.Acos(depth / fullRise);
			WettedPerim = 2.0 * EllipseArcLength(phi, Math.PI / 2.0, fullRise, scaledSpan) + 2.0 * scaledSpan;
			TopWidth = fullRiseDepthTerm * 2.0;
			ellipseCentroidNumeratorFullRiseTerm = EllipseCentroidNumerator(fullRise, Depth + fullRise, fullRise, scaledSpan) / Area - fullRise;
		}
		YCentroid = Depth - ellipseCentroidNumeratorFullRiseTerm;
	}

	private void ComputeEllipse(ICulvertGeometry culvert)
	{
		double scaledFullRise = culvert.FullRise / 2.0;
		double scaledSpan = culvert.Span / 2.0;
		double computed;
		if (Depth >= culvert.FullRise)
		{
			Area = Math.PI * scaledFullRise * scaledSpan;
			WettedPerim = 4.0 * EllipseArcLength(0.0, Math.PI / 2.0, scaledFullRise, scaledSpan);
			TopWidth = 0.0;
			computed = scaledFullRise;
		}
		else
		{
			double scaledFullRiseAboveDepth = scaledFullRise - Depth;
			double computed2 = scaledSpan / scaledFullRise * Math.Sqrt(scaledFullRise * scaledFullRise - scaledFullRiseAboveDepth * scaledFullRiseAboveDepth);
			double computed3 = Math.Asin(scaledFullRiseAboveDepth / scaledFullRise);
			double computed4 = Math.Asin(1.0);
			double computed5 = Math.Sqrt(scaledFullRise * scaledFullRise - scaledFullRiseAboveDepth * scaledFullRiseAboveDepth);
			Area = scaledSpan / scaledFullRise * (scaledFullRise * scaledFullRise * computed4 - scaledFullRiseAboveDepth * computed5 - scaledFullRise * scaledFullRise * computed3);
			double phi = Math.Acos(scaledFullRiseAboveDepth / scaledFullRise);
			WettedPerim = 2.0 * EllipseArcLength(0.0, phi, scaledFullRise, scaledSpan);
			TopWidth = computed2 * 2.0;
			computed = EllipseCentroidNumerator(0.0, Depth, scaledFullRise, scaledSpan) / Area;
		}
		YCentroid = Depth - computed;
	}

	private static double EllipseArcLength(double phi1, double phi2, double a, double b)
	{
		return CulvertMath.Gauss5Integrate(phi1, phi2, (double computed) =>
		{
			double computed2 = a * Math.Sin(computed);
			double computed3 = b * Math.Cos(computed);
			return Math.Sqrt(computed2 * computed2 + computed3 * computed3);
		});
	}

	private static double EllipseCentroidNumerator(double h1, double h2, double a, double b)
	{
		double coeff = 2.0 * b / a;
		return CulvertMath.Gauss5Integrate(h1, h2, (double computed) =>
		{
			double computed2 = Math.Sqrt(2.0 * a * computed - computed * computed);
			return coeff * computed2 * computed;
		});
	}

	public void ZeroOut()
	{
		Area = 0.0;
		WettedPerim = 0.0;
		TopWidth = 0.0;
		Vel = 0.0;
		VelHead = 0.0;
		HydRad = 0.0;
		Conveyance = 0.0;
		YCentroid = 0.0;
		SpecificForce = 0.0;
		FrictionSlope = 0.0;
	}
}
