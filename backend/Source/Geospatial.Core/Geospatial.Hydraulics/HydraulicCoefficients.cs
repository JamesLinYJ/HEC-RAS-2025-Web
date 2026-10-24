using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Geospatial.Hydraulics;

internal static class HydraulicCoefficients
{
	private static readonly int VectorWidth = Vector<float>.Count;

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static float ComputeChikwenduDispersion(float[] cellArea, float[] cellWettedWidth, float[] cellDepth, float[] cellDrag, float[] cellVelocity, float longitudinalMixingCoefficient, float lateralMixingCoefficient, int firstWetCell, int lastWetCell)
	{
		if (lastWetCell < firstWetCell)
		{
			return 0f;
		}
		float cellAreaSum = 0f;
		float cellArea2TimesCellVelocity2Sum = 0f;
		float cellWettedWidthSum = 0f;
		float computed = 0f;
		for (int i = firstWetCell; i <= lastWetCell; i++)
		{
			float cellArea2 = cellArea[i];
			if (!(cellArea2 <= 0f))
			{
				float cellVelocity2 = cellVelocity[i];
				float cellDragCellVelocity2Term = MathF.Sqrt(cellDrag[i]) * cellVelocity2;
				float longitudinalMixingCoefficientCellDepthTerm = longitudinalMixingCoefficient * cellDepth[i] * cellDragCellVelocity2Term;
				cellAreaSum += cellArea2;
				cellArea2TimesCellVelocity2Sum += cellArea2 * cellVelocity2;
				cellWettedWidthSum += cellWettedWidth[i];
				computed += cellArea2 * longitudinalMixingCoefficientCellDepthTerm;
			}
		}
		if (cellAreaSum < 1E-20f)
		{
			return 0f;
		}
		float cellWettedWidthSumTerm = ((cellWettedWidthSum > 0f) ? (2f / (cellWettedWidthSum * cellWettedWidthSum)) : 0f);
		float cellArea2TimesCellVelocity2SumOverCellAreaSum = cellArea2TimesCellVelocity2Sum / cellAreaSum;
		float computed2 = 0f;
		float cellAreaOverCellAreaSum = 0f;
		float computed3 = 0f;
		int ordinal = -1;
		float computed4 = 0f;
		for (int j = firstWetCell; j <= lastWetCell; j++)
		{
			float cellArea3 = cellArea[j];
			if (!(cellArea3 <= 0f))
			{
				float cellVelocity3 = cellVelocity[j];
				float cellDragCellVelocity3Term = MathF.Sqrt(cellDrag[j]) * cellVelocity3;
				float lateralMixingCoefficientCellDepthTerm = lateralMixingCoefficient * cellDepth[j] * cellDragCellVelocity3Term;
				if (ordinal >= 0)
				{
					float cellAreaOverCellAreaSum2 = cellArea[ordinal] / cellAreaSum;
					cellAreaOverCellAreaSum += cellAreaOverCellAreaSum2;
					float cellVelocityTerm = cellAreaOverCellAreaSum2 * cellVelocity[ordinal];
					computed3 += cellVelocityTerm;
					cellArea2TimesCellVelocity2SumOverCellAreaSum -= cellVelocityTerm;
					float computed5 = computed3 / cellAreaOverCellAreaSum;
					float computed6 = 1f - cellAreaOverCellAreaSum;
					float computed7 = ((computed6 > 1E-20f) ? (cellArea2TimesCellVelocity2SumOverCellAreaSum / computed6) : 0f);
					float epsilonTerm = MathF.Sqrt(computed4 * lateralMixingCoefficientCellDepthTerm + float.Epsilon);
					float cellArea3CellAreaSumTerm = cellWettedWidthSumTerm * epsilonTerm / (cellAreaOverCellAreaSum2 + cellArea3 / cellAreaSum);
					float computed8 = computed5 - computed7;
					computed2 += cellAreaOverCellAreaSum * cellAreaOverCellAreaSum * computed6 * computed6 * computed8 * computed8 / cellArea3CellAreaSumTerm;
				}
				ordinal = j;
				computed4 = lateralMixingCoefficientCellDepthTerm;
			}
		}
		float cellAreaSumTerm = computed / cellAreaSum;
		return computed2 + cellAreaSumTerm;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static float VelocityWeightingCoefficient(float[] cellArea, float[] cellVelocity, float flow, int firstWetCell, int lastWetCell)
	{
		if (lastWetCell < firstWetCell)
		{
			return 1f;
		}
		int vectorWidth = VectorWidth;
		Vector<float> zero = Vector<float>.Zero;
		Vector<float> zero2 = Vector<float>.Zero;
		int i = firstWetCell;
		int count;
		for (count = lastWetCell + 1; i + vectorWidth <= count; i += vectorWidth)
		{
			Vector<float> vector = new Vector<float>(cellArea, i);
			Vector<float> vector2 = new Vector<float>(cellVelocity, i);
			zero += vector;
			zero2 += vector2 * vector2 * vector2 * vector;
		}
		float computed = 0f;
		float computed2 = 0f;
		for (int j = 0; j < vectorWidth; j++)
		{
			computed += zero[j];
			computed2 += zero2[j];
		}
		for (; i < count; i++)
		{
			computed += cellArea[i];
			computed2 += cellVelocity[i] * cellVelocity[i] * cellVelocity[i] * cellArea[i];
		}
		if (MathF.Abs(flow) < 1E-20f)
		{
			return 1f;
		}
		return MathF.Max(computed * computed / (flow * flow * flow) * computed2, 1f);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static float MomentumCoefficient(float[] cellArea, float[] cellVelocity, float flow, int firstWetCell, int lastWetCell)
	{
		if (lastWetCell < firstWetCell)
		{
			return 1f;
		}
		int vectorWidth = VectorWidth;
		Vector<float> zero = Vector<float>.Zero;
		Vector<float> zero2 = Vector<float>.Zero;
		int i = firstWetCell;
		int count;
		for (count = lastWetCell + 1; i + vectorWidth <= count; i += vectorWidth)
		{
			Vector<float> vector = new Vector<float>(cellArea, i);
			Vector<float> vector2 = new Vector<float>(cellVelocity, i);
			zero += vector;
			zero2 += vector2 * vector2 * vector;
		}
		float computed = 0f;
		float computed2 = 0f;
		for (int j = 0; j < vectorWidth; j++)
		{
			computed += zero[j];
			computed2 += zero2[j];
		}
		for (; i < count; i++)
		{
			computed += cellArea[i];
			computed2 += cellVelocity[i] * cellVelocity[i] * cellArea[i];
		}
		if (MathF.Abs(flow) < 1E-20f)
		{
			return 1f;
		}
		return MathF.Max(computed / (flow * flow) * computed2, 1f);
	}
}
