using System;
using System.Drawing;
using System.Linq;
using Microsoft.VisualBasic;

namespace RasMapperLib.Utilities;

public abstract class Math
{
	public const float OneLessEpsilon = 0.9999999f;

	public static int ProgressMod(int count)
	{
		return checked((int)System.Math.Round(System.Math.Max(1.0, (double)count / 100.0)));
	}

	public static float ConvertRange(float originalStart, float originalEnd, float newStart, float newEnd, float floatValue)
	{
		float newEndNewStartTerm = (newEnd - newStart) / (originalEnd - originalStart);
		return newStart + (floatValue - originalStart) * newEndNewStartTerm;
	}

	public static void SmartRotatePoints(float angleRotation, System.Drawing.PointF rotationAnchor, System.Drawing.PointF[] ptsToRotate)
	{
		throw new NotImplementedException();
	}

	public static T[] ArrayRangeSubset<T>(T[] arr, int startIndex, int length)
	{
		T[] values = new T[checked(length - 1 + 1)];
		Array.Copy(arr, startIndex, values, 0, length);
		return values;
	}

	public static double GetMinValue(double v0, double v1, bool AllowMaxMinValues)
	{
		if (!AllowMaxMinValues)
		{
			if (v0 == double.MaxValue || v0 == double.MinValue)
			{
				v0 = double.NaN;
			}
			if (v1 == double.MaxValue || v1 == double.MinValue)
			{
				v1 = double.NaN;
			}
		}
		if (double.IsNaN(v0))
		{
			return v1;
		}
		if (double.IsNaN(v1))
		{
			return v0;
		}
		if (v0 < v1)
		{
			return v0;
		}
		return v1;
	}

	public static double GetMaxValue(double v0, double v1, bool AllowMaxMinValues)
	{
		if (!AllowMaxMinValues)
		{
			if (v0 == double.MaxValue || v0 == double.MinValue)
			{
				v0 = double.NaN;
			}
			if (v1 == double.MaxValue || v1 == double.MinValue)
			{
				v1 = double.NaN;
			}
		}
		if (double.IsNaN(v0))
		{
			return v1;
		}
		if (double.IsNaN(v1))
		{
			return v0;
		}
		if (v0 > v1)
		{
			return v0;
		}
		return v1;
	}

	public static bool ParametricFractions(double v0, double v1, double rangeHi, double rangeLo, ref double t0, ref double t1)
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

	public static double Max3(double d1, double d2, double d3)
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

	public static float Max3(float d1, float d2, float d3)
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

	public static int Max3(int d1, int d2, int d3)
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

	public static int Min3Index(double d0, double d1, double d2)
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

	public static void Min3Index(double d0, double d1, double d2, ref int idx, ref double doubleValue)
	{
		if (d0 < d1)
		{
			if (d0 < d2)
			{
				idx = 0;
				doubleValue = d0;
			}
			else
			{
				idx = 2;
				doubleValue = d2;
			}
		}
		else if (d1 < d2)
		{
			idx = 1;
			doubleValue = d1;
		}
		else
		{
			idx = 2;
			doubleValue = d2;
		}
	}

	public static int MaxStringLength(string[] vals)
	{
		int systemLengthTerm = 0;
		for (int i = 0; i < vals.Length; i = checked(i + 1))
		{
			int length = Strings.Trim(vals[i]).Length;
			systemLengthTerm = System.Math.Max(systemLengthTerm, length);
		}
		return systemLengthTerm;
	}

	public static int MinIndex(params double[] vals)
	{
		if (vals == null)
		{
			throw new ArgumentNullException();
		}
		if (vals.Count() == 0)
		{
			return -1;
		}
		double computed = vals[0];
		int result = 0;
		checked
		{
			int count = vals.Count() - 1;
			for (int i = 1; i <= count; i++)
			{
				float valsTerm = (float)vals[i];
				if ((double)valsTerm < computed)
				{
					computed = valsTerm;
					result = i;
				}
			}
			return result;
		}
	}

	public static double Min3(double d1, double d2, double d3)
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

	public static float Min3(float d1, float d2, float d3)
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

	public static int Min3(int d1, int d2, int d3)
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

	public static double Mid3(double d1, double d2, double d3)
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

