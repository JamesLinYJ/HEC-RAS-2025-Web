using System;
using Utility;

namespace Ras.Engine;

public class StepChecker
{
	public enum RejectReason
	{
		ACCEPT,
		ELEVATION_CHANGE,
		ELEVATION_NAN,
		VOLUME_NEGATIVE,
		VELOCITY_NAN,
		LOCAL_VOLUME_NEGATIVE,
		ERROR
	}

	private SolverControl control;

	public ThreadFor threadFor;

	public RejectReason reason;

	public int rejectCell;

	public int rejectFace;

	public bool Success => reason == RejectReason.ACCEPT;

	public StepChecker(SolverControl control)
	{
		this.control = control;
		reason = RejectReason.ACCEPT;
		rejectCell = -1;
		threadFor = new ThreadFor(1);
	}

	public void Clear()
	{
		reason = RejectReason.ACCEPT;
		rejectCell = -1;
		rejectFace = -1;
	}

	public bool CheckStep(MeshWaterState prevState, MeshWaterState newState)
	{
		Clear();
		threadFor.Run(10000, (int start, int end) =>
		{
			double fBegin = (double)start / 10000.0;
			double fEnd = (double)(end + 1) / 10000.0;
			CheckStepThread(prevState, newState, fBegin, fEnd);
		});
		return Success;
	}

	public void CheckStepThread(MeshWaterState prevState, MeshWaterState newState, double fBegin, double fEnd)
	{
		RejectReason rejectReason = RejectReason.ACCEPT;
		int rejectCell2 = -1;
		int rejectFace2 = -1;
		int fBeginTimesLength = (int)(fBegin * (double)prevState.CellElevation.Length);
		int count = (int)(fEnd * (double)prevState.CellElevation.Length) - 1;
		for (int i = fBeginTimesLength; i <= count; i++)
		{
			if (Math.Abs(newState.CellElevation[i] - prevState.CellElevation[i]) > control.checkMaxElevationChange)
			{
				rejectReason = RejectReason.ELEVATION_CHANGE;
				rejectCell2 = i;
			}
			else if (!double.IsFinite(newState.CellElevation[i]))
			{
				rejectReason = RejectReason.ELEVATION_NAN;
				rejectCell2 = i;
			}
			if (newState.CellVolume[i] < 0.0)
			{
				rejectReason = RejectReason.VOLUME_NEGATIVE;
				rejectCell2 = i;
			}
		}
		if (rejectReason != RejectReason.ACCEPT)
		{
			lock (this)
			{
				reason = rejectReason;
				rejectCell = rejectCell2;
				return;
			}
		}
		int fBeginTimesLength2 = (int)(fBegin * (double)prevState.FaceVelocity.Length);
		int count2 = (int)(fEnd * (double)prevState.FaceVelocity.Length) - 1;
		for (int j = fBeginTimesLength2; j <= count2; j++)
		{
			if (!double.IsFinite(newState.FaceVelocity[j]))
			{
				rejectReason = RejectReason.VELOCITY_NAN;
				rejectFace2 = j;
			}
		}
		if (rejectReason != RejectReason.ACCEPT)
		{
			lock (this)
			{
				reason = rejectReason;
				rejectFace = rejectFace2;
			}
		}
	}
}
