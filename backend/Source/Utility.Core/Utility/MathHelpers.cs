using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Utility.DataStructures;

namespace Utility;

public static class MathHelpers
{
	[StructLayout(LayoutKind.Explicit)]
	public struct FastSqrtStruct
	{
		[FieldOffset(0)]
		public float FloatValue;

		[FieldOffset(0)]
		public int IntValue;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Sqrt(float floatValue)
		{
			FastSqrtStruct fastSqrtStruct = new FastSqrtStruct
			{
				FloatValue = floatValue
			};
			fastSqrtStruct.IntValue -= 8388608;
			fastSqrtStruct.IntValue >>= 1;
			fastSqrtStruct.IntValue += 536870912;
			return fastSqrtStruct.FloatValue;
		}
	}

	public const float OneLessEpsilon = 0.9999999f;

	public static double InterpolateLogLog(double x0, double y0, double x1, double y1, double x)
	{
		double x0Term = Math.Log(x0);
		double y0Term = Math.Log(y0);
		double submergenceRatio = (Math.Log(x) - x0Term) / (Math.Log(x1) - x0Term);
		return Math.Exp(y0Term + (Math.Log(y1) - y0Term) * submergenceRatio);
	}

	public static int BinarySearch<T>(List<T> collection, T target) where T : IComparable<T>
	{
		return BinarySearch(collection, target, Comparer<T>.Default);
	}

	public static int BinarySearch<T>(IList<T> collection, T target) where T : IComparable<T>
	{
		return BinarySearch(collection, target, Comparer<T>.Default);
	}

	public static int BinarySearch<T>(IList<T> collection, T target, IComparer<T> comparer)
	{
		int medianOffset = 0;
		int countOffset = collection.Count - 1;
		if (comparer == null)
		{
			comparer = Comparer<T>.Default;
		}
		while (medianOffset <= countOffset)
		{
			int median = GetMedian(medianOffset, countOffset);
			int compare = comparer.Compare(collection[median], target);
			if (compare == 0)
			{
				return median;
			}
			if (compare < 0)
			{
				medianOffset = median + 1;
			}
			else
			{
				countOffset = median - 1;
			}
		}
		return ~medianOffset;
	}

	public static int BinarySearch<T>(IReadOnlyList<T> collection, T target) where T : IComparable<T>
	{
		int medianOffset = 0;
		int countOffset = collection.Count - 1;
		while (medianOffset <= countOffset)
		{
			int median = GetMedian(medianOffset, countOffset);
			int collectionMedianTerm = collection[median].CompareTo(target);
			if (collectionMedianTerm == 0)
			{
				return median;
			}
			if (collectionMedianTerm < 0)
			{
				medianOffset = median + 1;
			}
			else
			{
				countOffset = median - 1;
			}
		}
		return ~medianOffset;
	}

	public static int BinarySearch<T>(Span<T> collection, T target) where T : IComparable<T>
	{
		int medianOffset = 0;
		int lengthOffset = collection.Length - 1;
		while (medianOffset <= lengthOffset)
		{
			int median = GetMedian(medianOffset, lengthOffset);
			int collectionMedianTerm = collection[median].CompareTo(target);
			if (collectionMedianTerm == 0)
			{
				return median;
			}
			if (collectionMedianTerm < 0)
			{
				medianOffset = median + 1;
			}
			else
			{
				lengthOffset = median - 1;
			}
		}
		return ~medianOffset;
	}

	public static int BinarySearch<T>(IArray<T> collection, T target) where T : IComparable<T>
	{
		int medianOffset = 0;
		int countOffset = collection.Count - 1;
		while (medianOffset <= countOffset)
		{
			int median = GetMedian(medianOffset, countOffset);
			int collectionMedianTerm = collection[median].CompareTo(target);
			if (collectionMedianTerm == 0)
			{
				return median;
			}
			if (collectionMedianTerm < 0)
			{
				medianOffset = median + 1;
			}
			else
			{
				countOffset = median - 1;
			}
		}
		return ~medianOffset;
	}

	private static int GetMedian(int low, int hi)
	{
		return low + (hi - low >> 1);
	}