	public static float Mid3(float d1, float d2, float d3)
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

	public static void MaxMin3(int d0, int d1, int d2, ref int max, ref int min)
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

	public static void MaxMin3(float d0, float d1, float d2, ref float max, ref float min)
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

	public static void MaxMin3(double d0, double d1, double d2, ref double max, ref double min)
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

	public static void Sort3(ref double a, ref double b, ref double c)
	{
		if (!(a <= b) || !(b <= c))
		{
			if (a <= c && c <= b)
			{
				SwapD(ref b, ref c);
			}
			else if (b <= a && a <= c)
			{
				SwapD(ref a, ref b);
			}
			else if (b <= c && c <= a)
			{
				SwapD(ref a, ref b);
				SwapD(ref b, ref c);
			}
			else if (c <= a && a <= b)
			{
				SwapD(ref a, ref b);
				SwapD(ref a, ref c);
			}
			else
			{
				SwapD(ref a, ref c);
			}
		}
	}

	public static bool IsInRange(int intValue, int min, int max)
	{
		if (intValue >= min && intValue <= max)
		{
			return true;
		}
		return false;
	}

	public static bool IsInRange(double doubleValue, double min, double max)
	{
		if (doubleValue >= min && doubleValue <= max)
		{
			return true;
		}
		return false;
	}

	public static bool RangeContains(float min, float max, float containerMin, float containerMax)
	{
		if (containerMin <= min && containerMax >= max)
		{
			return true;
		}
		return false;
	}

	public static bool RangeOverlaps(float amin, float amax, float bmin, float bmax)
	{
		if (amax < bmin || amin > bmax)
		{
			return false;
		}
		return true;
	}

	public static double GetInRange(double doubleValue, double min, double max)
	{
		if (doubleValue > max)
		{
			return max;
		}
		if (doubleValue < min)
		{
			return min;
		}
		return doubleValue;
	}

	public static float GetInRange(float floatValue, float min, float max)
	{
		if (floatValue > max)
		{
			return max;
		}
		if (floatValue < min)
		{
			return min;
		}
		return floatValue;
	}

	public static int GetInRange(int intValue, int min, int max)
	{
		if (intValue > max)
		{
			return max;
		}
		if (intValue < min)
		{
			return min;
		}
		return intValue;
	}

	public static void SwapI(ref int n1, ref int n2)
	{
		int n12 = n1;
		n1 = n2;
		n2 = n12;
	}

	public static void SwapD(ref double n1, ref double n2)
	{
		double n12 = n1;
		n1 = n2;
		n2 = n12;
	}

	public static string Double2String(double doubleValue)
	{
		if (double.IsNaN(doubleValue))
		{
			return "";
		}
		return doubleValue.ToString();
	}

	public static double String2Double(string stringValue)
	{
		if (double.TryParse(stringValue, out var result))
		{
			return result;
		}
		return double.NaN;
	}

	public static int String2Integer(string stringValue)
	{
		if (int.TryParse(stringValue, out var result))
		{
			return result;
		}
		return 0;
	}

