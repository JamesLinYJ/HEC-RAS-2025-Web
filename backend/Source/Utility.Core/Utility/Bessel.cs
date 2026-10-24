using System;

namespace Utility;

public static class Bessel
{
	public static double J0(double x)
	{
		double computed = Math.Abs(x);
		if (computed < 8.0)
		{
			double computed2 = x * x;
			double computed3 = 57568490574.0 + computed2 * (-13362590354.0 + computed2 * (651619640.7 + computed2 * (-11214424.18 + computed2 * (77392.33017 + computed2 * -184.9052456))));
			double computed4 = 57568490411.0 + computed2 * (1029532985.0 + computed2 * (9494680.718 + computed2 * (59272.64853 + computed2 * (267.8532712 + computed2 * 1.0))));
			return computed3 / computed4;
		}
		double computed5 = 8.0 / computed;
		double computed6 = computed5 * computed5;
		double computed7 = 1.0 + computed6 * (-0.001098628627 + computed6 * (2.734510407E-05 + computed6 * (-2.073370639E-06 + computed6 * 2.093887211E-07)));
		double computed8 = -0.01562499995 + computed6 * (0.0001430488765 + computed6 * (-6.911147651E-06 + computed6 * (7.621095161E-07 + computed6 * -9.34935152E-08)));
		double computed9 = computed - 0.785398164;
		return Math.Sqrt(0.636619772 / computed) * (Math.Cos(computed9) * computed7 - computed5 * Math.Sin(computed9) * computed8);
	}

	public static double J1(double x)
	{
		double computed = Math.Abs(x);
		if (computed < 8.0)
		{
			double computed2 = x * x;
			double computed3 = x * (72362614232.0 + computed2 * (-7895059235.0 + computed2 * (242396853.1 + computed2 * (-2972611.439 + computed2 * (15704.4826 + computed2 * -30.16036606)))));
			double computed4 = 144725228442.0 + computed2 * (2300535178.0 + computed2 * (18583304.74 + computed2 * (99447.43394 + computed2 * (376.9991397 + computed2 * 1.0))));
			return computed3 / computed4;
		}
		double computed5 = 8.0 / computed;
		double computed6 = computed5 * computed5;
		double computed7 = 1.0 + computed6 * (0.00183105 + computed6 * (-3.516396496E-05 + computed6 * (2.457520174E-06 + computed6 * -2.40337019E-07)));
		double computed8 = 0.04687499995 + computed6 * (-0.0002002690873 + computed6 * (8.449199096E-06 + computed6 * (-8.8228987E-07 + computed6 * 1.05787412E-07)));
		double computed9 = computed - 2.356194491;
		double computed10 = Math.Sqrt(0.636619772 / computed) * (Math.Cos(computed9) * computed7 - computed5 * Math.Sin(computed9) * computed8);
		if (!(x < 0.0))
		{
			return computed10;
		}
		return 0.0 - computed10;
	}

	public static double Y0(double x)
	{
		if (x < 8.0)
		{
			double computed = x * x;
			double computed2 = -2957821389.0 + computed * (7062834065.0 + computed * (-512359803.6 + computed * (10879881.29 + computed * (-86327.92757 + computed * 228.4622733))));
			double computed3 = 40076544269.0 + computed * (745249964.8 + computed * (7189466.438 + computed * (47447.2647 + computed * (226.1030244 + computed * 1.0))));
			return computed2 / computed3 + 0.636619772 * J0(x) * Math.Log(x);
		}
		double computed4 = 8.0 / x;
		double computed5 = computed4 * computed4;
		double computed6 = 1.0 + computed5 * (-0.001098628627 + computed5 * (2.734510407E-05 + computed5 * (-2.073370639E-06 + computed5 * 2.093887211E-07)));
		double computed7 = -0.01562499995 + computed5 * (0.0001430488765 + computed5 * (-6.911147651E-06 + computed5 * (7.621095161E-07 + computed5 * -9.34935152E-08)));
		double computed8 = x - 0.785398164;
		return Math.Sqrt(0.636619772 / x) * (Math.Sin(computed8) * computed6 + computed4 * Math.Cos(computed8) * computed7);
	}

	public static double Y1(double x)
	{
		if (x < 8.0)
		{
			double computed = x * x;
			double computed2 = x * (-4900604943000.0 + computed * (1275274390000.0 + computed * (-51534381390.0 + computed * (734926455.1 + computed * (-4237922.726 + computed * 8511.937935)))));
			double computed3 = 24995805700000.0 + computed * (424441966400.0 + computed * (3733650367.0 + computed * (22459040.02 + computed * (102042.605 + computed * (354.9632885 + computed * 1.0)))));
			return computed2 / computed3 + 0.636619772 * (J1(x) * Math.Log(x) - 1.0 / x);
		}
		double computed4 = 8.0 / x;
		double computed5 = computed4 * computed4;
		double computed6 = 1.0 + computed5 * (0.00183105 + computed5 * (-3.516396496E-05 + computed5 * (2.457520174E-06 + computed5 * -2.40337019E-07)));
		double computed7 = 0.04687499995 + computed5 * (-0.0002002690873 + computed5 * (8.449199096E-06 + computed5 * (-8.8228987E-07 + computed5 * 1.05787412E-07)));
		double computed8 = x - 2.356194491;
		return Math.Sqrt(0.636619772 / x) * (Math.Sin(computed8) * computed6 + computed4 * Math.Cos(computed8) * computed7);
	}

	public static double J2(double x)
	{
		return 2.0 / x * J1(x) - J0(x);
	}

	public static double Y2(double x)
	{
		return 2.0 / x * Y1(x) - Y0(x);
	}
}
