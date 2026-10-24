using System;

namespace Ras.Engine.Structures.Culverts;

public static class OutletControlSolver
{
	public static void ComputeOutletControl(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		Initialize(culvert, control, cp, solution);
		ComputeExitLoss(culvert, control, cp, solution);
		if (cp.IsOutletFull)
		{
			ComputeFull(culvert, control, cp, solution);
		}
		if (!cp.IsInletFull)
		{
			ComputeBackWater(culvert, control, cp, solution);
		}
		if (cp.IsInletFull && !control.IsAllowSiphon)
		{
			CheckForSiphon(culvert, control, cp, solution);
		}
	}

	private static void Initialize(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		cp.Depth = 0.0;
		cp.FrictionLoss = 0.0;
		cp.IsDone = false;
		solution.FrictionLoss = 0.0;
		int lengthOffset = solution.DepthInternal.Length - 1;
		if (solution.Qoriented == 0.0)
		{
			cp.Depth = solution.WSEDS - culvert.InvertDS;
			cp.Depth = Math.Min(cp.Depth, culvert.EffectiveRise);
			cp.Depth = Math.Max(cp.Depth, 0.0);
			solution.DepthInternal[lengthOffset] = cp.Depth;
			cp.DepthInternalSuperCrit[0] = cp.Depth;
			solution.WSEOutlet = cp.Depth + culvert.InvertDS;
			solution.DepthInlet = Math.Min(solution.WSEDS - culvert.InvertUS, culvert.EffectiveRise);
			solution.DepthInlet = Math.Max(solution.DepthInlet, 0.0);
			solution.WSEInlet = solution.DepthInlet + culvert.InvertUS;
			solution.EGUS = Math.Max(solution.WSEDS, culvert.InvertUS);
			solution.EGOutletControl = solution.EGUS;
			cp.IsDone = true;
			return;
		}
		if (solution.Crit.Depth >= culvert.EffectiveRise)
		{
			cp.Depth = culvert.EffectiveRise;
			solution.DepthInternal[lengthOffset] = cp.Depth;
			cp.IsOutletFull = true;
			solution.DepthInternal[lengthOffset] = cp.Depth;
			solution.WSEOutlet = cp.Depth + culvert.InvertDS;
			cp.IsDone = true;
			return;
		}
		double depthPlusInvert = solution.Crit.Depth + culvert.InvertDS;
		double wseOutlet = CulvertSectionHydraulics.ComputeOutletWSEL(culvert, control, solution, depthPlusInvert);
		if (wseOutlet < depthPlusInvert)
		{
			cp.Depth = solution.Crit.Depth;
			cp.IsOutletCritical = true;
			solution.DepthInternal[lengthOffset] = cp.Depth;
			solution.WSEOutlet = cp.Depth + culvert.InvertDS;
			cp.IsDone = true;
			return;
		}
		if (culvert.CoefExitLoss >= 1.0)
		{
			if (solution.WSEDS >= culvert.EffectiveRise + culvert.InvertDS)
			{
				cp.Depth = culvert.EffectiveRise;
				cp.IsOutletFull = true;
				solution.DepthInternal[lengthOffset] = cp.Depth;
				solution.WSEOutlet = cp.Depth + culvert.InvertDS;
				cp.IsDone = true;
				return;
			}
			if (solution.WSEDS <= solution.Crit.Depth + culvert.InvertDS)
			{
				cp.Depth = solution.Crit.Depth;
				cp.IsOutletCritical = true;
				solution.DepthInternal[lengthOffset] = cp.Depth;
				solution.WSEOutlet = cp.Depth + culvert.InvertDS;
				cp.IsDone = true;
				return;
			}
			solution.WSEOutlet = solution.WSEDS;
			cp.Depth = solution.WSEOutlet - culvert.InvertDS;
			solution.DepthInternal[lengthOffset] = cp.Depth;
			cp.IsDone = true;
			CulvertXSection culvertXSection = new CulvertXSection
			{
				Depth = culvert.EffectiveRise,
				Qtotal = solution.Qoriented
			};
			CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
			_ = culvertXSection.Depth + culvertXSection.VelHead + culvert.InvertDS;
			_ = solution.EGDS;
			return;
		}
		depthPlusInvert = culvert.EffectiveRise + culvert.InvertDS;
		wseOutlet = CulvertSectionHydraulics.ComputeOutletWSEL(culvert, control, solution, depthPlusInvert);
		if (wseOutlet + 0.01 >= depthPlusInvert)
		{
			cp.Depth = culvert.EffectiveRise;
			cp.IsOutletFull = true;
			solution.DepthInternal[lengthOffset] = cp.Depth;
			cp.PrevDepthInternal = cp.Depth;
			solution.WSEOutlet = cp.Depth + culvert.InvertDS;
			cp.IsDone = true;
			return;
		}
		double waterSurfaceAboveInvert = solution.WSEDS - culvert.InvertDS;
		waterSurfaceAboveInvert = Math.Min(waterSurfaceAboveInvert, culvert.EffectiveRise);
		waterSurfaceAboveInvert = Math.Max(waterSurfaceAboveInvert, 0.0);
		cp.Depth = solution.Crit.Depth + 0.7 * (waterSurfaceAboveInvert - solution.Crit.Depth);
		solution.DepthInternal[lengthOffset] = cp.Depth;
		solution.WSEOutlet = cp.Depth + culvert.InvertDS;
		depthPlusInvert = solution.WSEOutlet;
		wseOutlet = CulvertSectionHydraulics.ComputeOutletWSEL(culvert, control, solution, depthPlusInvert);
		double absoluteWseOutletAboveDepthPlusInvert = Math.Abs(wseOutlet - depthPlusInvert);
		solution.WSEOutlet = wseOutlet;
		int ordinal = 0;
		bool condition = false;
		while (!condition)
		{
			ordinal++;
			if (ordinal >= 15)
			{
				condition = true;
			}
			depthPlusInvert = wseOutlet;
			wseOutlet = CulvertSectionHydraulics.ComputeOutletWSEL(culvert, control, solution, depthPlusInvert);
			double absoluteWseOutletAboveDepthPlusInvert2 = Math.Abs(wseOutlet - depthPlusInvert);
			if (absoluteWseOutletAboveDepthPlusInvert2 < absoluteWseOutletAboveDepthPlusInvert)
			{
				absoluteWseOutletAboveDepthPlusInvert = absoluteWseOutletAboveDepthPlusInvert2;
				solution.WSEOutlet = wseOutlet;
			}
			if (absoluteWseOutletAboveDepthPlusInvert < control.OutletControlDepthTolerance)
			{
				condition = true;
			}
		}
		if (solution.WSEOutlet > culvert.EffectiveRise + culvert.InvertDS)
		{
			cp.Depth = culvert.EffectiveRise;
			cp.IsOutletFull = true;
			solution.DepthInternal[lengthOffset] = cp.Depth;
			cp.PrevDepthInternal = cp.Depth;
			solution.WSEOutlet = cp.Depth + culvert.InvertDS;
			cp.IsDone = true;
		}
		else
		{
			cp.Depth = solution.WSEOutlet - culvert.InvertDS;
			cp.PrevDepthInternal = cp.Depth;
			solution.DepthInternal[lengthOffset] = cp.Depth;
		}
	}