	public static double[] LinearInterpolate(double min, double max, int steps)
	{
		if (steps < 1)
		{
			throw new InvalidOperationException("Steps cannot be less than 1");
		}
		double[] doubleValues = new double[steps];
		if (steps == 1)
		{
			doubleValues[0] = min;
			return doubleValues;
		}
		double maxAboveMinOverStepsOffset = (max - min) / (double)(steps - 1);
		for (int i = 0; i < steps; i++)
		{
			doubleValues[i] = min + (double)i * maxAboveMinOverStepsOffset;
		}
		return doubleValues;
	}

	public static int CombineHashCodes(int h1, int h2)
	{
		return ((h1 << 5) + h1) ^ h2;
	}

	public static int CombineHashCodes<T>(params T[] inputs)
	{
		int hashCodeTerm = 5381;
		for (int i = 0; i < inputs.Length; i++)
		{
			T input = inputs[i];
			int hashCode = input.GetHashCode();
			hashCodeTerm = ((hashCodeTerm << 5) + hashCodeTerm) ^ hashCode;
		}
		return hashCodeTerm;
	}

	public static void Sort(IList<float> buffer)
	{
		int[] rent = ArrayPool<int>.Shared.Rent(buffer.Count);
		float[] rentArray = ArrayPool<float>.Shared.Rent(buffer.Count);
		for (int i = 0; i < buffer.Count; i++)
		{
			rent[i] = i;
		}
		QuickSort(buffer, rent, 0, buffer.Count - 1);
		for (int j = 0; j < buffer.Count; j++)
		{
			rentArray[j] = buffer[rent[j]];
		}
		if (buffer is float[] floatValues)
		{
			Buffer.BlockCopy(rentArray, 0, floatValues, 0, floatValues.Length * 4);
		}
		else
		{
			for (int k = 0; k < buffer.Count; k++)
			{
				buffer[k] = rentArray[k];
			}
		}
		ArrayPool<int>.Shared.Return(rent);
		ArrayPool<float>.Shared.Return(rentArray);
	}

	public static void Sort<T>(IList<T> buffer, IComparer<T> comparer)
	{
		int[] rent = ArrayPool<int>.Shared.Rent(buffer.Count);
		T[] rentArray = ArrayPool<T>.Shared.Rent(buffer.Count);
		for (int i = 0; i < buffer.Count; i++)
		{
			rent[i] = i;
		}
		QuickSort(buffer, rent, 0, buffer.Count - 1, comparer);
		for (int j = 0; j < buffer.Count; j++)
		{
			rentArray[j] = buffer[rent[j]];
		}
		for (int k = 0; k < buffer.Count; k++)
		{
			buffer[k] = rentArray[k];
		}
		ArrayPool<int>.Shared.Return(rent);
		ArrayPool<T>.Shared.Return(rentArray);
	}

	public static int[] GetQuickSortMap<T>(IList<T> arr) where T : IComparable<T>
	{
		int[] quickSortMap = new int[arr.Count];
		for (int i = 0; i < arr.Count; i++)
		{
			quickSortMap[i] = i;
		}
		QuickSort(arr, quickSortMap, 0, arr.Count - 1);
		return quickSortMap;
	}

	private static void QuickSort<T>(IList<T> arr, int[] map, int left, int right) where T : IComparable<T>
	{
		QuickSort(arr, map, left, right, Comparer<T>.Default);
	}

	private static void QuickSort<T>(IList<T> arr, int[] map, int left, int right, IComparer<T> comparer)
	{
		do
		{
			int left2 = left;
			int right2 = right;
			int index = map[left2 + (right2 - left2 >> 1)];
			while (true)
			{
				if (left2 < arr.Count && comparer.Compare(arr[index], arr[map[left2]]) > 0)
				{
					left2++;
					continue;
				}
				while (right2 >= 0 && comparer.Compare(arr[map[right2]], arr[index]) > 0)
				{
					right2--;
				}
				if (left2 > right2)
				{
					break;
				}
				if (left2 < right2)
				{
					int map2 = map[left2];
					map[left2] = map[right2];
					map[right2] = map2;
				}
				left2++;
				right2--;
				if (left2 > right2)
				{
					break;
				}
			}
			if (right2 - left <= right - left2)
			{
				if (left < right2)
				{
					QuickSort(arr, map, left, right2, comparer);
				}
				left = left2;
			}
			else
			{
				if (left2 < right)
				{
					QuickSort(arr, map, left2, right, comparer);
				}
				right = right2;
			}
		}
		while (left < right);
	}

