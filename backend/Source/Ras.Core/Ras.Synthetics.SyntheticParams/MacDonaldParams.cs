using System;
using Geospatial.Vectors;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class MacDonaldParams : InOutPlanarParams
{
	public double UnitDischarge = 20.0;

	public double DepthBase = 1.2;

	public double DepthAmp = 0.3;

	public double DepthSharpness = 100.0;

	private double[] anaStage;

	private double[] anaVelocity;

	private double[] anaBed;

	private readonly object _precomputeLock = new object();

	private double dx = 0.01;

	private const double G = 9.81;

	public override UnitSystem Units => UnitSystem.SI;

	public new static MacDonaldParams Default()
	{
		MacDonaldParams macDonaldParams = new MacDonaldParams();
		macDonaldParams.CellsWide = 100;
		macDonaldParams.CellsTall = 1;
		macDonaldParams.CellSize = 10.0;
		macDonaldParams.RampSeconds = 100.0;
		macDonaldParams.NValue = 0.03f;
		macDonaldParams.Slope = 0.0;
		macDonaldParams.SolveDt = 0.5;
		macDonaldParams.SolveDuration = 2000.0;
		macDonaldParams.InitFlowAndStageBCs();
		return macDonaldParams;
	}

	public static MacDonaldParams Supercritical()
	{
		MacDonaldParams macDonaldParams = Default();
		macDonaldParams.UnitDischarge = 2.0;
		macDonaldParams.DepthBase = 0.65;
		macDonaldParams.DepthAmp = 0.1;
		macDonaldParams.DepthSharpness = 64.0;
		macDonaldParams.NValue = 0.0328f;
		macDonaldParams.CellsWide = 200;
		macDonaldParams.CellSize = 0.5;
		macDonaldParams.RampSeconds = 20.0;
		macDonaldParams.SolveDt = 0.05;
		macDonaldParams.SolveDuration = 300.0;
		macDonaldParams.InitFlowAndStageBCs();
		return macDonaldParams;
	}

	public void InitFlowAndStageBCs()
	{
		double x = (double)CellsWide * CellSize;
		UpstreamFlow = UnitDischarge * (double)CellsTall * CellSize;
		DownstreamStage = DepthAt(0.0);
		double x2 = DepthAt(x);
		UpstreamEGSlope = (double)(NValue * NValue) * UnitDischarge * UnitDischarge / Math.Pow(x2, 3.3333333333333335);
	}

	public override float TerrainElevation(Point pt)
	{
		EnsurePrecomputed();
		return InterpolateFromAnalytical(pt, anaBed);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => ComputeStage(pt);
	}

	private float ComputeStage(Point pt)
	{
		EnsurePrecomputed();
		return InterpolateFromAnalytical(pt, anaStage);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => ComputeVelocity(pt);
	}

	public override Func<Point, double, float> AnalyticalVelocityMagnitudeFunction()
	{
		return (Point pt, double _) => (float)ComputeVelocity(pt).Length;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point pt, double _) => (float)ComputeVelocity(pt).X;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point _, double _) => 0f;
	}

	private Vector ComputeVelocity(Point p)
	{
		EnsurePrecomputed();
		return new Vector(InterpolateFromAnalytical(p, anaVelocity), 0.0);
	}

	private double DepthAt(double x)
	{
		double cellsWideTimesCellSize = (double)CellsWide * CellSize;
		double unitDischargeTerm = Math.Pow(UnitDischarge * UnitDischarge / 9.81, 1.0 / 3.0);
		double computed = x / cellsWideTimesCellSize - 0.5;
		return unitDischargeTerm * (DepthBase + DepthAmp * Math.Exp((0.0 - DepthSharpness) * computed * computed));
	}

	private double DepthDerivative(double x)
	{
		double cellsWideTimesCellSize = (double)CellsWide * CellSize;
		double unitDischargeTerm = Math.Pow(UnitDischarge * UnitDischarge / 9.81, 1.0 / 3.0);
		double computed = x / cellsWideTimesCellSize - 0.5;
		double depthSharpnessTerm = Math.Exp((0.0 - DepthSharpness) * computed * computed);
		return unitDischargeTerm * DepthAmp * depthSharpnessTerm * (-2.0 * DepthSharpness * computed / cellsWideTimesCellSize);
	}

	private double BedSlopeAt(double x)
	{
		double depthAt = DepthAt(x);
		double depthDerivative = DepthDerivative(x);
		double unitDischarge = UnitDischarge;
		double nValue = NValue;
		double nValueUnitDischargeTerm = nValue * nValue * unitDischarge * unitDischarge / Math.Pow(depthAt, 3.3333333333333335);
		double unitDischargePolynomialOverDepthAtPolynomial = unitDischarge * unitDischarge / (9.81 * depthAt * depthAt * depthAt);
		return nValueUnitDischargeTerm - (1.0 - unitDischargePolynomialOverDepthAtPolynomial) * depthDerivative;
	}

	private void EnsurePrecomputed()
	{
		if (anaStage != null)
		{
			return;
		}
		lock (_precomputeLock)
		{
			if (anaStage == null)
			{
				PrecomputeAnalytical();
			}
		}
	}

	private float InterpolateFromAnalytical(Point p, double[] anaValues)
	{
		int anaValueLength = anaValues.Length;
		double anaValueLengthDxTerm = (double)(anaValueLength - 1) * dx;
		double computed = anaValueLengthDxTerm - p.X;
		if (computed < 0.0)
		{
			computed = 0.0;
		}
		if (computed > anaValueLengthDxTerm)
		{
			computed = anaValueLengthDxTerm;
		}
		int anaValueLengthOffset = (int)(computed / dx);
		if (anaValueLengthOffset >= anaValueLength - 1)
		{
			anaValueLengthOffset = anaValueLength - 2;
		}
		double dxTerm = (computed - (double)anaValueLengthOffset * dx) / dx;
		return (float)(anaValues[anaValueLengthOffset] * (1.0 - dxTerm) + anaValues[anaValueLengthOffset + 1] * dxTerm);
	}

	private void PrecomputeAnalytical()
	{
		double cellsWideTimesCellSize = (double)CellsWide * CellSize;
		int count = (int)Math.Ceiling(cellsWideTimesCellSize / dx);
		double[] anaBedArray = new double[count + 1];
		double[] anaStageArray = new double[count + 1];
		double[] anaVelocityArray = new double[count + 1];
		double unitDischarge = UnitDischarge;
		double anaBedArray2 = 0.0;
		double x = 0.0;
		double depthAt = DepthAt(x);
		anaBedArray[0] = anaBedArray2;
		anaStageArray[0] = anaBedArray2 + depthAt;
		anaVelocityArray[0] = unitDischarge / depthAt;
		for (int i = 0; i < count; i++)
		{
			double dxTerm = (double)i * dx;
			double computed = dxTerm + dx;
			if (computed > cellsWideTimesCellSize)
			{
				computed = cellsWideTimesCellSize;
			}
			double computed2 = computed - dxTerm;
			double bedSlopeAt = BedSlopeAt(dxTerm);
			double bedSlopeAt2 = BedSlopeAt(dxTerm + 0.5 * computed2);
			double bedSlopeAt3 = BedSlopeAt(dxTerm + 0.5 * computed2);
			double bedSlopeAt4 = BedSlopeAt(dxTerm + computed2);
			anaBedArray2 += computed2 / 6.0 * (bedSlopeAt + 2.0 * bedSlopeAt2 + 2.0 * bedSlopeAt3 + bedSlopeAt4);
			double depthAt2 = DepthAt(computed);
			anaBedArray[i + 1] = anaBedArray2;
			anaStageArray[i + 1] = anaBedArray2 + depthAt2;
			anaVelocityArray[i + 1] = unitDischarge / depthAt2;
		}
		anaBed = anaBedArray;
		anaVelocity = anaVelocityArray;
		anaStage = anaStageArray;
	}
}