	private static void ComputeExitLoss(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = solution.WSEOutlet - culvert.InvertDS,
			Qtotal = solution.Qoriented
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		if (culvertXSection.VelHead > solution.VelHeadDS)
		{
			solution.ExitLoss = culvert.CoefExitLoss * (culvertXSection.VelHead - solution.VelHeadDS);
		}
		else
		{
			solution.ExitLoss = 0.0;
		}
		if (culvertXSection.Depth + culvertXSection.VelHead + culvert.InvertDS - solution.ExitLoss > solution.EGDS)
		{
			solution.ExitLoss = culvertXSection.Depth + culvertXSection.VelHead + culvert.InvertDS - solution.EGDS;
		}
		if (culvertXSection.Depth >= culvert.EffectiveRise)
		{
			cp.IsOutletFull = true;
		}
		else
		{
			cp.IsOutletFull = false;
		}
	}

	private static void ComputeFull(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = culvert.EffectiveRise,
			Qtotal = solution.Qoriented
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		FrictionLossLength(control, culvertXSection, cp, culvert.Length);
		double exitLossSolutionTerm = solution.ExitLoss + cp.FrictionLoss + solution.EGDS;
		double velHeadCulvertXSectionTerm = exitLossSolutionTerm - culvertXSection.VelHead - culvert.EffectiveRise - culvert.InvertUS;
		if (velHeadCulvertXSectionTerm > 0.0 || (velHeadCulvertXSectionTerm >= -0.5 && solution.NormalDepth > solution.Crit.Depth) || solution.Crit.Depth >= culvert.EffectiveRise)
		{
			cp.IsInletFull = true;
			Array.Fill(solution.DepthInternal, culvert.EffectiveRise);
			solution.DepthInlet = culvert.EffectiveRise;
			solution.WSEInlet = culvert.EffectiveRise + culvert.InvertUS;
			ComputeEntranceLossWithCoef(culvert, control, culvertXSection, solution);
			solution.EGOutletControl = exitLossSolutionTerm + solution.EntranceLoss;
		}
		else
		{
			cp.TransitionedToFreeSfc = true;
			double energyGradeSolutionTerm = solution.EGDS + solution.ExitLoss - culvertXSection.VelHead - culvert.EffectiveRise - culvert.InvertDS;
			if (energyGradeSolutionTerm > 0.0)
			{
				cp.PartiallyFullLength = CulvertMath.Interpolate(energyGradeSolutionTerm, velHeadCulvertXSectionTerm, 0.0, 0.0, culvert.Length);
				FrictionLossLength(control, culvertXSection, cp, cp.PartiallyFullLength);
			}
		}
	}

