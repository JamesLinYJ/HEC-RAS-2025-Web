using System;
using System.Threading;
using Geospatial.Vectors;
using Utility.Units;

namespace Ras.Synthetics.SyntheticParams;

public class BumpParams : InOutPlanarParams
{
	public enum BumpFlowRegime
	{
		Subcritical,
		TranscriticalNoShock,
		TranscriticalWithShock
	}

	public BumpFlowRegime Regime;

	public double TotalEnergy = 2.0;

	public double UnitDischarge = 4.42;

	public double BumpCenter = 10.0;

	public double BumpHeight = 0.2;

	public double BumpHalfWidth = 2.0;

	private double[] _anaStage;

	private double[] _anaVelocity;

	private readonly Lock _precomputeLock = new Lock();

	private double dx = 0.01;

	private const double G = 9.8065;

	private const int NewtonMaxIterations = 50;

	private const double NewtonTolerance = 1E-09;

	public override UnitSystem Units => UnitSystem.SI;

	public static BumpParams Subcritical()
	{
		BumpParams bumpParams = new BumpParams();
		bumpParams.Regime = BumpFlowRegime.Subcritical;
		bumpParams.CellsWide = 200;
		bumpParams.CellsTall = 3;
		bumpParams.CellSize = 0.1;
		bumpParams.RampSeconds = 50.0;
		bumpParams.NValue = 0f;
		bumpParams.Slope = 0.0;
		bumpParams.UnitDischarge = 4.42;
		bumpParams.UpstreamEGSlope = 0.01;
		bumpParams.UpstreamFlow = 1.3259999999999998;
		bumpParams.DownstreamStage = 2.0;
		bumpParams.SolveDt = 0.02;
		bumpParams.SolveDuration = 500.0;
		bumpParams.InitTotalEnergy();
		return bumpParams;
	}

	public static BumpParams TranscriticalNoShock()
	{
		BumpParams bumpParams = new BumpParams();
		bumpParams.Regime = BumpFlowRegime.TranscriticalNoShock;
		bumpParams.CellsWide = 200;
		bumpParams.CellsTall = 3;
		bumpParams.CellSize = 0.1;
		bumpParams.RampSeconds = 50.0;
		bumpParams.NValue = 0f;
		bumpParams.Slope = 0.0;
		bumpParams.UnitDischarge = 1.53;
		bumpParams.UpstreamFlow = 0.45899999999999996;
		bumpParams.DownstreamStage = 0.405781;
		bumpParams.SolveDt = 0.02;
		bumpParams.SolveDuration = 500.0;
		bumpParams.InitTotalEnergy();
		return bumpParams;
	}

	public static BumpParams TranscriticalWithShock()
	{
		BumpParams bumpParams = new BumpParams();
		bumpParams.Regime = BumpFlowRegime.TranscriticalWithShock;
		bumpParams.CellsWide = 200;
		bumpParams.CellsTall = 3;
		bumpParams.CellSize = 0.1;
		bumpParams.RampSeconds = 50.0;
		bumpParams.NValue = 0f;
		bumpParams.Slope = 0.0;
		bumpParams.UnitDischarge = 0.18;
		bumpParams.UpstreamFlow = 0.054;
		bumpParams.DownstreamStage = 0.33;
		bumpParams.SolveDt = 0.02;
		bumpParams.SolveDuration = 500.0;
		bumpParams.InitTotalEnergy();
		return bumpParams;
	}

	private void InitTotalEnergy()
	{
		if (Regime == BumpFlowRegime.Subcritical)
		{
			double unitDischargeOverDownstreamStage = UnitDischarge / DownstreamStage;
			TotalEnergy = DownstreamStage + unitDischargeOverDownstreamStage * unitDischargeOverDownstreamStage / 19.613;
		}
		else
		{
			double unitDischargeTerm = Math.Pow(UnitDischarge * UnitDischarge / 9.8065, 1.0 / 3.0);
			TotalEnergy = BumpHeight + 1.5 * unitDischargeTerm;
		}
	}

	public override float TerrainElevation(Point pt)
	{
		return (float)BedElevation(SystemExtent.MaxX - pt.X);
	}

