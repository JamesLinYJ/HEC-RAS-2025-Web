using System;

namespace Ras.Engine.Structures.Culverts;

public static class CulvertSectionHydraulics
{
	public static double ComputeOutletWSEL(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution, double wseAssumed)
	{
		double depth = wseAssumed - culvert.InvertDS;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = depth,
			Qtotal = solution.Qoriented
		};
		ComputeXSHydraulicProps(culvert, control, culvertXSection);
		double coefExitLossCulvertTerm = 0.0;
		if (culvertXSection.VelHead > solution.VelHeadDS)
		{
			coefExitLossCulvertTerm = culvert.CoefExitLoss * (culvertXSection.VelHead - solution.VelHeadDS);
		}
		return solution.WSEDS + coefExitLossCulvertTerm - culvertXSection.VelHead;
	}

	public static void ComputeXSHydraulicProps(ICulvertGeometry culvert, CulvertSolverControl control, CulvertXSection culvProp)
	{
		culvProp.Mann = culvert.Mann;
		if (culvProp.Depth <= 0.0)
		{
			culvProp.ZeroOut();
			return;
		}
		if (!culvert.IsPartiallyBlocked && !culvert.IsMannBottom)
		{
			culvProp.Compute(culvert, control);
			return;
		}
		double depth = culvProp.Depth;
		double topWidth = 0.0;
		double area = 0.0;
		double yCentroid = 0.0;
		double topWidth2 = 0.0;
		CulvertXSection culvertXSection = new CulvertXSection();
		if (culvert.IsPartiallyBlocked)
		{
			double depth2 = Math.Max(culvert.DepthBlocked, 0.001);
			culvertXSection.Depth = depth2;
			culvertXSection.Qtotal = culvProp.Qtotal;
			culvertXSection.Compute(culvert, control);
			area = culvertXSection.Area;
			yCentroid = culvertXSection.YCentroid;
			culvProp.Depth += culvertXSection.Depth;
			culvProp.Compute(culvert, control);
			topWidth = ((!(culvertXSection.HydRad > 0.001)) ? culvertXSection.TopWidth : (culvertXSection.Area / culvertXSection.HydRad));
			topWidth2 = culvertXSection.TopWidth;
		}
		else
		{
			culvProp.Compute(culvert, control);
		}
		bool condition = false;
		CulvertXSection culvertXSection2 = new CulvertXSection();
		if (culvert.IsMannBottom && culvert.DepthMannBottom >= culvert.DepthBlocked)
		{
			condition = true;
			culvertXSection2.Depth = culvert.DepthMannBottom;
			culvertXSection2.Qtotal = culvProp.Qtotal;
			culvertXSection2.Compute(culvert, control);
		}
		double shapeCulvertTerm = 0.0;
		if (!condition)
		{
			shapeCulvertTerm = culvert.Shape switch
			{
				CulvertShape.Box => culvert.Span, 
				CulvertShape.SemiCircular => 2.0 * culvert.FullRise, 
				CulvertShape.Arch => culvert.Span, 
				_ => throw new NotImplementedException($"Bottom Manning's n depth for Culvert shape {culvert.Shape} not assumed in compute"), 
			};
		}
		else
		{
			shapeCulvertTerm = ((!(culvertXSection2.HydRad > 0.0001)) ? (culvertXSection2.TopWidth - topWidth) : (culvertXSection2.Area / culvertXSection2.HydRad - topWidth));
			shapeCulvertTerm = Math.Max(shapeCulvertTerm, 0.0);
		}
		double areaCulvPropTerm = culvProp.Area / culvProp.HydRad - topWidth + topWidth2;
		shapeCulvertTerm += topWidth2;
		double computed = areaCulvPropTerm - shapeCulvertTerm;
		double manningsNCulvertTerm = 0.0;
		if (culvert.IsMannBottom)
		{
			manningsNCulvertTerm = computed * Math.Pow(culvert.Mann, 1.5) + shapeCulvertTerm * Math.Pow(culvert.MannBottom, 1.5);
			culvProp.Mann = Math.Pow(manningsNCulvertTerm / areaCulvPropTerm, 2.0 / 3.0);
		}
		else
		{
			culvProp.Mann = culvert.Mann;
		}
		culvProp.Depth = depth;
		manningsNCulvertTerm = culvProp.Area * culvProp.YCentroid - area * (yCentroid + culvProp.Depth);
		culvProp.Area -= area;
		if (culvProp.Area <= 0.0)
		{
			culvProp.ZeroOut();
			return;
		}
		culvProp.HydRad = culvProp.Area / areaCulvPropTerm;
		double hydRadCulvPropTerm = Math.Pow(culvProp.HydRad, 2.0 / 3.0);
		culvProp.Conveyance = control.MannCoef / culvProp.Mann * culvProp.Area * hydRadCulvPropTerm;
		culvProp.Vel = culvProp.Qtotal / culvProp.Area;
		culvProp.VelHead = culvProp.Vel * culvProp.Vel / (2.0 * control.Grav);
		culvProp.FrictionSlope = culvProp.Mann * culvProp.Mann * culvProp.Vel * culvProp.Vel / (control.MannCoef * control.MannCoef * hydRadCulvPropTerm * hydRadCulvPropTerm);
		culvProp.YCentroid = manningsNCulvertTerm / culvProp.Area;
		culvProp.SpecificForce = culvProp.Qtotal * culvProp.Qtotal / (control.Grav * culvProp.Area) + culvProp.YCentroid * culvProp.Area;
	}

	public static void ComputeNormalDepth(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution)
	{
		if (solution.Qoriented <= 0.0 || culvert.Slope < 1E-07)
		{
			solution.NormalDepth = 999999.99;
			return;
		}
		bool isNorm = ComputeMaxFlow(culvert, control, solution).isNorm;
		double maxDepthQ = solution.MaxDepthQ;
		if (!isNorm)
		{
			solution.NormalDepth = maxDepthQ;
			return;
		}
		double qorientedSolutionTerm = solution.Qoriented / (control.MannCoef * Math.Sqrt(culvert.Slope));
		double maxDepthQ2 = maxDepthQ;
		int ordinal = 0;
		bool condition = false;
		while (!condition)
		{
			CulvertXSection culvertXSection = new CulvertXSection
			{
				Depth = maxDepthQ,
				Qtotal = solution.Qoriented
			};
			ComputeXSHydraulicProps(culvert, control, culvertXSection);
			double manningsNCulvertXSectionTerm = qorientedSolutionTerm * culvertXSection.Mann - culvertXSection.Area * Math.Pow(culvertXSection.HydRad, 2.0 / 3.0);
			if (manningsNCulvertXSectionTerm == 0.0)
			{
				maxDepthQ2 = 0.0;
			}
			else
			{
				maxDepthQ2 /= 2.0;
				maxDepthQ2 = Math.Abs(maxDepthQ2) * manningsNCulvertXSectionTerm / Math.Abs(manningsNCulvertXSectionTerm);
			}
			maxDepthQ += maxDepthQ2;
			ordinal++;
			if (Math.Abs(maxDepthQ2) < 0.005 || ordinal > 30)
			{
				condition = true;
			}
		}
		solution.NormalDepth = maxDepthQ;
	}

	private static (double qMax, bool isNorm) ComputeMaxFlow(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution)
	{
		bool condition = true;
		double mannCoefControlTerm = 0.0;
		double isPartiallyBlockedCulvertTerm = 0.0;
		isPartiallyBlockedCulvertTerm = ((!culvert.IsPartiallyBlocked || culvert.Shape == CulvertShape.Box) ? ComputeMaxFlowDepth(culvert, solution) : ComputeMaxFlowDepthFill(culvert, control, solution));
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = isPartiallyBlockedCulvertTerm,
			Qtotal = solution.Qoriented
		};
		ComputeXSHydraulicProps(culvert, control, culvertXSection);
		mannCoefControlTerm = control.MannCoef * culvertXSection.Area * Math.Pow(culvertXSection.HydRad, 2.0 / 3.0) * Math.Sqrt(culvert.Slope) / culvertXSection.Mann;
		if (solution.Qoriented > mannCoefControlTerm)
		{
			condition = false;
			isPartiallyBlockedCulvertTerm = culvert.EffectiveRise;
		}
		solution.MaxDepthQ = isPartiallyBlockedCulvertTerm;
		return (qMax: mannCoefControlTerm, isNorm: condition);
	}

	private static double ComputeMaxFlowDepthFill(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution)
	{
		double result = 0.0;
		double conveyance = 0.0;
		CulvertXSection culvertXSection = new CulvertXSection();
		for (int i = 70; i < 100; i++)
		{
			double depthCulvertXSectionTerm = (culvertXSection.Depth = (double)i / 100.0 * culvert.EffectiveRise);
			culvertXSection.Qtotal = 1.0;
			ComputeXSHydraulicProps(culvert, control, culvertXSection);
			if (culvertXSection.Conveyance > conveyance)
			{
				conveyance = culvertXSection.Conveyance;
				result = depthCulvertXSectionTerm;
			}
		}
		return result;
	}

	public static void ComputeCriticalDepth(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution)
	{
		if (solution.Qoriented <= 0.0)
		{
			solution.Crit.Depth = 0.0;
			solution.Crit.VelHead = 0.0;
			solution.Crit.EG = 0.0;
			solution.Crit.EGDepth = 0.0;
			return;
		}
		bool condition = false;
		CulvertXSection culvertXSection = new CulvertXSection();
		double depth2;
		if (culvert.Shape == CulvertShape.Box)
		{
			depth2 = Math.Pow(solution.Qoriented * solution.Qoriented / (culvert.Span * culvert.Span * control.Grav), 1.0 / 3.0);
			depth2 = Math.Min(depth2, culvert.EffectiveRise);
		}
		else
		{
			double depth3;
			bool condition2;
			(depth2, depth3, condition2) = CriticalDepthInit(culvert, control, solution);
			if (condition2)
			{
				return;
			}
			double computed = 0.01;
			double depth22 = depth2;
			double depth2Term = 0.001;
			double qorientedPolynomialOverGravity = solution.Qoriented * solution.Qoriented / control.Grav;
			culvertXSection.Depth = depth3;
			culvertXSection.Qtotal = solution.Qoriented;
			ComputeXSHydraulicProps(culvert, control, culvertXSection);
			if (qorientedPolynomialOverGravity * 1.01 > culvertXSection.Area * culvertXSection.Area * culvertXSection.Area / culvertXSection.TopWidth)
			{
				depth2 = culvert.EffectiveRise;
			}
			else
			{
				switch (culvert.Shape)
				{
				case CulvertShape.Box:
					depth2 = 1.01 / Math.Pow(culvert.EffectiveRise, 0.26) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
					break;
				case CulvertShape.Circular:
					depth3 = 0.938 * culvert.EffectiveRise;
					depth2 = 1.01 / Math.Pow(culvert.EffectiveRise, 0.26) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
					break;
				case CulvertShape.SemiCircular:
					depth3 = 0.927 * culvert.EffectiveRise;
					break;
				case CulvertShape.Ellipse:
				case CulvertShape.Arch:
					depth2 = 0.84 * Math.Pow(culvert.Span, 0.22) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
					break;
				case CulvertShape.PipeArch:
					depth3 = 0.9 * culvert.EffectiveRise;
					break;
				default:
					throw new NotImplementedException($"Culvert shape {culvert.Shape} not implemented in Critical Depth compute");
				}
				int ordinal = 0;
				int ordinal2 = 0;
				while (!condition2)
				{
					ordinal++;
					if (ordinal > 10)
					{
						condition2 = true;
					}
					depth22 = depth2;
					double depth = depth2 + depth2Term;
					CulvertXSection culvertXSection2 = new CulvertXSection();
					culvertXSection2.Depth = depth;
					culvertXSection2.Qtotal = solution.Qoriented;
					ComputeXSHydraulicProps(culvert, control, culvertXSection2);
					culvertXSection.Depth = depth2;
					culvertXSection.Qtotal = solution.Qoriented;
					ComputeXSHydraulicProps(culvert, control, culvertXSection);
					double areaCulvertXSection2Term = (culvertXSection2.Area - culvertXSection.Area) / depth2Term;
					double topWidthCulvertXSection2Term = (culvertXSection2.TopWidth - culvertXSection.TopWidth) / depth2Term;
					double areaCulvertXSectionTerm = culvertXSection.Area * culvertXSection.Area * culvertXSection.Area - qorientedPolynomialOverGravity * culvertXSection.TopWidth;
					double areaCulvertXSectionTerm2 = 3.0 * culvertXSection.Area * culvertXSection.Area * areaCulvertXSection2Term - qorientedPolynomialOverGravity * topWidthCulvertXSection2Term;
					depth2 = depth22 - areaCulvertXSectionTerm / areaCulvertXSectionTerm2;
					if (depth2 < computed)
					{
						condition2 = true;
					}
					if (depth2 > depth3)
					{
						ordinal2++;
						if (ordinal2 > 2)
						{
							condition2 = true;
						}
						depth2 = depth3;
					}
					else if (Math.Abs(depth2 - depth22) < computed)
					{
						condition = true;
						condition2 = true;
					}
				}
				if (!condition)
				{
					condition2 = false;
					ordinal = 0;
					depth2 = culvert.EffectiveRise / 2.0;
					depth2Term = depth2;
					culvertXSection.Depth = depth2;
					culvertXSection.Qtotal = solution.Qoriented;
					ComputeXSHydraulicProps(culvert, control, culvertXSection);
					double areaCulvertXSectionTerm3 = qorientedPolynomialOverGravity - culvertXSection.Area * culvertXSection.Area * culvertXSection.Area / culvertXSection.TopWidth;
					while (!condition2)
					{
						ordinal++;
						if (depth2Term > computed && ordinal < 30)
						{
							depth2Term /= 2.0;
							depth2 = (culvertXSection.Depth = ((!(areaCulvertXSectionTerm3 < 0.0)) ? (depth2 + depth2Term) : (depth2 - depth2Term)));
							culvertXSection.Qtotal = solution.Qoriented;
							ComputeXSHydraulicProps(culvert, control, culvertXSection);
							areaCulvertXSectionTerm3 = qorientedPolynomialOverGravity - culvertXSection.Area * culvertXSection.Area * culvertXSection.Area / culvertXSection.TopWidth;
						}
						else
						{
							condition2 = true;
							if (depth2Term < computed)
							{
								condition = true;
							}
						}
					}
				}
			}
		}
		culvertXSection.Depth = depth2;
		culvertXSection.Qtotal = solution.Qoriented;
		ComputeXSHydraulicProps(culvert, control, culvertXSection);
		solution.Crit.Depth = depth2;
		solution.Crit.VelHead = culvertXSection.VelHead;
		solution.Crit.EGDepth = depth2 + culvertXSection.VelHead;
		solution.Crit.EG = depth2 + culvertXSection.VelHead;
	}

	private static (double depth, double depthMax, bool done) CriticalDepthInit(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution)
	{
		double depth2 = ComputeMaxFlowDepth(culvert, solution);
		double effectiveRiseCulvertTerm = 0.0;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = depth2,
			Qtotal = solution.Qoriented
		};
		ComputeXSHydraulicProps(culvert, control, culvertXSection);
		double qorientedPolynomialOverGravity = solution.Qoriented * solution.Qoriented / control.Grav;
		if (qorientedPolynomialOverGravity * 1.01 > Math.Pow(culvertXSection.Area, 3.0) / culvertXSection.TopWidth)
		{
			solution.Crit.Depth = culvert.EffectiveRise;
			return (depth: culvert.EffectiveRise, depthMax: depth2, done: true);
		}
		effectiveRiseCulvertTerm = 1.01 / Math.Pow(culvert.EffectiveRise, 0.26) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
		switch (culvert.Shape)
		{
		case CulvertShape.Circular:
			effectiveRiseCulvertTerm = 1.01 / Math.Pow(culvert.EffectiveRise, 0.26) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
			break;
		case CulvertShape.SemiCircular:
			effectiveRiseCulvertTerm = 1.01 / Math.Pow(culvert.EffectiveRise, 0.26) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
			break;
		case CulvertShape.Ellipse:
		case CulvertShape.Arch:
			effectiveRiseCulvertTerm = 0.84 * Math.Pow(culvert.Span, 0.22) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
			break;
		case CulvertShape.PipeArch:
			effectiveRiseCulvertTerm = 1.01 / Math.Pow(culvert.EffectiveRise, 0.26) * Math.Pow(qorientedPolynomialOverGravity, 0.25);
			break;
		default:
			throw new NotImplementedException($"Culvert shape {culvert.Shape} not implemented in Critical Depth compute");
		}
		return (depth: effectiveRiseCulvertTerm, depthMax: depth2, done: false);
	}

	private static double ComputeMaxFlowDepth(FlowOrientedCulvert culvert, CulvertSolution solution)
	{
		double maxFlowDepth = 0.0;
		switch (culvert.Shape)
		{
		case CulvertShape.Box:
			maxFlowDepth = culvert.EffectiveRise - 0.01;
			break;
		case CulvertShape.Circular:
			maxFlowDepth = 0.938 * culvert.EffectiveRise;
			break;
		case CulvertShape.SemiCircular:
			maxFlowDepth = 0.927 * culvert.EffectiveRise;
			break;
		case CulvertShape.Ellipse:
		case CulvertShape.Arch:
			if (culvert.Span * 2.0 <= culvert.EffectiveRise)
			{
				maxFlowDepth = 0.96 * culvert.EffectiveRise;
			}
			else if (culvert.Span < culvert.EffectiveRise)
			{
				maxFlowDepth = 0.95 * culvert.EffectiveRise;
			}
			else if (culvert.Span < culvert.EffectiveRise * 1.2)
			{
				maxFlowDepth = 0.94 * culvert.EffectiveRise;
			}
			else
			{
				maxFlowDepth = ((!(culvert.Span < culvert.EffectiveRise * 2.0)) ? (0.92 * culvert.EffectiveRise) : (0.93 * culvert.EffectiveRise));
			}
			break;
		case CulvertShape.PipeArch:
			maxFlowDepth = 0.9 * culvert.EffectiveRise;
			break;
		default:
			throw new NotImplementedException($"Culvert shape {culvert.Shape} not implemented in Max Depth compute");
		}
		if (culvert.IsPartiallyBlocked)
		{
			maxFlowDepth -= culvert.DepthBlocked;
		}
		return maxFlowDepth;
	}
}
