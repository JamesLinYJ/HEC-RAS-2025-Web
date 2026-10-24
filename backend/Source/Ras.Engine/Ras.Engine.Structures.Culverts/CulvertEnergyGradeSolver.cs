using System;

namespace Ras.Engine.Structures.Culverts;

public static class CulvertEnergyGradeSolver
{
	public static void Compute(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution)
	{
		CulvertProfileState cp = new CulvertProfileState(control);
		Initialize(culvert, control, cp, solution);
		ComputeInletControl(culvert, control, cp, solution);
		OutletControlSolver.ComputeOutletControl(culvert, control, cp, solution);
		if (solution.EGInletControl > solution.EGOutletControl)
		{
			solution.IsOutletControl = false;
			solution.EGUS = solution.EGInletControl;
		}
		else
		{
			solution.IsOutletControl = true;
			solution.EGUS = solution.EGOutletControl;
		}
		Finalize(culvert, control, cp, solution);
	}

	private static void Initialize(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		cp.IsSubCritical = true;
		cp.IsOutletFull = false;
		cp.IsOutletCritical = false;
		cp.TransitionedToFullFlow = false;
		cp.TransitionedToFreeSfc = false;
		cp.IsInletCritical = false;
		cp.IsInletFull = false;
		cp.IsInletFree = false;
		cp.PartiallyFullLength = 0.0;
		cp.MixedFlowType = 0;
		cp.RunningTotalLengthInternal = culvert.Length;
		solution.NormalDepth = 0.0;
		solution.EGDS = solution.WSEDS + solution.VelHeadDS;
		int numProfileStations = control.NumProfileStations;
		cp.StationLocation[0] = 0.0;
		cp.StationLocation[numProfileStations - 1] = culvert.Length;
		for (int i = 1; i < numProfileStations - 1; i++)
		{
			cp.StationLocation[i] = (double)i / (double)(numProfileStations - 1) * culvert.Length;
		}
		CulvertSectionHydraulics.ComputeCriticalDepth(culvert, control, solution);
	}

	private static void ComputeInletControl(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		if (culvert.Shape != CulvertShape.LowArch && culvert.Shape != CulvertShape.HighArch)
		{
			CulvertXSection culvertXSection = new CulvertXSection
			{
				Depth = culvert.EffectiveRise,
				Qtotal = solution.Qoriented
			};
			CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
			double qorientedSolutionTerm = solution.Qoriented / (culvertXSection.Area * Math.Pow(culvert.EffectiveRise, 0.5));
			double useEqn26InletCoefUSTerm = 0.0;
			double effectiveRiseCulvertTerm = 0.0;
			if (qorientedSolutionTerm <= 4.0)
			{
				useEqn26InletCoefUSTerm = ((!culvert.InletCoefUS.UseEqn26) ? (culvert.InletCoefUS.K * Math.Pow(qorientedSolutionTerm, culvert.InletCoefUS.M) * culvert.EffectiveRise) : ((culvert.InletCoefUS.K * Math.Pow(qorientedSolutionTerm, culvert.InletCoefUS.M) + culvert.InletCoefUS.SlopeCorrection * culvert.Slope) * culvert.EffectiveRise + solution.Crit.Depth + solution.Crit.VelHead));
			}
			if (qorientedSolutionTerm >= 3.5)
			{
				effectiveRiseCulvertTerm = culvert.EffectiveRise * (culvert.InletCoefUS.C * qorientedSolutionTerm * qorientedSolutionTerm + culvert.InletCoefUS.Y + culvert.InletCoefUS.SlopeCorrection * culvert.Slope);
			}
			if (qorientedSolutionTerm < 3.5)
			{
				solution.EGInletControl = useEqn26InletCoefUSTerm + culvert.InvertUS;
			}
			else if (qorientedSolutionTerm < 4.0)
			{
				solution.EGInletControl = CulvertMath.Interpolate(3.5, 4.0, qorientedSolutionTerm, useEqn26InletCoefUSTerm, effectiveRiseCulvertTerm) + culvert.InvertUS;
			}
			else if (qorientedSolutionTerm >= 4.0)
			{
				solution.EGInletControl = effectiveRiseCulvertTerm + culvert.InvertUS;
			}
		}
	}

	private static void Finalize(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		double eGUS = solution.EGUS;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = solution.DepthInlet,
			Qtotal = solution.Qoriented
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		if (solution.DepthInlet >= culvert.EffectiveRise)
		{
			OutletControlSolver.ComputeEntranceLossWithCoef(culvert, control, culvertXSection, solution);
			eGUS = solution.EGUS - solution.EntranceLoss;
			eGUS = Math.Max(eGUS, solution.DepthInlet + culvertXSection.VelHead);
		}
		else
		{
			eGUS = solution.DepthInlet + culvertXSection.VelHead;
			solution.EntranceLoss = solution.EGUS - eGUS;
		}
		double energyGradePlusExitLoss = solution.EGDS + solution.ExitLoss;
		solution.FrictionLoss = eGUS - energyGradePlusExitLoss;
	}
}
