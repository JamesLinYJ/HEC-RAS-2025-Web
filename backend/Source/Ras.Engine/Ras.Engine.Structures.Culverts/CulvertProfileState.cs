using System;

namespace Ras.Engine.Structures.Culverts;

public class CulvertProfileState
{
	public double Depth;

	public double FrictionLoss;

	public double PartiallyFullLength;

	public double DirectStepVertAdj;

	public double DirectStepVertAdjFirst;

	public double DirectStepVertAdjUnit;

	public bool IsDone;

	public bool IsSubCritical;

	public bool IsOutletFull;

	public bool IsOutletCritical;

	public bool TransitionedToFullFlow;

	public bool TransitionedToFreeSfc;

	public bool IsInletCritical;

	public bool IsInletFull;

	public bool IsInletFree;

	public bool IsMaxQReverse;

	public int CurrentStationIdx;

	public double RunningTotalLengthInternal;

	public double PrevLengthInternal;

	public double PrevDepthInternal;

	public double[] StationLocation { get; set; }

	public double[] DepthInternalSuperCrit { get; set; }

	public int MixedFlowType { get; set; }

	public CulvertProfileState(CulvertSolverControl control)
	{
		InitInternalArrays(control.NumProfileStations);
	}

	private void InitInternalArrays(int count)
	{
		StationLocation = new double[count];
		DepthInternalSuperCrit = new double[count];
	}

	public void StoreDepthInternal(FlowOrientedCulvert culvert, CulvertSolution solution, double length, double depth, bool isInterpolate)
	{
		int depthInternalLength = solution.DepthInternal.Length;
		int unity = 0;
		bool condition = true;
		if (IsSubCritical)
		{
			if (CurrentStationIdx <= 1)
			{
				return;
			}
			unity = -1;
			PrevLengthInternal = RunningTotalLengthInternal;
			RunningTotalLengthInternal = length;
			if (RunningTotalLengthInternal <= StationLocation[CurrentStationIdx - 1])
			{
				condition = false;
				if (PrevLengthInternal > StationLocation[CurrentStationIdx] || PrevLengthInternal <= length)
				{
					PrevLengthInternal = StationLocation[CurrentStationIdx];
					PrevDepthInternal = solution.DepthInternal[CurrentStationIdx];
				}
			}
		}
		else
		{
			if (CurrentStationIdx >= depthInternalLength - 2)
			{
				return;
			}
			unity = 1;
			PrevLengthInternal = RunningTotalLengthInternal;
			RunningTotalLengthInternal = length;
			if (RunningTotalLengthInternal >= StationLocation[CurrentStationIdx + 1])
			{
				condition = false;
				if (PrevLengthInternal < StationLocation[CurrentStationIdx] || PrevLengthInternal >= length)
				{
					PrevLengthInternal = StationLocation[CurrentStationIdx];
					PrevDepthInternal = solution.DepthInternal[CurrentStationIdx];
				}
			}
		}
		while (!condition)
		{
			double interpolateCulvertMathTerm = 0.0;
			if (isInterpolate)
			{
				interpolateCulvertMathTerm = CulvertMath.Interpolate(PrevLengthInternal, RunningTotalLengthInternal, StationLocation[CurrentStationIdx + unity], PrevDepthInternal, depth);
				interpolateCulvertMathTerm = Math.Min(interpolateCulvertMathTerm, culvert.EffectiveRise);
			}
			else
			{
				interpolateCulvertMathTerm = depth;
			}
			if (IsSubCritical)
			{
				solution.DepthInternal[CurrentStationIdx + unity] = interpolateCulvertMathTerm;
			}
			else
			{
				DepthInternalSuperCrit[CurrentStationIdx + unity] = interpolateCulvertMathTerm;
			}
			CurrentStationIdx += unity;
			if (IsSubCritical)
			{
				if (RunningTotalLengthInternal > StationLocation[CurrentStationIdx - 1])
				{
					condition = true;
				}
				else if (RunningTotalLengthInternal < StationLocation[CurrentStationIdx + 1])
				{
					condition = true;
				}
			}
			if (CurrentStationIdx + unity >= depthInternalLength - 1 || CurrentStationIdx + unity <= 0)
			{
				condition = true;
			}
		}
		PrevDepthInternal = depth;
	}
}