	private static void ComputeBackWater(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		BackWaterInit(culvert, control, cp, solution);
		if (!cp.IsDone)
		{
			DirectStep(culvert, control, cp, solution);
		}
	}

	private static void BackWaterInit(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		CulvertSectionHydraulics.ComputeNormalDepth(culvert, control, solution);
		cp.IsDone = false;
		_ = solution.WSEOutlet;
		if (solution.Qoriented <= 0.0)
		{
			solution.EntranceLoss = 0.0;
			for (int i = 0; i < control.NumProfileStations; i++)
			{
				double interpolate = CulvertMath.Interpolate(0.0, culvert.Length, cp.StationLocation[i], culvert.InvertUS, culvert.InvertDS);
				double depthInternal = Math.Max(Math.Min(solution.WSEDS - interpolate, culvert.EffectiveRise), 0.0);
				solution.DepthInternal[i] = depthInternal;
			}
			cp.IsDone = true;
			return;
		}
		cp.DirectStepVertAdjFirst = 0.0;
		cp.IsMaxQReverse = false;
		cp.CurrentStationIdx = solution.DepthInternal.Length - 1;
		if (cp.TransitionedToFreeSfc)
		{
			cp.DirectStepVertAdjFirst = culvert.EffectiveRise - solution.MaxDepthQ;
			cp.DirectStepVertAdjUnit = -1.0;
			solution.DepthInternal[cp.CurrentStationIdx] = culvert.EffectiveRise;
			cp.PrevLengthInternal = culvert.Length;
			double length = culvert.Length - cp.PartiallyFullLength;
			cp.StoreDepthInternal(culvert, solution, length, culvert.EffectiveRise, isInterpolate: false);
		}
		else
		{
			if (cp.Depth <= solution.Crit.Depth && solution.NormalDepth <= solution.Crit.Depth)
			{
				solution.WSEInlet = solution.Crit.Depth + culvert.InvertUS;
				solution.DepthInlet = solution.Crit.Depth;
				cp.IsDone = true;
				Array.Fill(solution.DepthInternal, solution.Crit.Depth);
				EGOutletControlFinal(culvert, control, cp, solution);
				return;
			}
			if (Math.Abs(cp.Depth - solution.NormalDepth) <= control.OutletControlDepthTolerance && solution.NormalDepth <= culvert.EffectiveRise)
			{
				solution.WSEInlet = solution.NormalDepth + culvert.InvertUS;
				solution.DepthInlet = solution.NormalDepth;
				cp.IsDone = true;
				Array.Fill(solution.DepthInternal, solution.NormalDepth);
				EGOutletControlFinal(culvert, control, cp, solution);
				return;
			}
			if (cp.Depth > solution.NormalDepth)
			{
				cp.DirectStepVertAdjUnit = -1.0;
				if (cp.Depth > solution.MaxDepthQ)
				{
					CulvertXSection culvertXSection = new CulvertXSection
					{
						Qtotal = solution.Qoriented
					};
					culvertXSection.Depth = cp.Depth;
					CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
					if (culvertXSection.FrictionSlope > culvert.Slope)
					{
						cp.DirectStepVertAdjUnit = 1.0;
						cp.IsMaxQReverse = true;
					}
				}
			}
			else
			{
				cp.DirectStepVertAdjUnit = 1.0;
			}
		}
		double invertCulvertTerm = Math.Abs(culvert.InvertUS - culvert.InvertDS) + culvert.EffectiveRise;
		cp.DirectStepVertAdj = invertCulvertTerm / 400.0;
		cp.DirectStepVertAdj = Math.Max(control.OutletControlDepthTolerance * 2.0, cp.DirectStepVertAdj);
		cp.DirectStepVertAdjFirst = Math.Max(cp.DirectStepVertAdj, cp.DirectStepVertAdjFirst);
		cp.DirectStepVertAdj = cp.DirectStepVertAdjUnit * cp.DirectStepVertAdj;
		cp.DirectStepVertAdjFirst = cp.DirectStepVertAdjUnit * cp.DirectStepVertAdjFirst;
	}

