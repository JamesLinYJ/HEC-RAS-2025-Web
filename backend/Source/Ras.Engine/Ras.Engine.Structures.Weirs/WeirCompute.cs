using System;

namespace Ras.Engine.Structures.Weirs;

public static class WeirCompute
{
	private static readonly double DepthTol = 0.001;

	private static readonly double WetPerimTol = 0.001;

	public static void ComputeWeirFlowTemp(WeirPhysProps weir, WeirFlowState weirFlowState, int fIdx, Memory<float> elevs, Memory<float> wettedPerim)
	{
		Span<float> elevsSpan = elevs.Span;
		_ = wettedPerim.Span;
		double elev = elevsSpan[0];
		double x = Math.Abs(Math.Max(weirFlowState.WSEUS, elev) - Math.Max(weirFlowState.WSEDS, elev));
		weirFlowState.Q[fIdx] = weir.WeirCoef * weir.Length * Math.Pow(x, 1.5) * (double)Math.Sign(weirFlowState.WSEUS - weirFlowState.WSEDS);
	}

	public static void ComputeWeirFlow(WeirPhysProps weir, WeirFlowState weirFlowState, int fIdx, Memory<float> areas, Memory<float> elevs, Memory<float> wettedPerim)
	{
		_ = areas.Span;
		Span<float> elevsSpan = elevs.Span;
		Span<float> wettedPerimSpan = wettedPerim.Span;
		double elev = elevsSpan[0];
		double upstreamEnergyGrade = weirFlowState.WSEUS + weirFlowState.VelUS * weirFlowState.VelUS / (2.0 * weir.Grav);
		double downstreamEnergyGrade = weirFlowState.WSEDS + weirFlowState.VelDS * weirFlowState.VelDS / (2.0 * weir.Grav);
		double energyGrade = 0.0;
		double waterSurface = 0.0;
		double energyGradeAboveElev = 0.0;
		double waterSurfaceWeirFlowStateTerm = 0.0;
		double energyGrade2 = 0.0;
		double sign = 1.0;
		double wettedPerimSpanTerm = 0.0;
		double computed = 0.0;
		double weirDischargeSum = 0.0;
		double computed2 = 0.0;
		double tolerance = 1E-05;
		if (upstreamEnergyGrade >= downstreamEnergyGrade)
		{
			energyGrade = upstreamEnergyGrade;
			waterSurface = weirFlowState.WSEUS;
			energyGrade2 = downstreamEnergyGrade;
			waterSurfaceWeirFlowStateTerm = Math.Max(weirFlowState.WSEDS, elev);
		}
		else
		{
			energyGrade = downstreamEnergyGrade;
			waterSurface = weirFlowState.WSEDS;
			waterSurfaceWeirFlowStateTerm = Math.Max(weirFlowState.WSEUS, elev);
			energyGrade2 = upstreamEnergyGrade;
			sign = -1.0;
		}
		if (elevs.Length <= 1)
		{
			energyGradeAboveElev = energyGrade - elev;
			ComputeWeirSubmergenceCoef(weir, weirFlowState, energyGrade, waterSurface, energyGrade2, waterSurfaceWeirFlowStateTerm, elev);
			weirFlowState.Q[fIdx] = weir.WeirCoef * weirFlowState.CoefWeirSubmergence * weirFlowState.CoefOgeeHead * weir.Length * Math.Pow(energyGradeAboveElev, 1.5) * sign;
			return;
		}
		if ((double)wettedPerimSpan[0] > WetPerimTol && upstreamEnergyGrade > (double)elevsSpan[0])
		{
			wettedPerimSpanTerm = wettedPerimSpan[0];
			computed2 += wettedPerimSpanTerm;
			elev = elevsSpan[0];
			energyGradeAboveElev = energyGrade - elev;
			ComputeWeirSubmergenceCoef(weir, weirFlowState, energyGrade, waterSurface, energyGrade2, waterSurfaceWeirFlowStateTerm, elev);
			double weirDischarge = weir.WeirCoef * weirFlowState.CoefWeirSubmergence * weirFlowState.CoefOgeeHead * wettedPerimSpanTerm * Math.Pow(energyGradeAboveElev, 1.5) * sign;
			weirDischargeSum += weirDischarge;
		}
		for (int i = 1; i < elevsSpan.Length; i++)
		{
			if (upstreamEnergyGrade > (double)elevsSpan[i - 1])
			{
				double elevStep = elevsSpan[i] - elevsSpan[i - 1];
				if (elevStep > 0.0)
				{
					double wettedPerimStep = wettedPerimSpan[i] - wettedPerimSpan[i - 1];
					computed = wettedPerimStep * wettedPerimStep - elevStep * elevStep;
					wettedPerimSpanTerm = ((!(computed > tolerance)) ? 0.0 : Math.Sqrt(computed));
					computed2 += wettedPerimSpanTerm;
					elev = elevsSpan[i - 1];
					energyGradeAboveElev = energyGrade - elev;
					ComputeWeirSubmergenceCoef(weir, weirFlowState, energyGrade, waterSurface, energyGrade2, waterSurfaceWeirFlowStateTerm, elev);
					double weirDischarge2 = weir.WeirCoef * weirFlowState.CoefWeirSubmergence * weirFlowState.CoefOgeeHead * wettedPerimSpanTerm * Math.Pow(energyGradeAboveElev, 1.5) * sign;
					weirDischargeSum += weirDischarge2;
				}
			}
		}
		weirFlowState.Q[fIdx] = weirDischargeSum;
	}

