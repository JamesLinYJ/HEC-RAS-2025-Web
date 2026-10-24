using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Utility.Unsafe;

namespace Geospatial.PairedData;

public class ProfileBuilder
{
	public List<double> X;

	public List<double> Y;

	public int PointCount => X.Count;

	public ProfileBuilder()
	{
		X = new List<double>();
		Y = new List<double>();
	}

	public ProfileBuilder(int capacity)
	{
		X = new List<double>(capacity);
		Y = new List<double>(capacity);
	}

	public void Add(Point pt)
	{
		X.Add(pt.X);
		Y.Add(pt.Y);
	}

	public void Add(double x, double y)
	{
		X.Add(x);
		Y.Add(y);
	}

	public Profile Build()
	{
		return Profile.FromBuffers(X.ToArray(), Y.ToArray());
	}

	public Profile Filter(double tolerance)
	{
		return Filter(tolerance, 0, PointCount - 1);
	}

	public Profile Filter(double tolerance, int first, int last)
	{
		if (FilterInternal(tolerance, first, last, out var newX, out var newY))
		{
			return Profile.FromBuffers(newX, newY);
		}
		return Build();
	}

	public void FilterSelf(double tolerance)
	{
		int first = 0;
		int last = PointCount - 1;
		if (FilterInternal(tolerance, first, last, out var newX, out var newY))
		{
			UnsafeHelpers.SetListBackingBuffer(X, newX);
			UnsafeHelpers.SetListBackingBuffer(Y, newY);
		}
	}

	private bool FilterInternal(double tolerance, int first, int last, out double[] newX, out double[] newY)
	{
		if (double.IsNaN(tolerance))
		{
			throw new ArgumentException("tolerance");
		}
		if (last - first <= 1)
		{
			newX = null;
			newY = null;
			return false;
		}
		List<int> intList = new List<int>();
		for (int i = 0; i <= first; i++)
		{
			intList.Add(i);
		}
		intList.Add(last);
		double computed = X[first];
		double computed2 = Y[first];
		for (int j = last; j >= first; j += -1)
		{
			double computed3 = X[j];
			double computed4 = Y[j];
			if (computed3 != computed || computed4 != computed2)
			{
				last = j;
				break;
			}
		}
		FilterDouglasPeucker(first, last, tolerance, intList);
		for (int k = last + 1; k < PointCount; k++)
		{
			intList.Add(k);
		}
		if (intList.Count < PointCount)
		{
			CompressList(intList, out newX, out newY);
			return true;
		}
		newX = null;
		newY = null;
		return false;
	}

	private void FilterDouglasPeucker(int first, int last, double tolerance, List<int> keepIndex)
	{
		if (last - first <= 1)
		{
			return;
		}
		double computed = 0.0;
		int ordinal = 0;
		double computed2 = X[last] - X[first];
		double computed3 = Y[last] - Y[first];
		for (int i = first + 1; i < last; i++)
		{
			double computed4 = Math.Abs(computed2 * (Y[i] - Y[first]) - computed3 * (X[i] - X[first]));
			if (computed4 > computed)
			{
				computed = computed4;
				ordinal = i;
			}
		}
		double computed5 = Math.Sqrt(Math.Pow(computed2, 2.0) + Math.Pow(computed3, 2.0));
		if (computed / computed5 > tolerance)
		{
			keepIndex.Add(ordinal);
			FilterDouglasPeucker(first, ordinal, tolerance, keepIndex);
			FilterDouglasPeucker(ordinal, last, tolerance, keepIndex);
		}
	}

	private void CompressList(List<int> keepIndex, out double[] newX, out double[] newY, bool keepListIsSorted = false)
	{
		if (!keepListIsSorted)
		{
			keepIndex.Sort();
		}
		newX = new double[keepIndex.Count];
		newY = new double[keepIndex.Count];
		for (int i = 0; i < keepIndex.Count; i++)
		{
			newX[i] = X[keepIndex[i]];
			newY[i] = Y[keepIndex[i]];
		}
	}

	public void Clear()
	{
		X.Clear();
		Y.Clear();
	}
}
