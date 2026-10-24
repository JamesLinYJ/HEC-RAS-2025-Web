using System;
using Utility.SimdOps;

namespace Geospatial.Hydraulics;

internal static class HydraulicCurveCompactor
{
	internal static bool[] BuildKeepMask(float[] wse, int solvedWseCount, float[][] curves, float maximumBedElevation, float curvatureTolerance, float interpolationTolerance, bool[] keepMask)
	{
		ValidateTolerance(curvatureTolerance, "curvatureTolerance");
		ValidateTolerance(interpolationTolerance, "interpolationTolerance");
		if (keepMask == null || keepMask.Length < solvedWseCount)
		{
			keepMask = new bool[solvedWseCount];
		}
		if (solvedWseCount <= 2)
		{
			for (int i = 0; i < solvedWseCount; i++)
			{
				keepMask[i] = true;
			}
			return keepMask;
		}
		keepMask[0] = true;
		keepMask[solvedWseCount - 1] = true;
		for (int j = 0; j < solvedWseCount; j++)
		{
			if (!(wse[j] < maximumBedElevation))
			{
				keepMask[j] = true;
				break;
			}
		}
		Span<float> floatSpan = stackalloc float[curves.Length];
		for (int k = 0; k < curves.Length; k++)
		{
			float range = SimdOps.Range(curves[k].AsSpan(0, solvedWseCount));
			floatSpan[k] = ((range > 0f) ? (1f / range) : 0f);
			DetectCurvatureBreakpoints(wse.AsSpan(0, solvedWseCount), curves[k].AsSpan(0, solvedWseCount), curvatureTolerance, floatSpan[k], keepMask);
		}
		Simplify(wse.AsSpan(0, solvedWseCount), curves, floatSpan, interpolationTolerance, keepMask);
		return keepMask;
	}

	internal static float[] SelectWse(float[] wse, int solvedWseCount, bool[] keepMask)
	{
		int count = 0;
		for (int i = 0; i < solvedWseCount; i++)
		{
			if (keepMask[i])
			{
				count++;
			}
		}
		float[] floatValues = new float[count];
		int ordinal = 0;
		for (int j = 0; j < solvedWseCount; j++)
		{
			if (keepMask[j])
			{
				floatValues[ordinal++] = wse[j];
			}
		}
		return floatValues;
	}

	private static void DetectCurvatureBreakpoints(ReadOnlySpan<float> wse, ReadOnlySpan<float> values, float tolerance, float inverseRange, Span<bool> keepMask)
	{
		if (inverseRange == 0f)
		{
			return;
		}
		for (int i = 1; i < values.Length - 1; i++)
		{
			float wseStep = wse[i + 1] - wse[i - 1];
			float wseStepOverWseStep = (wse[i] - wse[i - 1]) / wseStep;
			float interpolatedValue = values[i - 1] + wseStepOverWseStep * (values[i + 1] - values[i - 1]);
			if (MathF.Abs(values[i] - interpolatedValue) * inverseRange > tolerance)
			{
				keepMask[i] = true;
			}
		}
	}

	private static void Simplify(ReadOnlySpan<float> wse, float[][] curves, ReadOnlySpan<float> inverseRanges, float tolerance, Span<bool> keepMask)
	{
		int ordinal = 0;
		while (ordinal < wse.Length - 1)
		{
			int ordinal2 = ordinal + 1;
			for (int i = ordinal + 1; i < wse.Length && (i <= ordinal + 1 || !ExceedsInterpolationTolerance(wse, curves, inverseRanges, ordinal, i, tolerance)); i++)
			{
				ordinal2 = i;
				if (keepMask[i])
				{
					break;
				}
			}
			keepMask[ordinal2] = true;
			ordinal = ordinal2;
		}
	}

	private static bool ExceedsInterpolationTolerance(ReadOnlySpan<float> wse, float[][] curves, ReadOnlySpan<float> inverseRanges, int leftIndex, int rightIndex, float tolerance)
	{
		float wseRightIndexTerm = 1f / (wse[rightIndex] - wse[leftIndex]);
		for (int i = leftIndex + 1; i < rightIndex; i++)
		{
			float wseLeftIndexTerm = (wse[i] - wse[leftIndex]) * wseRightIndexTerm;
			for (int j = 0; j < curves.Length; j++)
			{
				float inverseRange = inverseRanges[j];
				if (inverseRange != 0f)
				{
					float curve = curves[j][leftIndex];
					float curveCurvesTerm = curve + wseLeftIndexTerm * (curves[j][rightIndex] - curve);
					if (MathF.Abs(curves[j][i] - curveCurvesTerm) * inverseRange > tolerance)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static void ValidateTolerance(float computed, string parameterName)
	{
		if (!float.IsFinite(computed) || computed < 0f)
		{
			throw new ArgumentOutOfRangeException(parameterName, "Tolerance must be finite and nonnegative.");
		}
	}
}