	public static double AddAngles(double angle1, double angle2)
	{
		return System.Math.IEEERemainder(angle1 + angle2, System.Math.PI * 2.0);
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

	public static void Solve2x2(double a00, double a01, double a10, double a11, double b0, double b1, ref double x0, ref double x1)
	{
		double a00A11Term = a00 * a11 - a01 * a10;
		x0 = (b0 * a11 - a01 * b1) / a00A11Term;
		x1 = (a00 * b1 - b0 * a10) / a00A11Term;
	}

	public static double BitConverterSwapBytesToDouble(byte[] bytes, int startindex)
	{
		byte[] byteValues = new byte[8];
		int ordinal = 0;
		checked
		{
			do
			{
				byteValues[7 - ordinal] = bytes[ordinal + startindex];
				ordinal++;
			}
			while (ordinal <= 7);
			return BitConverter.ToDouble(byteValues, 0);
		}
	}

	public static int BitConverterSwapBytesToInteger(byte[] Bytes, int startIndex)
	{
		return BitConverter.ToInt32(checked(new byte[4]
		{
			Bytes[startIndex + 3],
			Bytes[startIndex + 2],
			Bytes[startIndex + 1],
			Bytes[startIndex]
		}), 0);
	}

	public static byte[] BitConverterSwapBytesGetBytes(double doubleValue)
	{
		byte[] bytes = BitConverter.GetBytes(doubleValue);
		int ordinal = 0;
		checked
		{
			do
			{
				byte b = bytes[ordinal];
				bytes[ordinal] = bytes[7 - ordinal];
				bytes[7 - ordinal] = b;
				ordinal++;
			}
			while (ordinal <= 3);
			return bytes;
		}
	}

	public static byte[] BitConverterSwapBytesGetBytes(int intValue)
	{
		byte[] bytes = BitConverter.GetBytes(intValue);
		byte b = bytes[0];
		byte b2 = bytes[1];
		byte b3 = bytes[2];
		byte b4 = bytes[3];
		bytes[0] = b4;
		bytes[1] = b3;
		bytes[2] = b2;
		bytes[3] = b;
		return bytes;
	}

	public static void AddBytes(ref int bufferIndex, byte[] bufferBytes, byte[] bytesToAdd)
	{
		bytesToAdd.CopyTo(bufferBytes, bufferIndex);
		checked
		{
			bufferIndex += bytesToAdd.Count();
		}
	}

	public static void AddBytes(ref int bufferIndex, byte[] bufferBytes, byte byteToAdd)
	{
		bufferBytes[bufferIndex] = byteToAdd;
		checked
		{
			bufferIndex++;
		}
	}

	public static void ArrayMaxMin(float[] a, ref float max, ref float min)
	{
		max = float.MinValue;
		min = float.MaxValue;
		checked
		{
			int count = a.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				float computed = a[i];
				if (computed > max)
				{
					max = computed;
				}
				if (computed < min)
				{
					min = computed;
				}
			}
		}
	}

