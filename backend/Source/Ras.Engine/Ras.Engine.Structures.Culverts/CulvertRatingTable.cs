using System;
using Utility.Units;

namespace Ras.Engine.Structures.Culverts;

public class CulvertRatingTable
{
	private const int numCulvLevels = 11;

	private readonly int numDSWSELs;

	private readonly double ElevResolution;

	private readonly double[] DownstreamElevs;

	private int numFlows = 21;

	private double[] Flows;

	private const double FlowTableGrowthFactor = 1.25;

	private const int MinColumnsToAdd = 4;

	private const double ApproxMaxCulvertVelocity = 15.0;

	private double[,] USEnergyGrade;

	private double[,] DownstreamVel;

	private readonly double MinTableElev;

	private readonly double MaxTableElev;

	private double MaxTableFlow;

	private readonly double MinElevForFlow;

	private readonly double OutletControlCoef;

	private readonly double InletControlCoef1;

	private readonly double InletControlCoef2;

	private readonly double FullCulvertArea;

	private readonly FlowOrientedCulvert _culvert;

	private readonly CulvertSolverControl _control;

	private int _lastFlowIdx;

	public CulvertRatingTable(string name, FlowOrientedCulvert culvert, CulvertSolverControl control)
	{
		_culvert = culvert;
		_control = control;
		ValidateData(name, culvert, control);
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = culvert.EffectiveRise,
			Qtotal = 1.0
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		FullCulvertArea = culvertXSection.Area;
		double gravityControlTerm = 2.0 * control.Grav * FullCulvertArea * FullCulvertArea;
		double scaledGravityOverMannCoefPolynomial = 2.0 * control.Grav / (control.MannCoef * control.MannCoef);
		double coefEntranceLossCulvertTerm = culvert.CoefEntranceLoss + culvert.CoefExitLoss + scaledGravityOverMannCoefPolynomial * culvertXSection.Mann * culvertXSection.Mann * culvert.Length / Math.Pow(culvertXSection.HydRad, 1.3333333333333333);
		OutletControlCoef = Math.Sqrt(gravityControlTerm / coefEntranceLossCulvertTerm);
		double unitSysControlTerm = ((control.UnitSys != UnitSystem.USCustomary) ? Math.Sqrt(3.28084) : 1.0);
		InletControlCoef1 = FullCulvertArea * Math.Sqrt(culvert.EffectiveRise) / (unitSysControlTerm * Math.Sqrt(culvert.InletCoefUS.C));
		InletControlCoef2 = 0.0 - culvert.InletCoefUS.Y - culvert.InletCoefUS.SlopeCorrection * culvert.Slope;
		MinTableElev = culvert.InvertDS;
		MinElevForFlow = Math.Max(culvert.InvertUS, culvert.InvertDS);
		MaxTableElev = MinElevForFlow + culvert.EffectiveRise;
		MaxTableFlow = FullCulvertArea * 15.0;
		ElevResolution = culvert.EffectiveRise / 10.0;
		numDSWSELs = (int)Math.Ceiling((MaxTableElev - MinTableElev) / ElevResolution) + 1;
		DownstreamElevs = new double[numDSWSELs];
		for (int i = 0; i < numDSWSELs - 1; i++)
		{
			DownstreamElevs[i] = MinTableElev + (double)i * ElevResolution;
		}
		DownstreamElevs[numDSWSELs - 1] = MaxTableElev;
		Flows = new double[numFlows];
		Flows[0] = 0.0;
		Flows[1] = MaxTableFlow / Math.Pow(1.25, numFlows - 2);
		for (int j = 2; j < numFlows; j++)
		{
			Flows[j] = Flows[j - 1] * 1.25;
		}
		USEnergyGrade = new double[numDSWSELs, numFlows];
		DownstreamVel = new double[numDSWSELs, numFlows];
		CulvertSolution culvertSolution = new CulvertSolution(control)
		{
			IsReverseFlow = false,
			VelHeadDS = 0.0
		};
		for (int k = 0; k < numDSWSELs; k++)
		{
			for (int l = 0; l < numFlows; l++)
			{
				culvertSolution.Qoriented = Flows[l];
				culvertSolution.WSEDS = DownstreamElevs[k];
				CulvertEnergyGradeSolver.Compute(culvert, control, culvertSolution);
				USEnergyGrade[k, l] = culvertSolution.EGUS;
				CulvertXSection culvertXSection2 = new CulvertXSection
				{
					Depth = culvertSolution.WSEOutlet - culvert.InvertDS,
					Qtotal = Flows[l]
				};
				CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection2);
				DownstreamVel[k, l] = culvertXSection2.Vel;
			}
		}
	}

	private static void ValidateData(string name, FlowOrientedCulvert culvert, CulvertSolverControl control)
	{
		if (culvert.EffectiveRise <= 0.0)
		{
			throw new SolverException("Problem with culvert: " + name + ". Culvert rise - depth blocked must be > 0.");
		}
		if (culvert.Mann <= 0.0)
		{
			throw new SolverException("Problem with culvert: " + name + ". Manning's roughness coefficient must be > 0.");
		}
	}

	public (double flow, double vel, double dFlow_dUp, double dFlow_dDown) GetFlowVelDerivs(double upEG, double dnWS)
	{
		double interpolate = 0.0;
		double interpolate2 = 0.0;
		double flowStepOverInterpolateDs6AboveInterpolateDs5 = 0.0;
		double usEnergyGradeDownstreamElevsTerm = 0.0;
		if (upEG <= MinElevForFlow)
		{
			return (flow: interpolate, vel: interpolate2, dFlow_dUp: flowStepOverInterpolateDs6AboveInterpolateDs5, dFlow_dDown: usEnergyGradeDownstreamElevsTerm);
		}
		if (upEG - dnWS < _control.ZeroFlowEgDiffTolerance)
		{
			return (flow: interpolate, vel: interpolate2, dFlow_dUp: flowStepOverInterpolateDs6AboveInterpolateDs5, dFlow_dDown: usEnergyGradeDownstreamElevsTerm);
		}
		if (dnWS >= MaxTableElev)
		{
			return FullFlowAnalytical(upEG, dnWS);
		}
		double d = (dnWS - DownstreamElevs[0]) / ElevResolution;
		int ordinal = Math.Max(0, (int)Math.Floor(d));
		int downstreamElevsTerm = Math.Min(ordinal + 1, DownstreamElevs.Length - 1);
		double dsFrac = 0.0;
		if (ordinal != downstreamElevsTerm)
		{
			dsFrac = Math.Max(0.0, dnWS - DownstreamElevs[ordinal]) / (DownstreamElevs[downstreamElevsTerm] - DownstreamElevs[ordinal]);
		}
		CheckTableLimits(upEG, ordinal, downstreamElevsTerm, dsFrac);
		int lastFlowIdxFlowsTerm = Math.Clamp(_lastFlowIdx, 1, Flows.Length - 1);
		double interpolateDs = InterpolateDs(USEnergyGrade, ordinal, downstreamElevsTerm, dsFrac, lastFlowIdxFlowsTerm - 1);
		double interpolateDs2 = InterpolateDs(USEnergyGrade, ordinal, downstreamElevsTerm, dsFrac, lastFlowIdxFlowsTerm);
		int i;
		if (upEG >= interpolateDs && upEG <= interpolateDs2)
		{
			i = lastFlowIdxFlowsTerm;
		}
		else if (upEG > interpolateDs2)
		{
			for (i = lastFlowIdxFlowsTerm + 1; i < Flows.Length; i++)
			{
				double interpolateDs3 = InterpolateDs(USEnergyGrade, ordinal, downstreamElevsTerm, dsFrac, i);
				if (upEG <= interpolateDs3)
				{
					break;
				}
			}
		}
		else
		{
			for (i = lastFlowIdxFlowsTerm - 1; i > 0; i--)
			{
				double interpolateDs4 = InterpolateDs(USEnergyGrade, ordinal, downstreamElevsTerm, dsFrac, i - 1);
				if (upEG >= interpolateDs4)
				{
					break;
				}
			}
		}
		i = (_lastFlowIdx = Math.Clamp(i, 1, Flows.Length - 1));
		double interpolateDs5 = InterpolateDs(USEnergyGrade, ordinal, downstreamElevsTerm, dsFrac, i - 1);
		double interpolateDs6 = InterpolateDs(USEnergyGrade, ordinal, downstreamElevsTerm, dsFrac, i);
		interpolate = CulvertMath.Interpolate(interpolateDs5, interpolateDs6, upEG, Flows[i - 1], Flows[i]);
		double y = InterpolateDs(DownstreamVel, ordinal, downstreamElevsTerm, dsFrac, i - 1);
		double y2 = InterpolateDs(DownstreamVel, ordinal, downstreamElevsTerm, dsFrac, i);
		interpolate2 = CulvertMath.Interpolate(interpolateDs5, interpolateDs6, upEG, y, y2);
		flowStepOverInterpolateDs6AboveInterpolateDs5 = ((interpolateDs6 - interpolateDs5 != 0.0) ? ((Flows[i] - Flows[i - 1]) / (interpolateDs6 - interpolateDs5)) : 0.0);
		if (downstreamElevsTerm != ordinal && flowStepOverInterpolateDs6AboveInterpolateDs5 != 0.0)
		{
			usEnergyGradeDownstreamElevsTerm = (0.0 - (USEnergyGrade[downstreamElevsTerm, i] - USEnergyGrade[ordinal, i]) / (DownstreamElevs[downstreamElevsTerm] - DownstreamElevs[ordinal])) * flowStepOverInterpolateDs6AboveInterpolateDs5;
		}
		return (flow: interpolate, vel: interpolate2, dFlow_dUp: flowStepOverInterpolateDs6AboveInterpolateDs5, dFlow_dDown: usEnergyGradeDownstreamElevsTerm);
	}

	private void CheckTableLimits(double upEG, int i0, int i1, double dsFrac)
	{
		double interpolateDs = InterpolateDs(USEnergyGrade, i0, i1, dsFrac, Flows.Length - 1);
		if (upEG < interpolateDs)
		{
			return;
		}
		int flowLengthStart = Flows.Length;
		int flowLengthEnd = flowLengthStart;
		double flow = Flows[flowLengthStart - 1];
		while (upEG >= interpolateDs || flowLengthEnd < flowLengthStart + 4)
		{
			flow *= 1.25;
			flowLengthEnd++;
			interpolateDs = EstimateEgAtFlow(flow, i0, i1, dsFrac);
		}
		double[] flows = new double[flowLengthEnd];
		Array.Copy(Flows, flows, flowLengthStart);
		double[,] usEnergyGrade = new double[numDSWSELs, flowLengthEnd];
		double[,] downstreamVel = new double[numDSWSELs, flowLengthEnd];
		for (int j = 0; j < numDSWSELs; j++)
		{
			for (int k = 0; k < flowLengthStart; k++)
			{
				usEnergyGrade[j, k] = USEnergyGrade[j, k];
				downstreamVel[j, k] = DownstreamVel[j, k];
			}
		}
		CulvertSolution culvertSolution = new CulvertSolution(_control)
		{
			IsReverseFlow = false,
			VelHeadDS = 0.0
		};
		double flow2 = Flows[flowLengthStart - 1];
		for (int l = flowLengthStart; l < flowLengthEnd; l++)
		{
			flow2 = (culvertSolution.Qoriented = (flows[l] = flow2 * 1.25));
			for (int m = 0; m < numDSWSELs; m++)
			{
				culvertSolution.WSEDS = DownstreamElevs[m];
				CulvertEnergyGradeSolver.Compute(_culvert, _control, culvertSolution);
				usEnergyGrade[m, l] = culvertSolution.EGUS;
				CulvertXSection culvertXSection = new CulvertXSection
				{
					Depth = culvertSolution.WSEOutlet - _culvert.InvertDS,
					Qtotal = flow2
				};
				CulvertSectionHydraulics.ComputeXSHydraulicProps(_culvert, _control, culvertXSection);
				downstreamVel[m, l] = culvertXSection.Vel;
			}
		}
		Flows = flows;
		USEnergyGrade = usEnergyGrade;
		DownstreamVel = downstreamVel;
		MaxTableFlow = Flows[^1];
	}

	private double EstimateEgAtFlow(double flow, int i0, int i1, double dsFrac)
	{
		CulvertSolution culvertSolution = new CulvertSolution(_control)
		{
			IsReverseFlow = false,
			VelHeadDS = 0.0,
			Qoriented = flow
		};
		culvertSolution.WSEDS = DownstreamElevs[i0];
		CulvertEnergyGradeSolver.Compute(_culvert, _control, culvertSolution);
		double eGUS = culvertSolution.EGUS;
		if (i1 == i0)
		{
			return eGUS;
		}
		culvertSolution.WSEDS = DownstreamElevs[i1];
		CulvertEnergyGradeSolver.Compute(_culvert, _control, culvertSolution);
		double eGUS2 = culvertSolution.EGUS;
		return (1.0 - dsFrac) * eGUS + dsFrac * eGUS2;
	}

	private static double InterpolateDs(double[,] table, int i0, int i1, double dsFrac, int col)
	{
		if (i0 == i1)
		{
			return table[i0, col];
		}
		return (1.0 - dsFrac) * table[i0, col] + dsFrac * table[i1, col];
	}

	public (double flow, double vel, double dFlow_dUp, double dFlow_dDown) FullFlowAnalytical(double upEG, double dnWS)
	{
		double d = upEG - dnWS;
		double outletControlCoefTerm = OutletControlCoef * Math.Sqrt(d);
		double upEGAboveInvertOverEffectiveRise = (upEG - _culvert.InvertUS) / _culvert.EffectiveRise;
		double inletControlCoef1InletControlCoef2Term = InletControlCoef1 * Math.Sqrt(upEGAboveInvertOverEffectiveRise + InletControlCoef2);
		double computed;
		double inletControlCoef1EffectiveRiseTerm;
		double computed2;
		if (inletControlCoef1InletControlCoef2Term < outletControlCoefTerm)
		{
			computed = inletControlCoef1InletControlCoef2Term;
			inletControlCoef1EffectiveRiseTerm = InletControlCoef1 / (2.0 * _culvert.EffectiveRise * Math.Sqrt(upEGAboveInvertOverEffectiveRise + InletControlCoef2));
			computed2 = 0.0;
		}
		else
		{
			computed = outletControlCoefTerm;
			inletControlCoef1EffectiveRiseTerm = OutletControlCoef / (2.0 * Math.Sqrt(d));
			computed2 = 0.0 - inletControlCoef1EffectiveRiseTerm;
		}
		double fullCulvertAreaTerm = computed / FullCulvertArea;
		return (flow: computed, vel: fullCulvertAreaTerm, dFlow_dUp: inletControlCoef1EffectiveRiseTerm, dFlow_dDown: computed2);
	}
}