	public static bool ParametricFractions(double v0, double v1, double rangeHi, double rangeLo, out double t0, out double t1)
	{
		double v0Term;
		double v1Term;
		bool condition;
		if (v0 <= v1)
		{
			v0Term = v0;
			v1Term = v1;
			condition = true;
		}
		else
		{
			v0Term = v1;
			v1Term = v0;
			condition = false;
		}
		if (v1Term < rangeLo || v0Term > rangeHi)
		{
			t0 = double.NaN;
			t1 = double.NaN;
			return false;
		}
		if (v0Term >= rangeLo && v1Term <= rangeHi)
		{
			t0 = 0.0;
			t1 = 1.0;
			return true;
		}
		double computed = v1Term - v0Term;
		if (condition)
		{
			if (rangeLo <= v0Term)
			{
				t0 = 0.0;
			}
			else
			{
				t0 = (rangeLo - v0Term) / computed;
			}
			if (rangeHi >= v1Term)
			{
				t1 = 1.0;
			}
			else
			{
				t1 = (rangeHi - v0Term) / computed;
			}
		}
		else
		{
			if (rangeHi >= v1Term)
			{
				t0 = 0.0;
			}
			else
			{
				t0 = (v1Term - rangeHi) / computed;
			}
			if (rangeLo <= v0Term)
			{
				t1 = 1.0;
			}
			else
			{
				t1 = (v1Term - rangeLo) / computed;
			}
		}
		return true;
	}

	public static T Max3<T>(T d1, T d2, T d3) where T : INumber<T>
	{
		if (d1 > d2)
		{
			if (d1 > d3)
			{
				return d1;
			}
			return d3;
		}
		if (d2 > d3)
		{
			return d2;
		}
		return d3;
	}

	public static T Min3<T>(T d1, T d2, T d3) where T : INumber<T>
	{
		if (d1 < d2)
		{
			if (d1 < d3)
			{
				return d1;
			}
			return d3;
		}
		if (d2 < d3)
		{
			return d2;
		}
		return d3;
	}

	public static T Mid3<T>(T d1, T d2, T d3) where T : INumber<T>
	{
		if (d1 <= d2 && d2 <= d3)
		{
			return d2;
		}
		if (d1 <= d3 && d3 <= d2)
		{
			return d3;
		}
		if (d2 <= d1 && d1 <= d3)
		{
			return d1;
		}
		if (d2 <= d3 && d3 <= d1)
		{
			return d3;
		}
		if (d3 <= d1 && d1 <= d2)
		{
			return d1;
		}
		return d2;
	}

	public static void MaxMin3<T>(T d0, T d1, T d2, out T max, out T min) where T : INumber<T>
	{
		if (d0 < d1)
		{
			if (d0 < d2)
			{
				min = d0;
			}
			else
			{
				min = d2;
			}
			if (d1 > d2)
			{
				max = d1;
			}
			else
			{
				max = d2;
			}
		}
		else
		{
			if (d1 < d2)
			{
				min = d1;
			}
			else
			{
				min = d2;
			}
			if (d0 > d2)
			{
				max = d0;
			}
			else
			{
				max = d2;
			}
		}
	}

	public static void Sort3<T>(ref T a, ref T b, ref T c) where T : struct, INumber<T>
	{
		if (!(a <= b) || !(b <= c))
		{
			if (a <= c && c <= b)
			{
				Swap(ref b, ref c);
			}
			else if (b <= a && a <= c)
			{
				Swap(ref a, ref b);
			}
			else if (b <= c && c <= a)
			{
				Swap(ref a, ref b);
				Swap(ref b, ref c);
			}
			else if (c <= a && a <= b)
			{
				Swap(ref a, ref b);
				Swap(ref a, ref c);
			}
			else
			{
				Swap(ref a, ref c);
			}
		}
	}

	public static int Min3Index<T>(T d0, T d1, T d2) where T : INumber<T>
	{
		if (d0 < d1)
		{
			if (d0 < d2)
			{
				return 0;
			}
			return 2;
		}
		if (d1 < d2)
		{
			return 1;
		}
		return 2;
	}

