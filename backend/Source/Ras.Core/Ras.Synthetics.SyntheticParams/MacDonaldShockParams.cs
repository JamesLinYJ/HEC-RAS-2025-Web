using System;
using Geospatial.Vectors;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class MacDonaldShockParams : InOutPlanarParams
{
	public double UnitDischarge = 2.0;

	public double JumpLocation = 500.0;

	public double SuperDepth = 0.55;

	public double SubDepthAtOutlet = 1.2;

	private double[] anaStage;

	private double[] anaVelocity;

	private double[] anaBed;

	private readonly object _precomputeLock = new object();

	private double dx = 0.01;

	private const double G = 9.81;

	public override UnitSystem Units => UnitSystem.SI;

	public double SubDepthAtJump
	{
		get
		{
			double unitDischarge = UnitDischarge;
			double superDepth = SuperDepth;
			double unitDischargePolynomialOverSuperDepthPolynomial = unitDischarge * unitDischarge / (9.81 * superDepth * superDepth * superDepth);
			return superDepth / 2.0 * (Math.Sqrt(1.0 + 8.0 * unitDischargePolynomialOverSuperDepthPolynomial) - 1.0);
		}
	}

	public new static MacDonaldShockParams Default()
	{
		MacDonaldShockParams macDonaldShockParams = new MacDonaldShockParams();
		macDonaldShockParams.CellsWide = 100;
		macDonaldShockParams.CellsTall = 1;
		macDonaldShockParams.CellSize = 10.0;
		macDonaldShockParams.RampSeconds = 100.0;
		macDonaldShockParams.NValue = 0.02f;
		macDonaldShockParams.SolveDt = 0.5;
		macDonaldShockParams.SolveDuration = 2000.0;
		macDonaldShockParams.InitFlowAndStageBCs();
		return macDonaldShockParams;
	}

	public void InitFlowAndStageBCs()
	{
		UpstreamFlow = UnitDischarge * (double)CellsTall * CellSize;
		DownstreamStage = SubDepthAtOutlet;
		UpstreamEGSlope = (double)(NValue * NValue) * UnitDischarge * UnitDischarge / Math.Pow(SuperDepth, 3.3333333333333335);
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

	private Vector ComputeVelocity(Point p)
	{
		EnsurePrecomputed();
		return new Vector(InterpolateFromAnalytical(p, anaVelocity), 0.0);
	}

	private double DepthAt(double x, bool subSide)
	{
		if (subSide)
		{
			double jumpLocationTerm = x / JumpLocation;
			return SubDepthAtOutlet + (SubDepthAtJump - SubDepthAtOutlet) * jumpLocationTerm;
		}
		return SuperDepth;
	}

	private double DepthDerivative(bool subSide)
	{
		if (subSide)
		{
			return (SubDepthAtJump - SubDepthAtOutlet) / JumpLocation;
		}
		return 0.0;
	}

	private double BedSlopeAt(double x, bool subSide)
	{
		double depthAt = DepthAt(x, subSide);
		double depthDerivative = DepthDerivative(subSide);
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
		int countEnd = (int)Math.Ceiling(cellsWideTimesCellSize / dx);
		int countStart = (int)Math.Round(JumpLocation / dx);
		if (countStart < 1 || countStart > countEnd - 1)
		{
			throw new InvalidOperationException("JumpLocation must be strictly inside the channel.");
		}
		double[] anaBedArray = new double[countEnd + 1];
		double[] anaStageArray = new double[countEnd + 1];
		double[] anaVelocityArray = new double[countEnd + 1];
		double unitDischarge = UnitDischarge;
		double anaBedArray2 = 0.0;
		double depthAt = DepthAt(0.0, subSide: true);
		anaBedArray[0] = anaBedArray2;
		anaStageArray[0] = anaBedArray2 + depthAt;
		anaVelocityArray[0] = unitDischarge / depthAt;
		for (int i = 0; i < countStart; i++)
		{
			double dxTerm = (double)i * dx;
			double x = dxTerm + dx;
			double dx2 = dx;
			double bedSlopeAt = BedSlopeAt(dxTerm, subSide: true);
			double bedSlopeAt2 = BedSlopeAt(dxTerm + 0.5 * dx2, subSide: true);
			double bedSlopeAt3 = BedSlopeAt(dxTerm + 0.5 * dx2, subSide: true);
			double bedSlopeAt4 = BedSlopeAt(x, subSide: true);
			anaBedArray2 += dx2 / 6.0 * (bedSlopeAt + 2.0 * bedSlopeAt2 + 2.0 * bedSlopeAt3 + bedSlopeAt4);
			double depthAt2 = DepthAt(x, subSide: true);
			anaBedArray[i + 1] = anaBedArray2;
			anaStageArray[i + 1] = anaBedArray2 + depthAt2;
			anaVelocityArray[i + 1] = unitDischarge / depthAt2;
		}
		for (int j = countStart; j < countEnd; j++)
		{
			double dxTerm2 = (double)j * dx;
			double dxTerm3 = Math.Min(dxTerm2 + dx, cellsWideTimesCellSize);
			double computed = dxTerm3 - dxTerm2;
			double bedSlopeAt5 = BedSlopeAt(dxTerm2, subSide: false);
			double bedSlopeAt6 = BedSlopeAt(dxTerm2 + 0.5 * computed, subSide: false);
			double bedSlopeAt7 = BedSlopeAt(dxTerm2 + 0.5 * computed, subSide: false);
			double bedSlopeAt8 = BedSlopeAt(dxTerm3, subSide: false);
			anaBedArray2 += computed / 6.0 * (bedSlopeAt5 + 2.0 * bedSlopeAt6 + 2.0 * bedSlopeAt7 + bedSlopeAt8);
			double depthAt3 = DepthAt(dxTerm3, subSide: false);
			anaBedArray[j + 1] = anaBedArray2;
			anaStageArray[j + 1] = anaBedArray2 + depthAt3;
			anaVelocityArray[j + 1] = unitDischarge / depthAt3;
		}
		anaBed = anaBedArray;
		anaVelocity = anaVelocityArray;
		anaStage = anaStageArray;
	}
}