	private double BedElevation(double x)
	{
		double bumpCenterTerm = x - BumpCenter;
		if (Math.Abs(bumpCenterTerm) >= BumpHalfWidth)
		{
			return 0.0;
		}
		double bumpHeightOverBumpHalfWidthPolynomial = BumpHeight / (BumpHalfWidth * BumpHalfWidth);
		double bumpHeightTerm = BumpHeight - bumpHeightOverBumpHalfWidthPolynomial * bumpCenterTerm * bumpCenterTerm;
		if (bumpHeightTerm < 0.0)
		{
			bumpHeightTerm = 0.0;
		}
		return bumpHeightTerm;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) => ComputeStage(pt);
	}

	private float ComputeStage(Point pt)
	{
		EnsurePrecomputed();
		return InterpolateFromAnalytical(pt, _anaStage);
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => ComputeVelocity(pt);
	}

	private Vector ComputeVelocity(Point p)
	{
		EnsurePrecomputed();
		return new Vector(InterpolateFromAnalytical(p, _anaVelocity), 0.0);
	}

	private void EnsurePrecomputed()
	{
		if (_anaStage != null)
		{
			return;
		}
		using (_precomputeLock.EnterScope())
		{
			if (_anaStage == null)
			{
				PrecomputeAnalytical();
			}
		}
	}

	private float InterpolateFromAnalytical(Point p, double[] anaValues)
	{
		int anaStageLength = _anaStage.Length;
		double anaStageLengthDxTerm = (double)(anaStageLength - 1) * dx;
		double computed = anaStageLengthDxTerm - p.X;
		if (computed < 0.0)
		{
			computed = 0.0;
		}
		if (computed > anaStageLengthDxTerm)
		{
			computed = anaStageLengthDxTerm;
		}
		int anaStageLengthOffset = (int)(computed / dx);
		if (anaStageLengthOffset >= anaStageLength - 1)
		{
			anaStageLengthOffset = anaStageLength - 2;
		}
		double dxTerm = (computed - (double)anaStageLengthOffset * dx) / dx;
		return (float)(anaValues[anaStageLengthOffset] * (1.0 - dxTerm) + anaValues[anaStageLengthOffset + 1] * dxTerm);
	}

	private void PrecomputeAnalytical()
	{
		int count = (int)Math.Ceiling(SystemExtent.MaxX / dx);
		double[] anaStage = new double[count + 1];
		double[] anaVelocity = new double[count + 1];
		double unitDischarge = UnitDischarge;
		double naN = double.NaN;
		double downstreamStageTerm = 0.0;
		if (Regime == BumpFlowRegime.TranscriticalWithShock)
		{
			double unitDischargeOverDownstreamStage = unitDischarge / DownstreamStage;
			downstreamStageTerm = DownstreamStage + unitDischargeOverDownstreamStage * unitDischargeOverDownstreamStage / 19.613;
			naN = FindShockLocation(unitDischarge, downstreamStageTerm);
		}
		for (int i = 0; i <= count; i++)
		{
			double dxTerm = (double)i * dx;
			double bedElevation = BedElevation(dxTerm);
			double regimeSubcriticalTerm = Regime switch
			{
				BumpFlowRegime.Subcritical => SolveDepthNewton(TotalEnergy - bedElevation, unitDischarge, isSubcritical: true), 
				BumpFlowRegime.TranscriticalNoShock => SolveDepthNewton(TotalEnergy - bedElevation, unitDischarge, dxTerm >= BumpCenter), 
				BumpFlowRegime.TranscriticalWithShock => (!(dxTerm >= BumpCenter)) ? ((!(dxTerm >= naN)) ? SolveDepthNewton(downstreamStageTerm - bedElevation, unitDischarge, isSubcritical: true) : SolveDepthNewton(TotalEnergy - bedElevation, unitDischarge, isSubcritical: false)) : SolveDepthNewton(TotalEnergy - bedElevation, unitDischarge, isSubcritical: true), 
				_ => throw new InvalidOperationException($"Unknown regime: {Regime}"), 
			};
			anaStage[i] = bedElevation + regimeSubcriticalTerm;
			anaVelocity[i] = unitDischarge / regimeSubcriticalTerm;
		}
		_anaVelocity = anaVelocity;
		_anaStage = anaStage;
	}

	private double FindShockLocation(double q, double eSubTarget)
	{
		double computed = 0.0;
		double bumpHeight = BumpHeight;
		double residual = Residual(computed);
		double residual2 = Residual(bumpHeight);
		if (residual > 0.0)
		{
			return BumpCenter - BumpHalfWidth;
		}
		if (residual2 < 0.0)
		{
			return BumpCenter;
		}
		for (int i = 0; i < 80; i++)
		{
			double bumpHeight2 = 0.5 * (computed + bumpHeight);
			double residual3 = Residual(bumpHeight2);
			if (residual3 > 0.0)
			{
				bumpHeight = bumpHeight2;
				residual2 = residual3;
			}
			else
			{
				computed = bumpHeight2;
			}
			if (bumpHeight - computed < 1E-10)
			{
				break;
			}
		}
		double bumpHeightTerm = 0.5 * (computed + bumpHeight);
		double bumpHeightOverBumpHalfWidthPolynomial = BumpHeight / (BumpHalfWidth * BumpHalfWidth);
		double bumpHeightTerm2 = 0.0 - Math.Sqrt(Math.Max(0.0, (BumpHeight - bumpHeightTerm) / bumpHeightOverBumpHalfWidthPolynomial));
		return BumpCenter + bumpHeightTerm2;
		double Residual(double zJump)
		{
			double totalEnergyAboveZJump = TotalEnergy - zJump;
			if (totalEnergyAboveZJump <= 0.0)
			{
				return double.NegativeInfinity;
			}
			double conjugateBelanger = ConjugateBelanger(SolveDepthNewton(totalEnergyAboveZJump, q, isSubcritical: false), q);
			double conjugateBelangerTerm = q / conjugateBelanger;
			return zJump + conjugateBelanger + conjugateBelangerTerm * conjugateBelangerTerm / 19.613 - eSubTarget;
		}
	}

	private static double ConjugateBelanger(double hSuper, double q)
	{
		double hSuperTerm = q * q / (9.8065 * hSuper * hSuper * hSuper);
		return hSuper * 0.5 * (Math.Sqrt(1.0 + 8.0 * hSuperTerm) - 1.0);
	}

	private static double SolveDepthNewton(double specificEnergy, double q, bool isSubcritical)
	{
		double computed = q * q / 19.613;
		double specificEnergy2 = (isSubcritical ? specificEnergy : Math.Pow(computed, 1.0 / 3.0));
		for (int i = 0; i < 50; i++)
		{
			double specificEnergy2Polynomial = specificEnergy2 * specificEnergy2;
			double specificEnergy2SpecificEnergyTerm = specificEnergy2Polynomial * specificEnergy2 - specificEnergy * specificEnergy2Polynomial + computed;
			double specificEnergySpecificEnergy2Term = 3.0 * specificEnergy2Polynomial - 2.0 * specificEnergy * specificEnergy2;
			double computed2 = specificEnergy2SpecificEnergyTerm / specificEnergySpecificEnergy2Term;
			specificEnergy2 -= computed2;
			if (Math.Abs(computed2) < 1E-09)
			{
				break;
			}
		}
		return specificEnergy2;
	}
}
