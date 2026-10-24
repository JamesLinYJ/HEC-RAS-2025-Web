using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ras.Engine.Engine;

public class LoopConvergence
{
	public enum ConvStatus
	{
		STALLED_RMS_BAD,
		STALLED_MAX_BAD,
		DIVERGENT,
		MAX_ITERATIONS,
		SKIPPED,
		CONVERGED_MAX,
		CONVERGED_RMS,
		STALLED_MAX_OKAY,
		STALLED_RMS_OKAY,
		ITERATING,
		SMALL_NORM
	}

	public bool CheckConvergence = true;

	public ConvStatus Status;

	public int MaxIterations;

	public int CellCount;

	public bool isInner;

	public int Iterations;

	public double[] IterErrorMax;

	public int[] IterCellMax;

	public double[] IterErrorRMS;

	public ulong[] HistoIterCount;

	public ulong[] HistoCellCount;

	public bool IsContinue => Status == ConvStatus.ITERATING;

	public long TotalIterations { get; private set; }

	public double ErrorMax
	{
		get
		{
			if (IterErrorMax.Length == 0)
			{
				return 0.0;
			}
			return IterErrorMax[Iterations];
		}
	}

	public int CellMax
	{
		get
		{
			if (IterCellMax.Length == 0)
			{
				return 0;
			}
			return IterCellMax[Iterations];
		}
	}

	public double ErrorRMS
	{
		get
		{
			if (IterErrorRMS.Length == 0)
			{
				return 0.0;
			}
			return IterErrorRMS[Iterations];
		}
	}

	public void Prepare(int maxIter, int countCells)
	{
		Iterations = 0;
		TotalIterations = 0L;
		Status = ConvStatus.ITERATING;
		MaxIterations = maxIter;
		CellCount = countCells;
		IterErrorMax = new double[maxIter + 1];
		IterCellMax = new int[maxIter + 1];
		IterErrorRMS = new double[maxIter + 1];
		HistoIterCount = new ulong[maxIter + 1];
		HistoCellCount = new ulong[countCells];
	}

	public void IncrementIterations()
	{
		Iterations++;
		TotalIterations++;
	}

	public void Compute(SolverControl.LoopControl loopControl, double[] errorValues, List<int> indices, bool[] mask)
	{
		int count = indices.Count;
		ComputeMax(errorValues, indices, count, mask);
		Status = ConvStatus.ITERATING;
		if (ErrorMax <= loopControl.tolConv)
		{
			Status = ConvStatus.CONVERGED_MAX;
		}
		else if (Iterations >= loopControl.IterMax)
		{
			Status = ConvStatus.MAX_ITERATIONS;
		}
		else if (ErrorMax <= loopControl.tolMax)
		{
			ComputeRMS(errorValues, indices, count, mask);
			if (ErrorRMS <= loopControl.tolRMS)
			{
				Status = ConvStatus.CONVERGED_RMS;
			}
		}
		if (!IsContinue)
		{
			if (Iterations >= 0 && Iterations < HistoIterCount.Length)
			{
				HistoIterCount[Iterations]++;
			}
			if (CellMax >= 0 && CellMax < HistoCellCount.Length)
			{
				HistoCellCount[CellMax]++;
			}
		}
	}

	private void ComputeMax(double[] errorValues, List<int> indices, int count, bool[] mask)
	{
		if (count == 0)
		{
			IterErrorMax[Iterations] = 0.0;
			IterCellMax[Iterations] = -1;
			return;
		}
		double iterErrorMax = 0.0;
		int iterCellMax = -1;
		Span<double> errorValuesSpan = errorValues.AsSpan();
		Span<int> indicesSpan = CollectionsMarshal.AsSpan(indices);
		Span<bool> maskSpan = mask.AsSpan();
		for (int i = 0; i < count; i++)
		{
			int index = indicesSpan[i];
			if (maskSpan[index])
			{
				double absoluteErrorValue = Math.Abs(errorValuesSpan[index]);
				if (absoluteErrorValue > iterErrorMax)
				{
					iterErrorMax = absoluteErrorValue;
					iterCellMax = index;
				}
			}
		}
		IterErrorMax[Iterations] = iterErrorMax;
		IterCellMax[Iterations] = iterCellMax;
	}

	private void ComputeRMS(double[] errorValues, List<int> indices, int count, bool[] mask)
	{
		if (count == 0)
		{
			IterErrorRMS[Iterations] = 0.0;
			return;
		}
		double iterErrorRMS = 0.0;
		Span<double> errorValuesSpan = errorValues.AsSpan();
		Span<int> indicesSpan = CollectionsMarshal.AsSpan(indices);
		Span<bool> maskSpan = mask.AsSpan();
		for (int i = 0; i < count; i++)
		{
			int index = indicesSpan[i];
			if (maskSpan[index])
			{
				iterErrorRMS += errorValuesSpan[index] * errorValuesSpan[index];
			}
		}
		if (iterErrorRMS > 0.0)
		{
			iterErrorRMS = Math.Sqrt(iterErrorRMS / (double)count);
		}
		IterErrorRMS[Iterations] = iterErrorRMS;
	}

	public void Reset()
	{
		Iterations = 0;
		for (int i = 0; i <= MaxIterations; i++)
		{
			IterErrorMax[i] = 0.0;
			IterErrorRMS[i] = 0.0;
			IterCellMax[i] = 0;
		}
		Status = ConvStatus.ITERATING;
	}
}