	private static void DirectStep(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		int ordinal = 500;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Qtotal = solution.Qoriented
		};
		culvertXSection.Depth = cp.Depth;
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		double sFPrev = culvertXSection.FrictionSlope;
		double eGPrev = culvertXSection.Depth + culvertXSection.VelHead;
		double depth = cp.Depth;
		double sFPrev2 = 0.0;
		double eGPrev2 = 0.0;
		int ordinal2 = 0;
		bool condition = false;
		double directStepVertAdjSum = depth + cp.DirectStepVertAdjFirst;
		double culvertLength = culvert.Length;
		if (cp.IsOutletFull)
		{
			culvertLength -= cp.PartiallyFullLength;
		}
		while (!condition)
		{
			ordinal2++;
			if (directStepVertAdjSum >= culvert.EffectiveRise)
			{
				condition = true;
				cp.TransitionedToFullFlow = true;
				cp.IsInletFull = true;
				cp.PartiallyFullLength = culvert.Length - culvertLength;
				solution.DepthInlet = culvert.EffectiveRise;
				solution.WSEInlet = culvert.EffectiveRise + culvert.InvertUS;
			}
			else if (directStepVertAdjSum <= solution.Crit.Depth && cp.DirectStepVertAdjFirst < 0.0)
			{
				condition = true;
				cp.IsInletCritical = true;
				solution.DepthInlet = solution.Crit.Depth;
				solution.WSEInlet = solution.Crit.Depth + culvert.InvertUS;
			}
			else if ((directStepVertAdjSum <= solution.NormalDepth && cp.DirectStepVertAdjFirst < 0.0) || (directStepVertAdjSum >= solution.NormalDepth && cp.DirectStepVertAdjFirst > 0.0 && !cp.IsMaxQReverse))
			{
				condition = true;
				solution.DepthInlet = solution.NormalDepth;
				solution.WSEInlet = solution.NormalDepth + culvert.InvertUS;
			}
			else
			{
				culvertXSection.Depth = directStepVertAdjSum;
				CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
				double computed;
				(sFPrev2, eGPrev2, computed) = ComputeDirectStepLength(culvert, culvertXSection, eGPrev, sFPrev);
				if (computed <= 0.0)
				{
					solution.DepthInlet = solution.Crit.Depth;
					solution.WSEInlet = solution.Crit.Depth + culvert.InvertUS;
					condition = true;
				}
				else
				{
					culvertLength -= computed;
					cp.StoreDepthInternal(culvert, solution, culvertLength, directStepVertAdjSum, isInterpolate: true);
					if (culvertLength <= 0.0)
					{
						condition = true;
						double directStepVertAdjSumDepthTerm = (directStepVertAdjSum - depth) * (computed + culvertLength) / computed;
						solution.DepthInlet = depth + directStepVertAdjSumDepthTerm;
						solution.WSEInlet = depth + directStepVertAdjSumDepthTerm + culvert.InvertUS;
					}
				}
			}
			if (ordinal2 > ordinal && !condition)
			{
				condition = true;
				if (solution.NormalDepth >= solution.Crit.Depth)
				{
					solution.DepthInlet = solution.NormalDepth;
					solution.WSEInlet = solution.NormalDepth + culvert.InvertUS;
				}
				else
				{
					solution.DepthInlet = solution.Crit.Depth;
					solution.WSEInlet = solution.Crit.Depth + culvert.InvertUS;
				}
			}
			else
			{
				depth = directStepVertAdjSum;
				directStepVertAdjSum += cp.DirectStepVertAdj;
				eGPrev = eGPrev2;
				sFPrev = sFPrev2;
			}
		}
		EGOutletControlFinal(culvert, control, cp, solution, culvertLength);
	}

	private static void EGOutletControlFinal(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution, double lengthCum = 0.0)
	{
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Qtotal = solution.Qoriented
		};
		culvertXSection.Depth = solution.DepthInlet;
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		ComputeEntranceLossWithCoef(culvert, control, culvertXSection, solution);
		if (cp.TransitionedToFullFlow)
		{
			FrictionLossLength(control, culvertXSection, cp, lengthCum);
			double partiallyFullLengthCpTerm = cp.PartiallyFullLength / culvert.Length * (culvert.InvertUS - culvert.InvertDS) + culvert.InvertDS + culvert.EffectiveRise;
			solution.EGOutletControl = partiallyFullLengthCpTerm + cp.FrictionLoss + solution.EntranceLoss + culvertXSection.VelHead;
		}
		else
		{
			solution.EGOutletControl = solution.WSEInlet + culvertXSection.VelHead + solution.EntranceLoss;
		}
		cp.CurrentStationIdx--;
		if (cp.CurrentStationIdx > 0)
		{
			Array.Fill(solution.DepthInternal, solution.DepthInlet, 0, cp.CurrentStationIdx + 1);
		}
		else
		{
			solution.DepthInternal[0] = solution.DepthInlet;
		}
	}

	private static void CheckForSiphon(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = culvert.EffectiveRise,
			Qtotal = solution.Qoriented
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		double egOutletControl = solution.WSEInlet + culvertXSection.VelHead + solution.EntranceLoss;
		if (egOutletControl > solution.EGOutletControl)
		{
			solution.EGOutletControl = egOutletControl;
		}
	}

	private static void FrictionLossLength(CulvertSolverControl control, CulvertXSection culvProp, CulvertProfileState cp, double length)
	{
		double gravityControlTerm = 2.0 * control.Grav / (control.MannCoef * control.MannCoef) * length / Math.Pow(culvProp.HydRad, 1.3333333333333333);
		cp.FrictionLoss = gravityControlTerm * culvProp.Mann * culvProp.Mann * culvProp.VelHead;
	}

	public static (double SF, double EG, double lengthStep) ComputeDirectStepLength(FlowOrientedCulvert culvert, CulvertXSection culvProp, double EGPrev, double SFPrev)
	{
		double frictionSlope = culvProp.FrictionSlope;
		double depthPlusVelHead = culvProp.Depth + culvProp.VelHead;
		double slopeCulvertTerm = 0.0;
		double egPrevAboveDepthPlusVelHead = EGPrev - depthPlusVelHead;
		double frictionSlopeSfPrevTerm = (frictionSlope + SFPrev) / 2.0;
		if (culvert.Slope - frictionSlopeSfPrevTerm != 0.0)
		{
			slopeCulvertTerm = egPrevAboveDepthPlusVelHead / (culvert.Slope - frictionSlopeSfPrevTerm);
		}
		return (SF: frictionSlope, EG: depthPlusVelHead, lengthStep: slopeCulvertTerm);
	}

	public static void ComputeEntranceLossWithCoef(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertXSection culvProp, CulvertSolution solution)
	{
		if (control.UseUSVelInEntLoss)
		{
			solution.EntranceLoss = culvert.CoefEntranceLoss * Math.Abs(culvProp.VelHead - solution.VelHeadUS);
		}
		else
		{
			solution.EntranceLoss = culvert.CoefEntranceLoss * culvProp.VelHead;
		}
	}
}
