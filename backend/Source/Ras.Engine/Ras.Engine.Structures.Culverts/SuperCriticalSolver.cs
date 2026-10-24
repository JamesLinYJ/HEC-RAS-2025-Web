using System;

namespace Ras.Engine.Structures.Culverts;

public static class SuperCriticalSolver
{
	public static void ComputeSuperCritical(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		if (!solution.IsOutletControl || cp.IsInletCritical || cp.IsOutletCritical)
		{
			double egInletControlAboveInvert = solution.EGInletControl - culvert.InvertUS;
			if (!solution.IsOutletControl)
			{
				_ = culvert.EffectiveRise * 1.5;
			}
			Initialize(culvert, control, cp, solution);
			DirectStepSuper(culvert, control, cp, solution);
			int lengthOffset = cp.DepthInternalSuperCrit.Length - 1;
			if (!DoesSubControl(culvert, control, solution, solution.DepthInternal[lengthOffset], cp.DepthInternalSuperCrit[lengthOffset]))
			{
				cp.MixedFlowType = 3;
				return;
			}
			cp.MixedFlowType = 2;
			ComputeHydraulicJump(culvert, control, cp, solution);
		}
	}

	private static void Initialize(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		cp.IsSubCritical = false;
		cp.Depth = solution.Crit.Depth;
		Array.Fill(cp.DepthInternalSuperCrit, -1.0);
		cp.DepthInternalSuperCrit[0] = cp.Depth;
		cp.CurrentStationIdx = 0;
		cp.PrevDepthInternal = cp.Depth;
		double invertCulvertTerm = Math.Abs(culvert.InvertUS - culvert.InvertDS) + culvert.EffectiveRise;
		cp.DirectStepVertAdj = invertCulvertTerm / 400.0;
		cp.DirectStepVertAdj = Math.Max(control.OutletControlDepthTolerance * 2.0, cp.DirectStepVertAdj);
		cp.DirectStepVertAdjFirst = cp.DirectStepVertAdj;
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
		cp.DirectStepVertAdj = cp.DirectStepVertAdjUnit * cp.DirectStepVertAdj;
		cp.DirectStepVertAdjFirst = cp.DirectStepVertAdjUnit * cp.DirectStepVertAdjFirst;
	}

