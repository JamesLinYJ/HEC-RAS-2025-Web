using System;
using System.Linq;

namespace Ras.Engine;

public class BoundaryCondition
{
	public enum BCType
	{
		None,
		Flow,
		Stage,
		RatingCurve,
		NormalDepth,
		VariableFlow,
		VariableStage,
		InternalFlow,
		SpatiallyVariableStageVelocity
	}

	public const int FaceMultipleValueStage = 0;

	public const int FaceMultipleValueVelocityX = 1;

	public const int FaceMultipleValueVelocityY = 2;

	public int[] Faces;

	public double[] FaceFractions;

	public int[] Cells;

	public double[] CellFractions;

	public int IndexState;

	public double TimeState = double.NaN;

	public double DurationState;

	public double ValueState;

	public double[] FaceValuesState;

	public double[,] FaceMultipleValuesState;

	public double[] CellValuesState;

	public double[,] CellMultipleValuesState;

	public double[] Timesteps;

	public double[] Values;

	public double[,] FaceValues;

	public double[,,] FaceMultipleValues;

	public double[,] CellValues;

	public double[,] RatingCurve;

	public double EGSlope = 0.01;

	public string Name { get; set; }

	public CellWaterSourceId WaterSourceId { get; internal set; } = CellWaterSourceId.Invalid;

	public bool UseLateralDistribution { get; set; }

	public float[] BoundaryNodeBedElevations { get; set; }

	public IFaceApportionment FaceApportionment { get; set; }

	public BoundaryCondition ShallowCopy()
	{
		return new BoundaryCondition
		{
			Name = Name,
			Faces = Faces,
			FaceFractions = FaceFractions,
			Cells = Cells,
			CellFractions = CellFractions,
			Timesteps = Timesteps,
			Values = Values,
			FaceValues = FaceValues,
			FaceMultipleValues = FaceMultipleValues,
			RatingCurve = RatingCurve,
			EGSlope = EGSlope,
			UseLateralDistribution = UseLateralDistribution,
			BoundaryNodeBedElevations = BoundaryNodeBedElevations,
			FaceApportionment = FaceApportionment
		};
	}

	public static BoundaryCondition ConstantFaceValue(int[] faces, double doubleValue)
	{
		BoundaryCondition boundaryCondition = new BoundaryCondition();
		boundaryCondition.Faces = faces;
		boundaryCondition.FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray();
		boundaryCondition.Timesteps = new double[2] { -1E+32, 1E+32 };
		boundaryCondition.Values = new double[2] { doubleValue, doubleValue };
		return boundaryCondition;
	}

	public static BoundaryCondition ConstantCellValue(int[] cells, double doubleValue)
	{
		BoundaryCondition boundaryCondition = new BoundaryCondition();
		boundaryCondition.Cells = cells;
		boundaryCondition.CellFractions = Enumerable.Repeat(1.0 / (double)cells.Length, cells.Length).ToArray();
		boundaryCondition.Timesteps = new double[2] { -1E+32, 1E+32 };
		boundaryCondition.Values = new double[2] { doubleValue, doubleValue };
		return boundaryCondition;
	}

	public static BoundaryCondition VariableValues(int[] faces, double[] timeSteps, double[] values)
	{
		return new BoundaryCondition
		{
			Faces = faces,
			FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray(),
			Timesteps = timeSteps,
			Values = values
		};
	}

	public double ValueAtTime(double absTime)
	{
		if (!EqualishTimes(absTime, TimeState))
		{
			SetTime(absTime);
			if (IndexState >= 0)
			{
				ValueState = Values[IndexState];
			}
			else
			{
				ValueState = InterpolateValue();
			}
		}
		return ValueState;
	}

	public double GetFlowFromStage(double stage)
	{
		return GetFlowFromStage(RatingCurve, stage);
	}

	public static double GetFlowFromStage(double[,] stageToFlow, double stage)
	{
		if (stageToFlow == null)
		{
			throw new InvalidOperationException("Rating curve BC data is not initialized.");
		}
		switch (stageToFlow.GetLength(0))
		{
		case 0:
			throw new InvalidOperationException("Rating curve BC data is invalid.");
		case 1:
			return stageToFlow[0, 1];
		default:
		{
			int indexOfStage = FindIndexOfStage(stageToFlow, stage);
			double stageToFlowIndexOfStageTerm = stageToFlow[indexOfStage, 0];
			double stageToFlowIndexOfStageTerm2 = stageToFlow[indexOfStage + 1, 0];
			double stageToFlowIndexOfStageTerm3 = stageToFlow[indexOfStage, 1];
			double stageToFlowIndexOfStageTerm4 = stageToFlow[indexOfStage + 1, 1];
			if (stageToFlowIndexOfStageTerm2 == stageToFlowIndexOfStageTerm)
			{
				return stageToFlowIndexOfStageTerm3;
			}
			double submergenceRatio = (stage - stageToFlowIndexOfStageTerm) / (stageToFlowIndexOfStageTerm2 - stageToFlowIndexOfStageTerm);
			return stageToFlowIndexOfStageTerm3 + (stageToFlowIndexOfStageTerm4 - stageToFlowIndexOfStageTerm3) * submergenceRatio;
		}
		}
	}

