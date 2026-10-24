using System;

namespace Ras.Engine.Structures.Culverts;

public static class CulvertFlowSolver
{
	public static CulvertFlowSolverResult Solve(CulvertPhysProps culvert, CulvertBoundaryConditions bcs, CulvertSolverControl control, double? initialGuessFlow = null)
	{
		double upstreamEnergyGrade = bcs.UpstreamEnergyGrade;
		double downstreamEnergyGrade = bcs.DownstreamEnergyGrade;
		bool isReverseFlow = upstreamEnergyGrade < downstreamEnergyGrade;
		double downstreamEnergyGrade2 = (isReverseFlow ? downstreamEnergyGrade : upstreamEnergyGrade);
		FlowOrientedCulvert culvert2 = culvert.AsFlowOriented(isReverseFlow);
		FlowOrientedBCs flowOrientedBCs = bcs.AsFlowOriented(isReverseFlow);
		CulvertSolution culvertSolution = new CulvertSolution(control);
		culvertSolution.WSEUS = flowOrientedBCs.UpstreamWse;
		culvertSolution.WSEDS = flowOrientedBCs.DownstreamWse;
		culvertSolution.VelHeadUS = flowOrientedBCs.UpstreamVelocityHead;
		culvertSolution.VelHeadDS = flowOrientedBCs.DownstreamVelocityHead;
		culvertSolution.EGUS = flowOrientedBCs.UpstreamEnergyGrade;
		culvertSolution.EGDS = flowOrientedBCs.DownstreamEnergyGrade;
		culvertSolution.IsReverseFlow = isReverseFlow;
		CulvertSolverConvergence culvertSolverConvergence = new CulvertSolverConvergence(0, isConverged: false, downstreamEnergyGrade2, 0.0);
		double energyGradeTolerance = control.EnergyGradeTolerance;
		if (IsZeroFlow(culvert2, control, flowOrientedBCs))
		{
			culvertSolution.Qoriented = 0.0;
			culvertSolverConvergence.IsConverged = true;
			return new CulvertFlowSolverResult(culvertSolution, culvertSolverConvergence);
		}
		double qMin = 0.0;
		double qMax = 10000000000.0;
		double egMin = culvertSolution.EGDS;
		double egMax = double.NaN;
		double initialFlowGuess = ((!initialGuessFlow.HasValue) ? ComputeInitialFlowGuess(culvert2, control, flowOrientedBCs, isReverseFlow, downstreamEnergyGrade2, ref qMax) : Math.Abs(initialGuessFlow.Value));
		double computedEnergyGrade = ComputeEnergyGrade(culvert2, control, culvertSolution, initialFlowGuess);
		bool isLow = computedEnergyGrade < downstreamEnergyGrade2;
		culvertSolverConvergence.Iterations = 1;
		UpdateBracket(initialFlowGuess, computedEnergyGrade, downstreamEnergyGrade2, energyGradeTolerance, ref qMin, ref qMax, ref egMin, ref egMax, ref isLow);
		if (Math.Abs(downstreamEnergyGrade2 - computedEnergyGrade) <= energyGradeTolerance)
		{
			culvertSolverConvergence.IsConverged = true;
			culvertSolverConvergence.ComputedEnergyGrade = computedEnergyGrade;
			return new CulvertFlowSolverResult(culvertSolution, culvertSolverConvergence);
		}
		double initialFlowGuess2 = initialFlowGuess;
		if (isLow)
		{
			initialFlowGuess2 = qMax;
			computedEnergyGrade = ComputeEnergyGrade(culvert2, control, culvertSolution, initialFlowGuess2);
			UpdateBracket(initialFlowGuess2, computedEnergyGrade, downstreamEnergyGrade2, energyGradeTolerance, ref qMin, ref qMax, ref egMin, ref egMax, ref isLow);
			if (isLow)
			{
				initialFlowGuess2 = initialFlowGuess;
				for (int i = 0; i < 7; i++)
				{
					initialFlowGuess2 = 2.0 * initialFlowGuess2;
					computedEnergyGrade = ComputeEnergyGrade(culvert2, control, culvertSolution, initialFlowGuess2);
					UpdateBracket(initialFlowGuess2, computedEnergyGrade, downstreamEnergyGrade2, energyGradeTolerance, ref qMin, ref qMax, ref egMin, ref egMax, ref isLow);
					if (!isLow)
					{
						break;
					}
				}
			}
		}
		if (qMax > 3.0 * qMin)
		{
			double computed;
			if (initialGuessFlow.HasValue)
			{
				initialFlowGuess2 = 0.8 * initialFlowGuess2;
				computed = 1.5;
			}
			else
			{
				initialFlowGuess2 = 0.6 * initialFlowGuess2;
				computed = 2.0;
			}
			for (int j = 0; j < 6; j++)
			{
				if (Math.Abs(initialFlowGuess2) > qMax || Math.Abs(initialFlowGuess2) < qMin)
				{
					initialFlowGuess2 = 0.5 * (qMax + qMin);
				}
				computedEnergyGrade = ComputeEnergyGrade(culvert2, control, culvertSolution, initialFlowGuess2);
				UpdateBracket(initialFlowGuess2, computedEnergyGrade, downstreamEnergyGrade2, energyGradeTolerance, ref qMin, ref qMax, ref egMin, ref egMax, ref isLow);
				if (qMax <= 3.5 * qMin)
				{
					break;
				}
				initialFlowGuess2 /= computed;
				computed *= 1.2;
			}
		}
		bool condition = false;
		int k;
		for (k = 2; k <= control.MaxIterations; k++)
		{
			initialFlowGuess2 = (condition ? (0.5 * (qMax + qMin)) : CulvertMath.Interpolate(egMax, egMin, downstreamEnergyGrade2, qMax, qMin));
			computedEnergyGrade = ComputeEnergyGrade(culvert2, control, culvertSolution, initialFlowGuess2);
			if (Math.Abs(downstreamEnergyGrade2 - computedEnergyGrade) < energyGradeTolerance)
			{
				culvertSolverConvergence.IsConverged = true;
				culvertSolverConvergence.ComputedEnergyGrade = computedEnergyGrade;
				culvertSolverConvergence.Iterations = k;
				return new CulvertFlowSolverResult(culvertSolution, culvertSolverConvergence);
			}
			isLow = computedEnergyGrade < downstreamEnergyGrade2;
			UpdateBracket(initialFlowGuess2, computedEnergyGrade, downstreamEnergyGrade2, energyGradeTolerance, ref qMin, ref qMax, ref egMin, ref egMax, ref isLow);
			if (Math.Abs(qMin - qMax) * qMax < 1E-06)
			{
				culvertSolverConvergence.IsConverged = true;
				break;
			}
			condition = !condition;
		}
		culvertSolverConvergence.ComputedEnergyGrade = computedEnergyGrade;
		culvertSolverConvergence.Iterations = k;
		return new CulvertFlowSolverResult(culvertSolution, culvertSolverConvergence);
	}

