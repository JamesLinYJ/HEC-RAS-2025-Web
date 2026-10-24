using System;

namespace Ras.Synthetics.Solvers;

public sealed class SolverStandardStep
{
	public enum MarchDirection
	{
		Upstream,
		Downstream
	}

	public readonly struct StationResult
	{
		public double Stage { get; init; }

		public double Depth { get; init; }

		public double Velocity { get; init; }

		public double FroudeNumber { get; init; }

		public double FrictionSlope { get; init; }

		public int Iterations { get; init; }

		public bool Converged { get; init; }
	}

	public double Flow { get; init; }

	public Func<double, ICrossSection> XSec { get; init; }

	public Func<double, double> BedElevation { get; init; }

	public Func<double, double> ManningN { get; init; }

	public double Gravity { get; init; } = 9.80665;

	public double ManningKn { get; init; } = 1.0;

	public double Tolerance { get; init; } = 1E-06;

	public int MaxIterations { get; init; } = 50;

	public bool TrySolve(double[] stations, double boundaryStation, double boundaryStage, MarchDirection direction, out StationResult[] result)
	{
		int stationLength = stations.Length;
		result = new StationResult[stationLength];
		if (stationLength == 0)
		{
			return true;
		}
		int[] intValues = new int[stationLength];
		for (int i = 0; i < stationLength; i++)
		{
			intValues[i] = i;
		}
		double bx = boundaryStation;
		Array.Sort(intValues, (int a, int b) => Math.Abs(stations[a] - bx).CompareTo(Math.Abs(stations[b] - bx)));
		int ordinal = intValues[0];
		double station = stations[ordinal];
		double bedElevation = BedElevation(station);
		double boundaryStageAboveBedElevation = boundaryStage - bedElevation;
		if (boundaryStageAboveBedElevation < 0.0)
		{
			boundaryStageAboveBedElevation = 0.0;
		}
		ICrossSection xs = XSec(station);
		double n = ManningN(station);
		result[ordinal] = MakeResult(xs, bedElevation, boundaryStageAboveBedElevation, n, 0, converged: true);
		ICrossSection crossSection = null;
		double computed = 0.0;
		bool result2 = true;
		for (int unity = 1; unity < stationLength; unity++)
		{
			int ordinal2 = intValues[unity];
			int ordinal3 = intValues[unity - 1];
			double station2 = stations[ordinal2];
			double station3 = stations[ordinal3];
			double dx = Math.Abs(station2 - station3);
			double bedElevation2 = BedElevation(station2);
			ICrossSection crossSection2 = XSec(station2);
			double manningN = ManningN(station2);
			double computed2;
			if (crossSection2 == crossSection)
			{
				computed2 = computed;
			}
			else
			{
				computed2 = CriticalDepth(crossSection2);
				crossSection = crossSection2;
				computed = computed2;
			}
			double depth = SolveStep(crossSection2, computed2, manningN, bedElevation2, dx, direction, result[ordinal3], out var iters, out var converged);
			result[ordinal2] = MakeResult(crossSection2, bedElevation2, depth, manningN, iters, converged);
			if (!converged)
			{
				result2 = false;
			}
		}
		return result2;
	}

	private StationResult MakeResult(ICrossSection xs, double z, double depth, double n, int iters, bool converged)
	{
		if (depth < 0.0)
		{
			depth = 0.0;
		}
		xs.HydraulicProperties(depth, out var area, out var wettedPerimeter, out var topWidth);
		double velocity = ((area > 0.0) ? (Flow / area) : 0.0);
		double r = ((wettedPerimeter > 0.0) ? (area / wettedPerimeter) : 0.0);
		double frictionSlope = FrictionSlope(n, velocity, r);
		double areaOverTopWidth = ((topWidth > 0.0) ? (area / topWidth) : 0.0);
		double froudeNumber = ((areaOverTopWidth > 0.0) ? (Math.Abs(velocity) / Math.Sqrt(Gravity * areaOverTopWidth)) : 0.0);
		return new StationResult
		{
			Stage = z + depth,
			Depth = depth,
			Velocity = velocity,
			FroudeNumber = froudeNumber,
			FrictionSlope = frictionSlope,
			Iterations = iters,
			Converged = converged
		};
	}

	private double FrictionSlope(double n, double V, double R)
	{
		if (R <= 0.0 || n <= 0.0)
		{
			return 0.0;
		}
		double computed = Math.Cbrt(R);
		double manningKnTerm = ManningKn * computed * computed;
		if (manningKnTerm <= 0.0)
		{
			return 0.0;
		}
		double computed2 = n * V / manningKnTerm;
		return computed2 * computed2;
	}