	private static int FindIndexOfStage(double[,] stageToFlowRatingCurve, double stage)
	{
		int length = stageToFlowRatingCurve.GetLength(0);
		if (stageToFlowRatingCurve[0, 0] <= stageToFlowRatingCurve[length - 1, 0])
		{
			if (stage < stageToFlowRatingCurve[0, 0] || stage > stageToFlowRatingCurve[length - 1, 0])
			{
				throw new InvalidOperationException($"Rating curve BC stage {stage} is outside supported range [{stageToFlowRatingCurve[0, 0]}, {stageToFlowRatingCurve[length - 1, 0]}].");
			}
		}
		else if (stage > stageToFlowRatingCurve[0, 0] || stage < stageToFlowRatingCurve[length - 1, 0])
		{
			throw new InvalidOperationException($"Rating curve BC stage {stage} is outside supported range [{stageToFlowRatingCurve[length - 1, 0]}, {stageToFlowRatingCurve[0, 0]}].");
		}
		for (int i = 1; i < length; i++)
		{
			double stageToFlowRatingCurveTerm = stageToFlowRatingCurve[i - 1, 0];
			double stageToFlowRatingCurveTerm2 = stageToFlowRatingCurve[i, 0];
			if ((stage >= stageToFlowRatingCurveTerm && stage <= stageToFlowRatingCurveTerm2) || (stage <= stageToFlowRatingCurveTerm && stage >= stageToFlowRatingCurveTerm2))
			{
				return i - 1;
			}
		}
		throw new InvalidOperationException($"Rating curve BC stage {stage} is outside supported segments.");
	}

	private double InterpolateValue()
	{
		int indexStateTerm = ~IndexState;
		double timestep = Timesteps[indexStateTerm - 1];
		double timestep2 = Timesteps[indexStateTerm];
		double submergenceRatio = (TimeState - timestep) / (timestep2 - timestep);
		double computed = Values[indexStateTerm - 1];
		double computed2 = Values[indexStateTerm];
		return computed + (computed2 - computed) * submergenceRatio;
	}

	public double AverageValueOverInterval(double startTime, double duration)
	{
		if (duration <= 0.0)
		{
			return ValueAtTime(startTime);
		}
		if (EqualishTimes(startTime, TimeState) && EqualishTimes(duration, DurationState))
		{
			return ValueState;
		}
		double startTimePlusDuration = startTime + duration;
		SetTimeAndDuration(startTime, duration);
		int indexState = IndexState;
		double startTime2 = startTime;
		double computed = 0.0;
		while (startTime2 < startTimePlusDuration)
		{
			double timestep = Timesteps[indexState];
			double timestep2 = Timesteps[indexState + 1];
			double startTime22 = Math.Min(timestep2, startTimePlusDuration);
			double timestep2AboveTimestep = timestep2 - timestep;
			double interpolatedValue = Values[indexState] + (Values[indexState + 1] - Values[indexState]) * ((startTime2 - timestep) / timestep2AboveTimestep);
			double interpolatedValue2 = Values[indexState] + (Values[indexState + 1] - Values[indexState]) * ((startTime22 - timestep) / timestep2AboveTimestep);
			computed += (startTime22 - startTime2) * (interpolatedValue + interpolatedValue2) * 0.5;
			startTime2 = startTime22;
			indexState++;
		}
		ValueState = computed / duration;
		return ValueState;
	}

	public static BoundaryCondition ConstantFaceValues(int[] faces, double[] faceValues)
	{
		BoundaryCondition boundaryCondition = new BoundaryCondition();
		boundaryCondition.Faces = faces;
		boundaryCondition.FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray();
		boundaryCondition.Timesteps = new double[2] { -1E+32, 1E+32 };
		BoundaryCondition boundaryCondition2 = boundaryCondition;
		int faceLength = faces.Length;
		boundaryCondition2.FaceValues = new double[2, faceLength];
		for (int i = 0; i < faceLength; i++)
		{
			boundaryCondition2.FaceValues[0, i] = faceValues[i];
			boundaryCondition2.FaceValues[1, i] = faceValues[i];
		}
		return boundaryCondition2;
	}