	private static bool IsZeroFlow(FlowOrientedCulvert culvert, CulvertSolverControl control, FlowOrientedBCs bcs)
	{
		double invertCulvertTerm = Math.Max(culvert.InvertUS, culvert.InvertDS);
		if (bcs.UpstreamEnergyGrade - invertCulvertTerm < control.ZeroFlowDepthTolerance)
		{
			return true;
		}
		if (Math.Abs(bcs.UpstreamEnergyGrade - bcs.DownstreamEnergyGrade) < control.ZeroFlowEgDiffTolerance)
		{
			return true;
		}
		return false;
	}

	private static double ComputeEnergyGrade(FlowOrientedCulvert culvert, CulvertSolverControl control, CulvertSolution solution, double q)
	{
		solution.Qoriented = q;
		CulvertEnergyGradeSolver.Compute(culvert, control, solution);
		return solution.EGUS;
	}

	private static void UpdateBracket(double qCheck, double computedEg, double knownEg, double egToler, ref double qMin, ref double qMax, ref double egMin, ref double egMax, ref bool isLow)
	{
		if (computedEg + egToler < knownEg)
		{
			isLow = true;
			if (qCheck >= qMin)
			{
				qMin = qCheck;
				egMin = computedEg;
			}
		}
		else
		{
			isLow = false;
			if (qCheck <= qMax)
			{
				qMax = qCheck;
				egMax = computedEg;
			}
		}
	}

	private static double ComputeInitialFlowGuess(FlowOrientedCulvert culvert, CulvertSolverControl control, FlowOrientedBCs bcs, bool isReverse, double knownEg, ref double qMax)
	{
		double d = bcs.UpstreamEnergyGrade - Math.Max(bcs.DownstreamEnergyGrade, culvert.InvertDS);
		double depth = bcs.UpstreamEnergyGrade - culvert.InvertUS;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = depth,
			Qtotal = 1.0
		};
		CulvertSectionHydraulics.ComputeXSHydraulicProps(culvert, control, culvertXSection);
		double areaCulvertXSectionTerm = 5.613999999999999 * culvertXSection.Area * Math.Sqrt(d);
		double x = depth / (culvert.EffectiveRise * culvert.InletCoefUS.K);
		double areaCulvertXSectionTerm2 = culvertXSection.Area * Math.Sqrt(culvert.EffectiveRise) * Math.Pow(x, 1.0 / culvert.InletCoefUS.M);
		x = (depth / culvert.EffectiveRise - culvert.InletCoefUS.Y + culvert.InletCoefUS.SlopeCorrection * culvert.Slope) / culvert.InletCoefUS.C;
		x = Math.Abs(x);
		double areaCulvertXSectionTerm3 = culvertXSection.Area * Math.Sqrt(culvert.EffectiveRise * x);
		x = areaCulvertXSectionTerm3 / (culvertXSection.Area * Math.Sqrt(culvert.EffectiveRise));
		double computed = 0.0;
		double computed2 = 0.0;
		if (x > 4.0)
		{
			computed = areaCulvertXSectionTerm3;
			computed2 = areaCulvertXSectionTerm3;
		}
		else
		{
			x = areaCulvertXSectionTerm2 / (culvertXSection.Area * Math.Sqrt(culvert.EffectiveRise));
			if (x <= 3.5)
			{
				computed = areaCulvertXSectionTerm2;
				computed2 = areaCulvertXSectionTerm2;
			}
			else
			{
				computed = 0.5 * (areaCulvertXSectionTerm3 + areaCulvertXSectionTerm2);
				computed2 = Math.Max(areaCulvertXSectionTerm3, areaCulvertXSectionTerm2);
			}
		}
		qMax = Math.Max(areaCulvertXSectionTerm, computed2);
		if (x >= 3.5 && bcs.DownstreamWse >= culvert.InvertDS + culvert.EffectiveRise)
		{
			return areaCulvertXSectionTerm;
		}
		return Math.Min(computed, areaCulvertXSectionTerm);
	}
}
