using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility;

namespace Ras.Synthetics.SyntheticParams;

public class LongWaveRunupParams : BasicRectangleParams
{
	private readonly record struct AnalyticalState(double Stage, double Velocity);

	private readonly record struct StateKey(long PositionBits, long TimeBits);

	private sealed class CarrierGreenspanSolution
	{
		private readonly record struct DimensionlessState(double Elevation, double Velocity);

		private const double XiMaximum = 16.0;

		private const int XiIntervals = 1600;

		private const double OmegaMaximum = 16.0;

		private const int OmegaIntervals = 1600;

		private const int TransformIterations = 12;

		private const double TransformTolerance = 1E-10;

		private readonly object _cacheLock = new object();

		private readonly Dictionary<StateKey, AnalyticalState> _cache = new Dictionary<StateKey, AnalyticalState>();

		private readonly double[] _omega = new double[1601];

		private readonly double[] _weightedSpectrum = new double[1601];

		public CarrierGreenspanSolution()
		{
			BuildSpectrum();
		}

		public AnalyticalState Evaluate(Point point, double dimensionalTime)
		{
			double pointTerm = (point.X - 50000.0) / 5000.0;
			StateKey key = new StateKey(BitConverter.DoubleToInt64Bits(pointTerm), BitConverter.DoubleToInt64Bits(dimensionalTime));
			lock (_cacheLock)
			{
				if (_cache.TryGetValue(key, out var localValue))
				{
					return localValue;
				}
			}
			AnalyticalState analyticalState = EvaluateUncached(point, dimensionalTime, pointTerm);
			lock (_cacheLock)
			{
				_cache[key] = analyticalState;
				return analyticalState;
			}
		}

		private AnalyticalState EvaluateUncached(Point point, double dimensionalTime, double landPosition)
		{
			double pointTerm = 0.1 * (point.X - 50000.0);
			if (dimensionalTime <= 0.0)
			{
				return new AnalyticalState(Math.Max(InitialSurface(landPosition), pointTerm), 0.0);
			}
			double dimensionalTimeTerm = dimensionalTime / Math.Sqrt(5096.83995922528);
			SolveShoreline(dimensionalTimeTerm, out var elevation, out var lambda);
			if (landPosition >= elevation)
			{
				return new AnalyticalState(pointTerm, 0.0);
			}
			double computed = 4.0 * Math.Sqrt(Math.Max(0.0 - landPosition, 0.0));
			double lambda2 = lambda;
			DimensionlessState dimensionlessState = default;
			for (int i = 0; i < 12; i++)
			{
				dimensionlessState = EvaluateTransform(computed, lambda2);
				double elevationAboveLandPosition = dimensionlessState.Elevation - landPosition;
				if (elevationAboveLandPosition <= 0.0)
				{
					return new AnalyticalState(pointTerm, 0.0);
				}
				double computed2 = 4.0 * Math.Sqrt(elevationAboveLandPosition);
				double velocityDimensionlessStateTerm = 2.0 * (dimensionalTimeTerm + dimensionlessState.Velocity);
				double lambda2Term = Math.Max(Math.Abs(computed2 - computed), Math.Abs(velocityDimensionlessStateTerm - lambda2));
				computed = 0.5 * (computed + computed2);
				lambda2 = 0.5 * (lambda2 + velocityDimensionlessStateTerm);
				if (lambda2Term <= 1E-10)
				{
					break;
				}
			}
			dimensionlessState = EvaluateTransform(computed, lambda2);
			double scaledElevation = 500.0 * dimensionlessState.Elevation;
			return new AnalyticalState(Velocity: Math.Sqrt(4905.0) * dimensionlessState.Velocity, Stage: Math.Max(scaledElevation, pointTerm));
		}

		private static double InitialSurface(double landPosition)
		{
			double landPositionOffset = 0.0 - landPosition;
			double computed = 0.006 * Math.Exp(-0.4444 * Math.Pow(landPositionOffset - 4.1209, 2.0));
			double computed2 = 0.018 * Math.Exp(-4.0 * Math.Pow(landPositionOffset - 1.6384, 2.0));
			return 500.0 * (computed - computed2);
		}

		private void SolveShoreline(double dimensionlessTime, out double elevation, out double lambda)
		{
			lambda = 2.0 * dimensionlessTime;
			for (int i = 0; i < 12; i++)
			{
				double dimensionlessTimeVelocityTerm = 2.0 * (dimensionlessTime + EvaluateTransform(0.0, lambda).Velocity);
				double lambdaTerm = Math.Abs(dimensionlessTimeVelocityTerm - lambda);
				lambda = 0.5 * (lambda + dimensionlessTimeVelocityTerm);
				if (lambdaTerm <= 1E-10)
				{
					break;
				}
			}
			elevation = EvaluateTransform(0.0, lambda).Elevation;
		}