	public static BoundaryCondition VariableFaceValues(int[] faces, double[] timeSteps, double[,] faceValues)
	{
		return new BoundaryCondition
		{
			Faces = faces,
			FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray(),
			Timesteps = timeSteps,
			FaceValues = faceValues
		};
	}

	public double[] FaceValuesAtTime(double absTime)
	{
		if (FaceValuesState == null)
		{
			FaceValuesState = new double[FaceValues.GetLength(1)];
		}
		if (!EqualishTimes(absTime, TimeState))
		{
			SetTime(absTime);
			if (IndexState >= 0)
			{
				for (int i = 0; i < Faces.Length; i++)
				{
					FaceValuesState[i] = FaceValues[IndexState, i];
				}
			}
			else
			{
				InterpolateElementValues(FaceValues, FaceValuesState);
			}
		}
		return FaceValuesState;
	}

	public double[,] FaceMultipleValuesAtTime(double absTime)
	{
		int length = FaceMultipleValues.GetLength(1);
		int length2 = FaceMultipleValues.GetLength(2);
		if (FaceMultipleValuesState == null)
		{
			FaceMultipleValuesState = new double[length, length2];
		}
		if (!EqualishTimes(absTime, TimeState))
		{
			SetTime(absTime);
			if (IndexState >= 0)
			{
				for (int i = 0; i < length; i++)
				{
					for (int j = 0; j < length2; j++)
					{
						FaceMultipleValuesState[i, j] = FaceMultipleValues[IndexState, i, j];
					}
				}
			}
			else
			{
				int indexStateTerm = ~IndexState;
				double timestep = Timesteps[indexStateTerm - 1];
				double timestep2 = Timesteps[indexStateTerm];
				double submergenceRatio = (TimeState - timestep) / (timestep2 - timestep);
				for (int k = 0; k < length; k++)
				{
					for (int l = 0; l < length2; l++)
					{
						double faceMultipleValuesTerm = FaceMultipleValues[indexStateTerm - 1, k, l];
						double faceMultipleValuesTerm2 = FaceMultipleValues[indexStateTerm, k, l];
						FaceMultipleValuesState[k, l] = faceMultipleValuesTerm + (faceMultipleValuesTerm2 - faceMultipleValuesTerm) * submergenceRatio;
					}
				}
			}
		}
		return FaceMultipleValuesState;
	}

	public double[] CellValuesAtTime(double absTime)
	{
		if (CellValuesState == null)
		{
			CellValuesState = new double[CellValues.GetLength(1)];
		}
		if (!EqualishTimes(absTime, TimeState))
		{
			SetTime(absTime);
			if (IndexState >= 0)
			{
				for (int i = 0; i < Cells.Length; i++)
				{
					CellValuesState[i] = CellValues[IndexState, i];
				}
			}
			else
			{
				InterpolateElementValues(CellValues, CellValuesState);
			}
		}
		return CellValuesState;
	}

	private void InterpolateElementValues(double[,] elementValues, double[] valuesState)
	{
		int indexStateTerm = ~IndexState;
		double timestep = Timesteps[indexStateTerm - 1];
		double timestep2 = Timesteps[indexStateTerm];
		double submergenceRatio = (TimeState - timestep) / (timestep2 - timestep);
		for (int i = 0; i < valuesState.Length; i++)
		{
			double elementValuesTerm = elementValues[indexStateTerm - 1, i];
			double elementValuesTerm2 = elementValues[indexStateTerm, i];
			valuesState[i] = elementValuesTerm + (elementValuesTerm2 - elementValuesTerm) * submergenceRatio;
		}
	}

	public void SetTime(double absTime)
	{
		if (double.IsNaN(TimeState))
		{
			IndexState = 0;
			TimeState = Timesteps[IndexState];
		}
		if (EqualishTimes(absTime, TimeState))
		{
			return;
		}
		int i = ((IndexState < 0) ? (~IndexState) : IndexState);
		if (absTime > Timesteps[^1])
		{
			IndexState = Timesteps.Length - 1;
		}
		else if (absTime <= Timesteps[0])
		{
			IndexState = 0;
		}
		else
		{
			if (absTime > TimeState)
			{
				for (; i < Timesteps.Length && Timesteps[i] < absTime; i++)
				{
				}
			}
			else
			{
				while (i > 0 && Timesteps[i - 1] >= absTime)
				{
					i--;
				}
			}
			IndexState = (EqualishTimes(absTime, Timesteps[i]) ? i : (~i));
		}
		TimeState = absTime;
	}

	public void SetTimeAndDuration(double startTime, double duration)
	{
		SetTime(startTime);
		DurationState = duration;
	}

	private static bool EqualishTimes(double time1, double time2)
	{
		if (Math.Abs(time1 - time2) < 1E-10)
		{
			return true;
		}
		return false;
	}
}