	public static void Min3Index<T>(T d0, T d1, T d2, out int idx, out T d0Term) where T : INumber<T>
	{
		if (d0 < d1)
		{
			if (d0 < d2)
			{
				idx = 0;
				d0Term = d0;
			}
			else
			{
				idx = 2;
				d0Term = d2;
			}
		}
		else if (d1 < d2)
		{
			idx = 1;
			d0Term = d1;
		}
		else
		{
			idx = 2;
			d0Term = d2;
		}
	}

	public static double RoundToSignificantFigures(float f, int figures)
	{
		if (f == 0f || float.IsNaN(f))
		{
			return f;
		}
		double computed = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Abs(f))) + 1.0);
		return (float)(computed * Math.Round((double)f / computed, figures));
	}

	public static double RoundToSignificantFigures(double d, int figures)
	{
		if (d == 0.0 || double.IsNaN(d))
		{
			return d;
		}
		double computed = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Abs(d))) + 1.0);
		return computed * Math.Round(d / computed, figures);
	}

	public static float RoundToSignificantBits(float f, int bitsOfPrecision)
	{
		if (f == 0f || float.IsNaN(f))
		{
			return f;
		}
		double y = Math.Truncate(Math.Log(Math.Abs(f), 2.0));
		float computed = (float)Math.Pow(2.0, y);
		float computed2 = f / computed;
		int bitsOfPrecisionPower = (int)Math.Pow(2.0, bitsOfPrecision);
		return (float)(Math.Round(computed2 * (float)bitsOfPrecisionPower) / (double)bitsOfPrecisionPower) * computed;
	}

	public static uint NextLargerPowerOf2(uint x)
	{
		x--;
		x |= x >> 1;
		x |= x >> 2;
		x |= x >> 4;
		x |= x >> 8;
		x |= x >> 16;
		return x + 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Swap<T>(ref T n1, ref T n2) where T : struct
	{
		T n12 = n1;
		n1 = n2;
		n2 = n12;
	}

	public static void SwapIfLarger<T>(ref T smaller, ref T larger) where T : INumber<T>
	{
		if (smaller > larger)
		{
			T smaller2 = smaller;
			smaller = larger;
			larger = smaller2;
		}
	}

	public static double AddAngles(double angle1, double angle2)
	{
		return Math.IEEERemainder(angle1 + angle2, Math.PI * 2.0);
	}

	public static double GaussianQuadraturePoint(int n1to5, int index)
	{
		switch (n1to5)
		{
		case 1:
			if (index == 0)
			{
				return 0.5;
			}
			break;
		case 2:
			switch (index)
			{
			case 0:
				return 0.2113248654;
			case 1:
				return 0.7886751346;
			}
			break;
		case 3:
			switch (index)
			{
			case 0:
				return 0.1127016654;
			case 1:
				return 0.5;
			case 2:
				return 0.8872983346;
			}
			break;
		case 4:
			switch (index)
			{
			case 0:
				return 0.0694318442;
			case 1:
				return 0.3300094783;
			case 2:
				return 0.6699905218;
			case 3:
				return 0.9305681558;
			}
			break;
		case 5:
			switch (index)
			{
			case 0:
				return 0.0469100771;
			case 1:
				return 0.230765345;
			case 2:
				return 0.5;
			case 3:
				return 0.7692346551;
			case 4:
				return 0.953089923;
			}
			break;
		}
		throw new Exception("GaussianQuadraturePoint error n1to5 or index parameter not handled");
	}

	public static double GaussianQuadratureWeight(int n1to5, int index)
	{
		switch (n1to5)
		{
		case 1:
			if (index == 0)
			{
				return 1.0;
			}
			break;
		case 2:
			return 0.5;
		case 3:
			switch (index)
			{
			case 0:
				return 0.2777777778;
			case 1:
				return 0.4444444445;
			case 2:
				return 0.2777777778;
			}
			break;
		case 4:
			switch (index)
			{
			case 0:
				return 0.1739274226;
			case 1:
				return 0.3260725774;
			case 2:
				return 0.3260725774;
			case 3:
				return 0.1739274226;
			}
			break;
		case 5:
			switch (index)
			{
			case 0:
				return 0.1184634425;
			case 1:
				return 0.2393143353;
			case 2:
				return 0.2844444444;
			case 3:
				return 0.2393143353;
			case 4:
				return 0.1184634425;
			}
			break;
		}
		throw new Exception("GaussianQuadratureWeight error n1to5 or index parameter not handled");
	}

	public static void Solve2x2<T>(T a00, T a01, T a10, T a11, T b0, T b1, out T x0, out T x1) where T : IBinaryFloatingPointIeee754<T>
	{
		T a00A11Term = a00 * a11 - a01 * a10;
		x0 = (b0 * a11 - a01 * b1) / a00A11Term;
		x1 = (a00 * b1 - b0 * a10) / a00A11Term;
	}

	public static void Solve2x2<T>(T[,] A, T[] b, T[] x) where T : IBinaryFloatingPointIeee754<T>
	{
		T localValue = A[0, 0] * A[1, 1] - A[0, 1] * A[1, 0];
		x[0] = (b[0] * A[1, 1] - A[0, 1] * b[1]) / localValue;
		x[1] = (A[0, 0] * b[1] - b[0] * A[1, 0]) / localValue;
	}

	public static void Solve3x3<T>(T[,] A, T[] b, T[] x) where T : IBinaryFloatingPointIeee754<T>
	{
		T localValue = A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1];
		T localValue2 = A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0];
		T localValue3 = A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0];
		T localValue4 = A[0, 0] * localValue - A[0, 1] * localValue2 + A[0, 2] * localValue3;
		T oneTerm = T.One / localValue4;
		T localValue5 = b[1] * A[2, 2] - A[1, 2] * b[2];
		T localValue6 = A[1, 0] * b[2] - b[1] * A[2, 0];
		localValue4 = b[0] * localValue - A[0, 1] * localValue5 + A[0, 2] * (b[1] * A[2, 1] - A[1, 1] * b[2]);
		x[0] = localValue4 * oneTerm;
		localValue4 = A[0, 0] * localValue5 - b[0] * localValue2 + A[0, 2] * localValue6;
		x[1] = localValue4 * oneTerm;
		localValue4 = A[0, 0] * (A[1, 1] * b[2] - b[1] * A[2, 1]) - A[0, 1] * localValue6 + b[0] * localValue3;
		x[2] = localValue4 * oneTerm;
	}

	public static bool SolveTridiagonal(float[] a, float[] b, float[] c, float[] r, ref float[] u, int n = -1)
	{
		if (a == null || b == null || c == null || r == null)
		{
			return false;
		}
		if (n == -1)
		{
			n = a.Length;
		}
		if (n == 0 || b.Length < n || c.Length < n || r.Length < n)
		{
			return false;
		}
		if ((double)b[0] == 0.0)
		{
			return false;
		}
		if (u == null || u.Length < n)
		{
			u = new float[n];
		}
		float[] floatValues = new float[n];
		float computed = b[0];
		u[0] = r[0] / computed;
		for (int i = 1; i < n; i++)
		{
			floatValues[i] = c[i - 1] / computed;
			computed = b[i] - a[i] * floatValues[i];
			if ((double)computed == 0.0)
			{
				return false;
			}
			u[i] = (r[i] - a[i] * u[i - 1]) / computed;
		}
		for (int j = n - 2; j >= 0; j += -1)
		{
			u[j] -= floatValues[j + 1] * u[j + 1];
		}
		return true;
	}

	public static bool SolveTridiagonal(double[] a, double[] b, double[] c, double[] r, ref double[] u, int n = -1)
	{
		if (a == null || b == null || c == null || r == null)
		{
			return false;
		}
		if (n == -1)
		{
			n = a.Length;
		}
		if (n == 0 || b.Length < n || c.Length < n || r.Length < n)
		{
			return false;
		}
		if (b[0] == 0.0)
		{
			return false;
		}
		if (u == null || u.Length < n)
		{
			u = new double[n];
		}
		double[] doubleValues = new double[n];
		double computed = b[0];
		u[0] = r[0] / computed;
		for (int i = 1; i < n; i++)
		{
			doubleValues[i] = c[i - 1] / computed;
			computed = b[i] - a[i] * doubleValues[i];
			if (computed == 0.0)
			{
				return false;
			}
			u[i] = (r[i] - a[i] * u[i - 1]) / computed;
		}
		for (int j = n - 2; j >= 0; j += -1)
		{
			u[j] -= doubleValues[j + 1] * u[j + 1];
		}
		return true;
	}

	public static void Quadratic(double a, double b, double c, out double x1, out double x2)
	{
		double computed = Math.Sqrt(b * b - 4.0 * a * c);
		x1 = (0.0 - b + computed) / (2.0 * a);
		x2 = (0.0 - b - computed) / (2.0 * a);
	}

	public static void GaussJordan(double[,] a, double[,] b, bool makeInverse = false)
	{
		GaussJordan(a.AsSpan2D(), b.AsSpan2D(), makeInverse);
	}

	public static void GaussJordan(Span2D<double> a, Span2D<double> b, bool makeInverse = false)
	{
		int height = a.Height;
		if (height > 32)
		{
			Memory<int> intSpan = new int[height + 1];
			Memory<int> intSpan2 = new int[height];
			Execute(((Memory<int>)new int[height]).Span, intSpan2.Span, intSpan.Span, a, b, makeInverse);
		}
		else
		{
			Span<int> indxc = stackalloc int[height + 1];
			Span<int> indxr = stackalloc int[height];
			Execute(stackalloc int[height], indxr, indxc, a, b, makeInverse);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void Execute(Span<int> ipiv, Span<int> intSpan3, Span<int> intSpan4, Span2D<double> span2D, Span2D<double> span2D2, bool condition)
		{
			int height2 = span2D.Height;
			int width = span2D2.Width;
			int count = height2 - 1;
			int count2 = width - 1;
			for (int i = 0; i <= count; i++)
			{
				ipiv[i] = -1;
			}
			int intervalIndex = 0;
			int intervalIndex2 = 0;
			for (int j = 0; j <= count; j++)
			{
				double span2DTerm = 0.0;
				for (int k = 0; k <= count; k++)
				{
					if (ipiv[k] != 0)
					{
						for (int l = 0; l <= count; l++)
						{
							if (ipiv[l] == -1)
							{
								if (Math.Abs(span2D[k, l]) > span2DTerm)
								{
									span2DTerm = Math.Abs(span2D[k, l]);
									intervalIndex = k;
									intervalIndex2 = l;
								}
							}
							else if (ipiv[l] > 0)
							{
								throw new Exception("Matrix is singular");
							}
						}
					}
				}
				ipiv[intervalIndex2]++;
				if (intervalIndex != intervalIndex2)
				{
					for (int m = 0; m <= count; m++)
					{
						double span2D3 = span2D[intervalIndex, m];
						span2D[intervalIndex, m] = span2D[intervalIndex2, m];
						span2D[intervalIndex2, m] = span2D3;
					}
					for (int n = 0; n <= count2; n++)
					{
						double span2D22 = span2D2[intervalIndex, n];
						span2D2[intervalIndex, n] = span2D2[intervalIndex2, n];
						span2D2[intervalIndex2, n] = span2D22;
					}
				}
				intSpan3[j] = intervalIndex;
				intSpan4[j] = intervalIndex2;
				if (span2D[intervalIndex2, intervalIndex2] == 0.0)
				{
					throw new Exception("Matrix is singular");
				}
				double span2DTerm2 = 1.0 / span2D[intervalIndex2, intervalIndex2];
				span2D[intervalIndex2, intervalIndex2] = 1.0;
				for (int intervalIndex3 = 0; intervalIndex3 <= count; intervalIndex3++)
				{
					span2D[intervalIndex2, intervalIndex3] *= span2DTerm2;
				}
				for (int intervalIndex4 = 0; intervalIndex4 <= count2; intervalIndex4++)
				{
					span2D2[intervalIndex2, intervalIndex4] *= span2DTerm2;
				}
				for (int intervalIndex5 = 0; intervalIndex5 <= count; intervalIndex5++)
				{
					if (intervalIndex5 != intervalIndex2)
					{
						double span2DTerm3 = span2D[intervalIndex5, intervalIndex2];
						span2D[intervalIndex5, intervalIndex2] = 0.0;
						for (int intervalIndex6 = 0; intervalIndex6 <= count; intervalIndex6++)
						{
							span2D[intervalIndex5, intervalIndex6] -= span2D[intervalIndex2, intervalIndex6] * span2DTerm3;
						}
						for (int intervalIndex7 = 0; intervalIndex7 <= width - 1; intervalIndex7++)
						{
							span2D2[intervalIndex5, intervalIndex7] -= span2D2[intervalIndex2, intervalIndex7] * span2DTerm3;
						}
					}
				}
			}
			if (condition)
			{
				for (int count3 = count; count3 >= 0; count3 += -1)
				{
					if (intSpan3[count3] != intSpan4[count3])
					{
						for (int intervalIndex8 = 0; intervalIndex8 <= count; intervalIndex8++)
						{
							double span2D4 = span2D[intervalIndex8, intSpan3[count3]];
							span2D[intervalIndex8, intSpan3[count3]] = span2D[intervalIndex8, intSpan4[count3]];
							span2D[intervalIndex8, intSpan4[count3]] = span2D4;
						}
					}
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float FastSqrt(float floatValue)
	{
		FastSqrtStruct fastSqrtStruct = new FastSqrtStruct
		{
			FloatValue = floatValue
		};
		fastSqrtStruct.IntValue -= 8388608;
		fastSqrtStruct.IntValue >>= 1;
		fastSqrtStruct.IntValue += 536870912;
		return fastSqrtStruct.FloatValue;
	}

	public static string DoubleToSigFigString(double doubleValue, int sigFigs)
	{
		if (doubleValue == 0.0)
		{
			return "0";
		}
		if (double.IsNaN(doubleValue) || double.MaxValue == doubleValue || sigFigs <= 0)
		{
			return "";
		}
		double computed = Math.Abs(doubleValue);
		int ordinal = ((computed >= 1.0) ? ((int)Math.Floor(Math.Log10(computed)) + 1) : 0);
		int sigFigsTerm = sigFigs - ordinal;
		if (computed >= 1.0)
		{
			if (sigFigsTerm > 0)
			{
				return doubleValue.ToString($"N{sigFigsTerm}");
			}
			return doubleValue.ToString("N0");
		}
		sigFigsTerm = sigFigs - (int)Math.Floor(Math.Log10(computed)) - 1;
		return doubleValue.ToString($"F{sigFigsTerm}");
	}

	public static int GCD(int a, int b)
	{
		a = Math.Abs(a);
		b = Math.Abs(b);
		while (b != 0)
		{
			int ordinal = b;
			b = a % b;
			a = ordinal;
		}
		return a;
	}

	public static int LCM(int a, int b)
	{
		if (a == 0 || b == 0)
		{
			return 0;
		}
		return Math.Abs(a * b) / GCD(a, b);
	}

	public static int LCM(IEnumerable<int> numbers)
	{
		List<int> numbersList = numbers.ToList();
		if (numbersList.Count == 0)
		{
			throw new ArgumentException("Must provide at least one number.", "numbers");
		}
		int numbersList2 = numbersList[0];
		for (int i = 1; i < numbersList.Count; i++)
		{
			numbersList2 = LCM(numbersList2, numbersList[i]);
		}
		return numbersList2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool DoubleEquals(double a, double b)
	{
		if (double.IsNaN(a) && double.IsNaN(b))
		{
			return true;
		}
		return a == b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool FloatEquals(float a, float b)
	{
		if (float.IsNaN(a) && float.IsNaN(b))
		{
			return true;
		}
		return a == b;
	}

	public static bool SpanDoubleEquals(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
	{
		if (a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (!DoubleEquals(a[i], b[i]))
			{
				return false;
			}
		}
		return true;
	}

	public static bool SpanFloatEquals(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
	{
		if (a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (!FloatEquals(a[i], b[i]))
			{
				return false;
			}
		}
		return true;
	}

	public static double ParseFirstNumber(Span<byte> ascii)
	{
		if (ascii.Length == 0)
		{
			return double.NaN;
		}
		double sign = 1.0;
		double computed = 0.0;
		int i;
		for (i = 0; i < ascii.Length; i++)
		{
			byte b = ascii[i];
			switch (b)
			{
			case 45:
				sign = -1.0;
				continue;
			case 48:
			case 49:
			case 50:
			case 51:
			case 52:
			case 53:
			case 54:
			case 55:
			case 56:
			case 57:
			{
				int ordinal = b - 48;
				computed *= 10.0;
				computed += (double)ordinal;
				continue;
			}
			default:
				if (b == 46 || b == 101 || b == 69)
				{
					if (!(computed > 0.0))
					{
						continue;
					}
					break;
				}
				if (computed == 0.0)
				{
					sign = 1.0;
					continue;
				}
				return sign * computed;
			}
			break;
		}
		if (i >= ascii.Length - 1)
		{
			if (computed == 0.0)
			{
				return double.NaN;
			}
			return computed;
		}
		throw new NotImplementedException();
	}
}