	private static void DirectStepSuper(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
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
		double sFPrev2 = 0.0;
		double eGPrev2 = 0.0;
		double lengthStepSum = 0.0;
		double depth = cp.Depth;
		int ordinal2 = 0;
		bool condition = false;
		double directStepVertAdjSum = depth + cp.DirectStepVertAdjFirst;
		double depthInternalSuperCrit = 0.0;
		while (!condition)
		{
			ordinal2++;
			if (directStepVertAdjSum >= culvert.EffectiveRise)
			{
				condition = true;
				depthInternalSuperCrit = culvert.EffectiveRise;
				solution.WSEOutletSup = culvert.EffectiveRise + culvert.InvertUS;
			}
			else if (directStepVertAdjSum >= solution.Crit.Depth && cp.DirectStepVertAdjFirst > 0.0)
			{
				condition = true;
				cp.IsInletCritical = true;
				depthInternalSuperCrit = solution.Crit.Depth;
				solution.WSEOutletSup = solution.Crit.Depth + culvert.InvertUS;
			}
			else if ((directStepVertAdjSum <= solution.NormalDepth && cp.DirectStepVertAdjFirst < 0.0) || (directStepVertAdjSum >= solution.NormalDepth && cp.DirectStepVertAdjFirst > 0.0))
			{
				condition = true;
				depthInternalSuperCrit = solution.NormalDepth;
				solution.WSEOutletSup = solution.NormalDepth + culvert.InvertUS;
			}
			else
			{
				culvertXSection.Depth = directStepVertAdjSum;
				CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
				(double SF, double EG, double lengthStep) tuple = OutletControlSolver.ComputeDirectStepLength(culvert, culvertXSection, eGPrev, sFPrev);
				sFPrev2 = tuple.SF;
				eGPrev2 = tuple.EG;
				double lengthStep = tuple.lengthStep;
				lengthStep = 0.0 - lengthStep;
				if (lengthStep <= 0.0)
				{
					depthInternalSuperCrit = solution.Crit.Depth;
					solution.WSEOutletSup = solution.Crit.Depth + culvert.InvertUS;
					condition = true;
				}
				else
				{
					lengthStepSum += lengthStep;
					cp.StoreDepthInternal(culvert, solution, lengthStepSum, directStepVertAdjSum, isInterpolate: true);
					if (lengthStepSum > culvert.Length)
					{
						condition = true;
						double directStepVertAdjSumDepthTerm = (directStepVertAdjSum - depth) * (culvert.Length + lengthStep) / lengthStep;
						depthInternalSuperCrit = depth + directStepVertAdjSumDepthTerm + culvert.InvertUS;
						solution.WSEOutletSup = depth + directStepVertAdjSumDepthTerm + culvert.InvertUS;
					}
				}
			}
			if (ordinal2 > ordinal && !condition)
			{
				condition = true;
				if (solution.NormalDepth >= solution.Crit.Depth)
				{
					depthInternalSuperCrit = solution.NormalDepth + culvert.InvertUS;
					solution.WSEOutletSup = solution.NormalDepth;
				}
				else
				{
					depthInternalSuperCrit = solution.Crit.Depth;
					solution.WSEOutletSup = solution.Crit.Depth + culvert.InvertUS;
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
		cp.CurrentStationIdx++;
		int lengthOffset = cp.DepthInternalSuperCrit.Length - 1;
		if (cp.CurrentStationIdx < lengthOffset)
		{
			Array.Fill(cp.DepthInternalSuperCrit, depthInternalSuperCrit, cp.CurrentStationIdx, lengthOffset - cp.CurrentStationIdx + 1);
		}
		else
		{
			cp.DepthInternalSuperCrit[lengthOffset] = depthInternalSuperCrit;
		}
	}

	private static void ComputeHydraulicJump(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertProfileState cp, CulvertSolution solution)
	{
		_ = solution.EGUS;
		solution.DepthInlet = cp.DepthInternalSuperCrit[0];
		solution.DepthInternal[0] = cp.DepthInternalSuperCrit[0];
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = culvert.EffectiveRise,
			Qtotal = solution.Qoriented
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		cp.CurrentStationIdx = 1;
		CulvertXSection culvertXSection2 = new CulvertXSection();
		culvertXSection2.Qtotal = solution.Qoriented;
		bool condition = false;
		while (!condition)
		{
			culvertXSection.Depth = solution.DepthInternal[cp.CurrentStationIdx];
			CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
			culvertXSection2.Depth = cp.DepthInternalSuperCrit[cp.CurrentStationIdx];
			CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection2);
			if (culvertXSection2.SpecificForce > culvertXSection.SpecificForce)
			{
				solution.DepthInternal[cp.CurrentStationIdx] = cp.DepthInternalSuperCrit[cp.CurrentStationIdx];
			}
			else
			{
				condition = true;
			}
			cp.CurrentStationIdx++;
			if (cp.CurrentStationIdx == cp.DepthInternalSuperCrit.Length - 1)
			{
				condition = true;
			}
		}
	}

	public static bool DoesSubControl(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution, double depthSub, double depthSuper)
	{
		bool result = true;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = depthSub,
			Qtotal = solution.Qoriented
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		CulvertXSection culvertXSection2 = new CulvertXSection
		{
			Depth = depthSuper,
			Qtotal = solution.Qoriented
		};
		culvertXSection2.Depth = depthSuper;
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection2);
		if (culvertXSection2.SpecificForce > culvertXSection.SpecificForce)
		{
			result = false;
		}
		return result;
	}
}