		private DimensionlessState EvaluateTransform(double sigma, double lambda)
		{
			double computed = 0.0;
			double computed2 = 0.0;
			for (int i = 0; i < _omega.Length; i++)
			{
				double omega = _omega[i];
				double weightedSpectrum = _weightedSpectrum[i];
				double omegaTimesLambda = omega * lambda;
				computed += weightedSpectrum * Bessel.J0(omega * sigma) * Math.Cos(omegaTimesLambda);
				double j1OverOmegaTimesSigma = ((omega == 0.0 || sigma == 0.0) ? 0.5 : (Bessel.J1(omega * sigma) / (omega * sigma)));
				computed2 += weightedSpectrum * j1OverOmegaTimesSigma * Math.Sin(omegaTimesLambda);
			}
			double computed3 = computed2;
			return new DimensionlessState(-0.25 * computed - 0.5 * computed3 * computed3, computed3);
		}

		private void BuildSpectrum()
		{
			double computed = 0.01;
			double computed2 = 0.01;
			for (int i = 0; i <= 1600; i++)
			{
				double omega = (double)i * computed2;
				_omega[i] = omega;
				double computed3 = 0.0;
				for (int j = 0; j <= 1600; j++)
				{
					double computed4 = (double)j * computed;
					double simpsonCoefficient = SimpsonCoefficient(j, 1600);
					computed3 += simpsonCoefficient * computed4 * computed4 * InitialTransform(computed4) * Bessel.J1(omega * computed4);
				}
				computed3 *= computed / 3.0;
				_weightedSpectrum[i] = computed2 / 3.0 * SimpsonCoefficient(i, 1600) * computed3;
			}
		}

		private static double InitialTransform(double sigma)
		{
			double sigmaPolynomial = sigma * sigma;
			double computed = sigmaPolynomial - 65.9344;
			double computed2 = sigmaPolynomial - 26.2144;
			double computed3 = -0.0026664 * computed / 16.0 * Math.Exp(-0.4444 * computed * computed / 256.0);
			double computed4 = 0.072 * computed2 / 16.0 * Math.Exp(-4.0 * computed2 * computed2 / 256.0);
			return computed3 + computed4;
		}

		private static double SimpsonCoefficient(int index, int intervals)
		{
			if (index == 0 || index == intervals)
			{
				return 1.0;
			}
			if (index % 2 != 0)
			{
				return 4.0;
			}
			return 2.0;
		}
	}

	public const double BeachSlope = 0.1;

	public const double ReferenceLength = 5000.0;

	public const double ShorelineX = 50000.0;

	public const double VerticalScale = 500.0;

	private const double Gravity = 9.81;

	private const double CrestHeight = 0.006;

	private const double CrestShape = 0.4444;

	private const double CrestLocation = 4.1209;

	private const double TroughHeight = 0.018;

	private const double TroughShape = 4.0;

	private const double TroughLocation = 1.6384;

	private static readonly CarrierGreenspanSolution _analyticalSolution = new CarrierGreenspanSolution();

	public static LongWaveRunupParams Default()
	{
		return new LongWaveRunupParams
		{
			CellsWide = 1008,
			CellsTall = 3,
			CellSize = 50.0,
			NValue = 0f,
			SolveDt = 0.04,
			SolveDuration = 220.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return (float)(0.1 * (pt.X - 50000.0));
	}

	public double InitialWave(Point pt)
	{
		double ptTerm = (50000.0 - pt.X) / 5000.0;
		double computed = 0.006 * Math.Exp(-0.4444 * Math.Pow(ptTerm - 4.1209, 2.0));
		double computed2 = 0.018 * Math.Exp(-4.0 * Math.Pow(ptTerm - 1.6384, 2.0));
		return 500.0 * (computed - computed2);
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)InitialWave(pt);
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) => (float)_analyticalSolution.Evaluate(pt, time).Stage;
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double time) => new Vector(_analyticalSolution.Evaluate(pt, time).Velocity, 0.0);
	}

	public override BoundaryConditionLine GetUpstreamBC(BoundaryCondition boundaryCondition, Polyline polyline)
	{
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantStage(boundaryCondition, polyline, 0.0);
		boundaryConditionLine.Name = "Offshore stage";
		return boundaryConditionLine;
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return new _003C_003Ez__ReadOnlySingleElementList<NamedResultPoint>(new NamedResultPoint(new Point(50000.0, SystemExtent.Center.Y), "Still-water shoreline"));
	}

	public override IReadOnlyList<NamedResultLine> GetResultLines()
	{
		return new _003C_003Ez__ReadOnlySingleElementList<NamedResultLine>(new NamedResultLine(Polyline.FromSegment(new Segment(new Point(SystemExtent.MinX, SystemExtent.Center.Y), new Point(SystemExtent.MaxX, SystemExtent.Center.Y))), "Beach centerline"));
	}
}
