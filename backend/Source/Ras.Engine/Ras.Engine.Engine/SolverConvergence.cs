using System.Collections.Generic;

namespace Ras.Engine.Engine;

public class SolverConvergence
{
	public LoopConvergence InnerConv;

	public LoopConvergence OuterConv;

	public SolverConvergence()
	{
		InnerConv = new LoopConvergence
		{
			isInner = true
		};
		OuterConv = new LoopConvergence
		{
			isInner = false
		};
	}

	public void Prepare(SolverControl.LoopControl InnerControl, SolverControl.LoopControl OuterControl, int cellCount)
	{
		InnerConv.Prepare(InnerControl.IterMax, cellCount);
		OuterConv.Prepare(OuterControl.IterMax, cellCount);
	}

	public void Prepare(SolverControl.LoopControl OuterControl, int cellCount)
	{
		OuterConv.Prepare(OuterControl.IterMax, cellCount);
	}

	public void ComputeInner(SolverControl.LoopControl InnerControl, double[] errorValues, List<int> indices, bool[] maskArray)
	{
		InnerConv.Compute(InnerControl, errorValues, indices, maskArray);
	}

	public void ComputeOuter(SolverControl.LoopControl OuterControl, double[] errorValues, List<int> indices, bool[] maskArray)
	{
		OuterConv.Compute(OuterControl, errorValues, indices, maskArray);
	}

	public bool ContinueInner()
	{
		return InnerConv.Status == LoopConvergence.ConvStatus.ITERATING;
	}

	public bool ContinueOuter()
	{
		return OuterConv.Status == LoopConvergence.ConvStatus.ITERATING;
	}

	public void ResetInner()
	{
		InnerConv.Reset();
	}

	public void ResetOuter()
	{
		OuterConv.Reset();
	}
}
