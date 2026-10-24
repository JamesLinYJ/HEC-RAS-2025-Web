using System;
using System.Collections.Generic;
using System.Numerics;
using Utility;

namespace Geospatial;

public class JaggedWeights<T> where T : unmanaged, IBinaryFloatingPointIeee754<T>
{
	private const int _loopSize = 1000;

	public int Count => StartInfo.Length;

	public Memory<int> StartInfo { get; set; }

	public Memory<int> CountInfo { get; set; }

	public Memory<int> WeightInfo { get; set; }

	public Memory<T> WeightValues { get; set; }

	public JaggedWeights(Memory<int> startInfo, Memory<int> countInfo, Memory<int> weightInfo, Memory<T> weightValues)
	{
		StartInfo = startInfo;
		CountInfo = countInfo;
		WeightInfo = weightInfo;
		WeightValues = weightValues;
	}

	public static JaggedWeights<T> ConstantField(int count)
	{
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		for (int i = 0; i < count; i++)
		{
			intValues[i] = 0;
			intValuesArray[i] = 1;
		}
		return new JaggedWeights<T>(intValues, intValuesArray, new int[1], new T[1] { T.One });
	}

	public static JaggedWeights<T> Identity(int count)
	{
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		int[] intValues2 = new int[count];
		T[] values = new T[count];
		for (int i = 0; i < count; i++)
		{
			intValues[i] = i;
			intValuesArray[i] = 1;
			intValues2[i] = i;
			values[i] = T.One;
		}
		return new JaggedWeights<T>(intValues, intValuesArray, intValues2, values);
	}

	public void GetWeightsForElement(int elementIndex, out Memory<int> info, out Memory<T> weights)
	{
		Memory<int> startInfo = StartInfo;
		int start = startInfo.Span[elementIndex];
		startInfo = CountInfo;
		int length = startInfo.Span[elementIndex];
		startInfo = WeightInfo;
		info = startInfo.Slice(start, length);
		weights = WeightValues.Slice(start, length);
	}

	public void ScaleElements(IEnumerable<T> elementScales)
	{
		Memory<int> startInfo = StartInfo;
		Span<int> startInfoSpan = startInfo.Span;
		startInfo = StartInfo;
		startInfo.ToArray();
		startInfo = CountInfo;
		Span<int> startInfo2 = startInfo.Span;
		Span<T> weightValues = WeightValues.Span;
		int ordinal = 0;
		foreach (T elementScale in elementScales)
		{
			for (int i = 0; i < startInfo2[ordinal]; i++)
			{
				weightValues[startInfoSpan[ordinal] + i] *= elementScale;
			}
			ordinal++;
		}
	}

	public void ScaleElements(T elementScale)
	{
		Span<T> weightValues = WeightValues.Span;
		for (int i = 0; i < weightValues.Length; i++)
		{
			weightValues[i] *= elementScale;
		}
	}

	public JaggedWeights<T> CopyExpanded()
	{
		List<T> weightValuesList = new List<T>();
		List<int> intList = new List<int>();
		Memory<int> startInfo = StartInfo;
		Span<int> startInfoSpan = startInfo.Span;
		startInfo = CountInfo;
		Span<int> startInfo2 = startInfo.Span;
		Span<T> weightValues = WeightValues.Span;
		startInfo = WeightInfo;
		Span<int> startInfo3 = startInfo.Span;
		startInfo = StartInfo;
		int[] startInfoArray = startInfo.ToArray();
		int startInfoSum = 0;
		for (int i = 0; i < startInfoSpan.Length; i++)
		{
			startInfoArray[i] = startInfoSum;
			for (int j = 0; j < startInfo2[i]; j++)
			{
				intList.Add(startInfo3[startInfoSpan[i] + j]);
				weightValuesList.Add(weightValues[startInfoSpan[i] + j]);
			}
			startInfoSum += startInfo2[i];
		}
		return new JaggedWeights<T>(startInfoArray, CountInfo, intList.ToArray(), weightValuesList.ToArray());
	}

	public bool HasAnyData()
	{
		Span<int> countInfo = CountInfo.Span;
		for (int i = 0; i < countInfo.Length; i++)
		{
			if (countInfo[i] > 0)
			{
				return true;
			}
		}
		return false;
	}

	public bool HasDataForAllElements()
	{
		Span<int> countInfo = CountInfo.Span;
		for (int i = 0; i < countInfo.Length; i++)
		{
			if (countInfo[i] == 0)
			{
				return false;
			}
		}
		return true;
	}

	public void EnsureAllWeightsNormalized()
	{
		throw new NotImplementedException();
	}

	public void ApplyWeights(T[] values, T[] result)
	{
		Parallel.SmartFor(Count, (int start, int stop) =>
		{
			ApplyWeightsStartStop(values, result, start, stop);
		}, 1000);
	}

	private void ApplyWeightsStartStop(T[] values, T[] result, int start, int stop)
	{
		for (int i = start; i <= stop; i++)
		{
			result[i] = ApplyWeightsForElement(values, i);
		}
	}

	public T ApplyWeightsForElement(T[] values, int idx)
	{
		GetWeightsForElement(idx, out var info, out var weights);
		Span<int> infoSpan = info.Span;
		Span<T> weightsSpan = weights.Span;
		T zero = T.Zero;
		for (int i = 0; i < info.Length; i++)
		{
			zero += weightsSpan[i] * values[infoSpan[i]];
		}
		return zero;
	}

	public void ApplyWeights(Vector2[] vector2s, Vector2[] result)
	{
		Parallel.SmartFor(Count, (int start, int stop) =>
		{
			ApplyWeightsStartStop(vector2s, result, start, stop);
		}, 1000);
	}

	private void ApplyWeightsStartStop(Vector2[] vector2s, Vector2[] result, int start, int stop)
	{
		for (int i = start; i <= stop; i++)
		{
			result[i] = ApplyWeightsForElement(vector2s, i);
		}
	}

	public Vector2 ApplyWeightsForElement(Vector2[] vector2s, int idx)
	{
		GetWeightsForElement(idx, out var info, out var weights);
		Span<int> infoSpan = info.Span;
		Span<T> weightsSpan = weights.Span;
		Vector2 zero = Vector2.Zero;
		for (int i = 0; i < info.Length; i++)
		{
			float saturating = float.CreateSaturating(weightsSpan[i]);
			zero += saturating * vector2s[infoSpan[i]];
		}
		return zero;
	}
}