	public static void ComputeWeirSubmergenceCoef(WeirPhysProps weir, WeirFlowState weirFlowState, double egHead, double WSEHW, double egTW, double WSETW, double weirCrest)
	{
		weirFlowState.CoefWeirSubmergence = 1.0;
		weirFlowState.CoefOgeeHead = 1.0;
		double egHeadAboveWeirCrest = egHead - weirCrest;
		double waterSurfaceAboveWeirCrest = Math.Max(WSETW - weirCrest, 0.0);
		if (egHeadAboveWeirCrest < DepthTol)
		{
			return;
		}
		weirFlowState.Submergence = waterSurfaceAboveWeirCrest / egHeadAboveWeirCrest;
		double submergence = weirFlowState.Submergence;
		double submergenceRatio = (egTW - weirCrest) / (egHead - weirCrest);
		double y = submergenceRatio;
		if (submergenceRatio > 0.95)
		{
			double y2 = submergence;
			submergence = Interpolate(0.95, 1.0, submergenceRatio, y2, y);
			if (submergenceRatio > 0.97)
			{
				double y3 = submergence;
				submergence = Interpolate(0.97, 1.0, submergenceRatio, y3, y);
			}
			if (submergenceRatio > 0.99)
			{
				double y4 = submergence;
				submergence = Interpolate(0.99, 1.0, submergenceRatio, y4, y);
			}
		}
		if (weir.Shape == WeirCrestShape.BroadCrested)
		{
			if (submergence < 0.76)
			{
				return;
			}
			double[] breakpoints = new double[10] { 0.76, 0.84, 0.88, 0.9, 0.92, 0.94, 0.96, 0.98, 0.995, 1.0 };
			double[] coefficients = new double[10] { 1.0, 0.985, 0.96, 0.93, 0.88, 0.82, 0.72, 0.575, 0.35, 0.0 };
			int intervalIndex = 0;
			for (int i = 0; i < breakpoints.Length; i++)
			{
				if (submergence <= breakpoints[i])
				{
					intervalIndex = i;
					break;
				}
			}
			double interpolationFraction = (submergence - breakpoints[intervalIndex - 1]) / (breakpoints[intervalIndex] - breakpoints[intervalIndex - 1]);
			weirFlowState.CoefWeirSubmergence = coefficients[intervalIndex - 1] + interpolationFraction * (coefficients[intervalIndex] - coefficients[intervalIndex - 1]);
		}
		if (weir.Shape == WeirCrestShape.Ogee)
		{
			double[] breakpointsArray = new double[8] { 0.0, 0.052, 0.086, 0.23, 0.34, 0.495, 0.705, 0.85 };
			double[] coefficientsArray = new double[8] { 100.0, 60.0, 40.0, 10.0, 6.0, 3.0, 1.0, 0.0 };
			double waterSurfaceDropOverEgHeadAboveWeirCrest = (WSEHW - WSETW) / egHeadAboveWeirCrest;
			double tolerance = 1E-07;
			if (submergenceRatio > 0.95)
			{
				waterSurfaceDropOverEgHeadAboveWeirCrest = 1.0 - submergence;
			}
			waterSurfaceDropOverEgHeadAboveWeirCrest = Math.Max(waterSurfaceDropOverEgHeadAboveWeirCrest, tolerance);
			int intervalIndex2 = 0;
			for (int j = 0; j < breakpointsArray.Length - 1; j++)
			{
				if (waterSurfaceDropOverEgHeadAboveWeirCrest < breakpointsArray[j])
				{
					intervalIndex2 = j;
					break;
				}
			}
			waterSurfaceDropOverEgHeadAboveWeirCrest = Math.Min(waterSurfaceDropOverEgHeadAboveWeirCrest, 0.85);
			double interpolationFraction2 = (waterSurfaceDropOverEgHeadAboveWeirCrest - breakpointsArray[intervalIndex2 - 1]) / (breakpointsArray[intervalIndex2] - breakpointsArray[intervalIndex2 - 1]);
			double interpolatedValue = coefficientsArray[intervalIndex2 - 1] - interpolationFraction2 * (coefficientsArray[intervalIndex2 - 1] - coefficientsArray[intervalIndex2]);
			weirFlowState.CoefWeirSubmergence = 0.01 * (100.0 - interpolatedValue);
			if (weir.IsDesignHead)
			{
				double designHeadRatio = egHeadAboveWeirCrest / weir.DesignHead;
				if (designHeadRatio > 1.6)
				{
					weirFlowState.CoefOgeeHead = 1.0;
				}
				else
				{
					double[] breakpoints2 = new double[8] { 0.0, 0.04, 0.16, 0.31, 0.61, 1.0, 1.35, 1.6 };
					double[] coefficients2 = new double[8] { 0.785, 0.8, 0.84, 0.88, 0.94, 1.0, 1.04, 1.07 };
					intervalIndex2 = 0;
					for (int k = 0; k < breakpoints2.Length - 1; k++)
					{
						if (designHeadRatio < breakpoints2[k])
						{
							intervalIndex2 = k;
							break;
						}
					}
					intervalIndex2 = Math.Max(intervalIndex2, 1);
					weirFlowState.CoefOgeeHead = Interpolate(breakpoints2[intervalIndex2 - 1], breakpoints2[intervalIndex2], designHeadRatio, coefficients2[intervalIndex2 - 1], coefficients2[intervalIndex2]);
				}
			}
		}
		if (weir.Shape == WeirCrestShape.SharpCrested && WSETW > weirCrest)
		{
			weirFlowState.CoefWeirSubmergence = Math.Pow(1.0 - Math.Pow(submergence, 1.5), 0.385);
		}
	}

	public static double Interpolate(double x1, double x2, double stx, double y1, double y2)
	{
		double result = (y1 + y2) / 2.0;
		if (x1 != x2)
		{
			result = (x2 - stx) * (y2 - y1) / (x1 - x2) + y2;
		}
		return result;
	}
}