	private double SolveStep(ICrossSection xsU, double dCrit, double nU, double zU, double dx, MarchDirection dir, StationResult prev, out int iters, out bool converged)
	{
		double EK = prev.Stage + prev.Velocity * prev.Velocity / (2.0 * Gravity);
		double SfK = prev.FrictionSlope;
		double depth = prev.Depth;
		double signHF = ((dir == MarchDirection.Upstream) ? (-1.0) : 1.0);
		double dCritOffset;
		double dCritTerm;
		if (dir == MarchDirection.Upstream)
		{
			dCritOffset = dCrit + 1E-06;
			double depthDCritTerm = Math.Max(depth, dCrit);
			dCritTerm = Math.Max(2.0 * depthDCritTerm, 5.0 * dCrit);
			if (dCritTerm <= dCritOffset)
			{
				dCritTerm = dCritOffset + Math.Max(1.0, dCritOffset);
			}
		}
		else
		{
			dCritOffset = 1E-06;
			dCritTerm = Math.Max(dCrit - 1E-06, 2E-06);
		}
		double residual = Residual(dCritOffset);
		double residual2 = Residual(dCritTerm);
		int iters2 = 0;
		while (residual * residual2 > 0.0 && iters2 < 30 && dir == MarchDirection.Upstream)
		{
			dCritTerm *= 2.0;
			residual2 = Residual(dCritTerm);
			iters2++;
		}
		if (residual * residual2 > 0.0)
		{
			iters = iters2;
			converged = false;
			if (!(Math.Abs(residual) < Math.Abs(residual2)))
			{
				return dCritTerm;
			}
			return dCritOffset;
		}
		if (Math.Abs(residual) < Tolerance)
		{
			iters = iters2;
			converged = true;
			return dCritOffset;
		}
		if (Math.Abs(residual2) < Tolerance)
		{
			iters = iters2;
			converged = true;
			return dCritTerm;
		}
		double a = dCritOffset;
		double fa = residual;
		double b = dCritTerm;
		double fb = residual2;
		double computed = Math.Clamp(depth, a, b);
		double computed2 = Math.Clamp(computed * 1.05, a, b);
		if (Math.Abs(computed2 - computed) < 1E-06)
		{
			computed2 = 0.5 * (a + b);
		}
		double residual3 = Residual(computed);
		double residual4 = Residual(computed2);
		TightenBracket(ref a, ref fa, ref b, ref fb, computed, residual3);
		TightenBracket(ref a, ref fa, ref b, ref fb, computed2, residual4);
		double naNTerm = computed2;
		for (iters = 1; iters <= MaxIterations; iters++)
		{
			double naN = double.NaN;
			double residual4AboveResidual3 = residual4 - residual3;
			if (Math.Abs(residual4AboveResidual3) > 1E-30 && Math.Abs(computed2 - computed) > 1E-30)
			{
				naN = computed2 - residual4 * (computed2 - computed) / residual4AboveResidual3;
			}
			if (!double.IsFinite(naN) || naN <= a || naN >= b)
			{
				naN = 0.5 * (a + b);
			}
			double residual5 = Residual(naN);
			TightenBracket(ref a, ref fa, ref b, ref fb, naN, residual5);
			if (Math.Abs(naN - naNTerm) < Tolerance || Math.Abs(residual5) < Tolerance)
			{
				converged = true;
				return naN;
			}
			computed = computed2;
			residual3 = residual4;
			computed2 = naN;
			residual4 = residual5;
			naNTerm = naN;
			if (b - a < Tolerance)
			{
				converged = true;
				return 0.5 * (a + b);
			}
		}
		converged = false;
		return naNTerm;
		double Residual(double d)
		{
			if (d <= 0.0)
			{
				d = 1E-12;
			}
			xsU.HydraulicProperties(d, out var area, out var wettedPerimeter, out var _);
			double dischargeOverArea = ((area > 0.0) ? (Flow / area) : 0.0);
			double zUGravityTerm = zU + d + dischargeOverArea * dischargeOverArea / (2.0 * Gravity);
			double frictionSlope = FrictionSlope(R: (wettedPerimeter > 0.0) ? (area / wettedPerimeter) : 0.0, n: nU, V: dischargeOverArea);
			double sfKFrictionSlopeTerm = 0.5 * (SfK + frictionSlope) * dx;
			return zUGravityTerm - EK + signHF * sfKFrictionSlopeTerm;
		}
	}

	private static void TightenBracket(ref double a, ref double fa, ref double b, ref double fb, double d, double f)
	{
		if (!(d <= a) && !(d >= b))
		{
			if (f == 0.0)
			{
				a = (b = d);
				fa = (fb = 0.0);
			}
			else if (Math.Sign(f) == Math.Sign(fa))
			{
				a = d;
				fa = f;
			}
			else
			{
				b = d;
				fb = f;
			}
		}
	}

	private double CriticalDepth(ICrossSection xs)
	{
		double computed = 1E-06;
		double computed2 = 100.0;
		int ordinal = 0;
		while (F(computed2) > 0.0 && ordinal < 30)
		{
			computed2 *= 2.0;
			ordinal++;
		}
		if (F(computed) <= 0.0)
		{
			return computed;
		}
		if (F(computed2) >= 0.0)
		{
			return computed2;
		}
		for (int i = 0; i < 80; i++)
		{
			double computed3 = 0.5 * (computed + computed2);
			if (F(computed3) > 0.0)
			{
				computed = computed3;
			}
			else
			{
				computed2 = computed3;
			}
			if (computed2 - computed < Tolerance)
			{
				return 0.5 * (computed + computed2);
			}
		}
		return 0.5 * (computed + computed2);
		double F(double d)
		{
			xs.HydraulicProperties(d, out var area, out var _, out var topWidth);
			if (area <= 0.0)
			{
				return double.PositiveInfinity;
			}
			return Flow * Flow * topWidth / (Gravity * area * area * area) - 1.0;
		}
	}
}