	public static void ArrayMaxMin(double[] a, ref double max, ref double min)
	{
		max = double.MinValue;
		min = double.MaxValue;
		checked
		{
			int count = a.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				double computed = a[i];
				if (computed > max)
				{
					max = computed;
				}
				if (computed < min)
				{
					min = computed;
				}
			}
		}
	}

	public static bool StringArraysEqual(string[] a, string[] b)
	{
		if (a == null && b == null)
		{
			return true;
		}
		if ((a == null) | (b == null))
		{
			return false;
		}
		if (a.Count() != b.Count())
		{
			return false;
		}
		checked
		{
			int count = a.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!a[i].Equals(b[i]))
				{
					return false;
				}
			}
			return true;
		}
	}

	public static bool ArraysEqual<T>(T[] a, T[] b) where T : struct, IEquatable<T>
	{
		if (a == null && b == null)
		{
			return true;
		}
		if ((a == null) | (b == null))
		{
			return false;
		}
		if (a.Count() != b.Count())
		{
			return false;
		}
		checked
		{
			int count = a.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!a[i].Equals(b[i]))
				{
					return false;
				}
			}
			return true;
		}
	}

	public static T Clamp<T>(T localValue, T min, T max) where T : IComparable
	{
		ref T reference = ref localValue;
		T referenceTerm = default;
		if (referenceTerm == null)
		{
			referenceTerm = reference;
			reference = ref referenceTerm;
		}
		if (reference.CompareTo(min) < 0)
		{
			return min;
		}
		ref T reference2 = ref localValue;
		referenceTerm = default;
		if (referenceTerm == null)
		{
			referenceTerm = reference2;
			reference2 = ref referenceTerm;
		}
		if (reference2.CompareTo(max) > 0)
		{
			return max;
		}
		return localValue;
	}

	public static void GaussJordanUnitTest()
	{
		double[,] doubleValues = new double[3, 3];
		double[,] doubleValues2 = new double[3, 1];
		doubleValues[0, 0] = 5.0;
		doubleValues[0, 1] = 1.0;
		doubleValues[0, 2] = 8.0;
		doubleValues[1, 0] = 4.0;
		doubleValues[1, 1] = -2.0;
		doubleValues[1, 2] = 0.0;
		doubleValues[2, 0] = 6.0;
		doubleValues[2, 1] = 7.0;
		doubleValues[2, 2] = 4.0;
		doubleValues2[0, 0] = 46.0;
		doubleValues2[1, 0] = 12.0;
		doubleValues2[2, 0] = 50.0;
		GaussJordan(doubleValues, doubleValues2, makeInverse: true);
	}

	public static void GaussJordan(double[,] a, double[,] b, bool makeInverse = false)
	{
		int length = a.GetLength(0);
		int length2 = b.GetLength(1);
		checked
		{
			int lengthOffset = length - 1;
			int length2Offset = length2 - 1;
			int[] intValues = new int[length + 1];
			int[] intValuesArray = new int[length - 1 + 1];
			int[] intValues2 = new int[length - 1 + 1];
			int count = lengthOffset;
			for (int i = 0; i <= count; i++)
			{
				intValues2[i] = -1;
			}
			int count2 = lengthOffset;
			int ordinal = default;
			int ordinal2 = default;
			for (int j = 0; j <= count2; j++)
			{
				double systemTerm = 0.0;
				int count3 = lengthOffset;
				for (int k = 0; k <= count3; k++)
				{
					if (intValues2[k] == 0)
					{
						continue;
					}
					int count4 = lengthOffset;
					for (int l = 0; l <= count4; l++)
					{
						if (intValues2[l] == -1)
						{
							if (System.Math.Abs(a[k, l]) > systemTerm)
							{
								systemTerm = System.Math.Abs(a[k, l]);
								ordinal = k;
								ordinal2 = l;
							}
						}
						else if (intValues2[l] > 0)
						{
							throw new Exception("Matrix is singular");
						}
					}
				}
				intValues2[ordinal2]++;
				if (ordinal != ordinal2)
				{
					int count5 = lengthOffset;
					for (int m = 0; m <= count5; m++)
					{
						double computed = a[ordinal, m];
						a[ordinal, m] = a[ordinal2, m];
						a[ordinal2, m] = computed;
					}
					int count6 = length2Offset;
					for (int n = 0; n <= count6; n++)
					{
						double computed2 = b[ordinal, n];
						b[ordinal, n] = b[ordinal2, n];
						b[ordinal2, n] = computed2;
					}
				}
				intValuesArray[j] = ordinal;
				intValues[j] = ordinal2;
				if (a[ordinal2, ordinal2] == 0.0)
				{
					throw new Exception("Matrix is singular");
				}
				double computed3 = 1.0 / a[ordinal2, ordinal2];
				a[ordinal2, ordinal2] = 1.0;
				int count7 = lengthOffset;
				for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
				{
					a[ordinal2, intervalIndex] *= computed3;
				}
				int count8 = length2Offset;
				for (int intervalIndex2 = 0; intervalIndex2 <= count8; intervalIndex2++)
				{
					b[ordinal2, intervalIndex2] *= computed3;
				}
				int count9 = lengthOffset;
				for (int intervalIndex3 = 0; intervalIndex3 <= count9; intervalIndex3++)
				{
					if (intervalIndex3 != ordinal2)
					{
						double computed4 = a[intervalIndex3, ordinal2];
						a[intervalIndex3, ordinal2] = 0.0;
						int count10 = lengthOffset;
						for (int intervalIndex4 = 0; intervalIndex4 <= count10; intervalIndex4++)
						{
							a[intervalIndex3, intervalIndex4] -= a[ordinal2, intervalIndex4] * computed4;
						}
						int count11 = length2 - 1;
						for (int intervalIndex5 = 0; intervalIndex5 <= count11; intervalIndex5++)
						{
							b[intervalIndex3, intervalIndex5] -= b[ordinal2, intervalIndex5] * computed4;
						}
					}
				}
			}
			if (!makeInverse)
			{
				return;
			}
			for (int lengthOffset2 = lengthOffset; lengthOffset2 >= 0; lengthOffset2 += -1)
			{
				if (intValuesArray[lengthOffset2] != intValues[lengthOffset2])
				{
					int count12 = lengthOffset;
					for (int intervalIndex6 = 0; intervalIndex6 <= count12; intervalIndex6++)
					{
						double computed5 = a[intervalIndex6, intValuesArray[lengthOffset2]];
						a[intervalIndex6, intValuesArray[lengthOffset2]] = a[intervalIndex6, intValues[lengthOffset2]];
						a[intervalIndex6, intValues[lengthOffset2]] = computed5;
					}
				}
			}
		}
	}
}
