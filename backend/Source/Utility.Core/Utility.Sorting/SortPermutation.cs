using System;
using System.Globalization;
using Utility.Comparers;

namespace Utility.Sorting;

public static class SortPermutation
{
	private const int RadixBits = 8;

	private const int RadixBuckets = 256;

	private const ulong RadixMask = 255uL;

	public const int HiddenRow = -1;

	public static int[] Identity(int rowCount)
	{
		if (rowCount < 0)
		{
			rowCount = 0;
		}
		int[] intValues = new int[rowCount];
		for (int i = 0; i < rowCount; i++)
		{
			intValues[i] = i;
		}
		return intValues;
	}

	public static int[] BuildVisible(int rowCount, Predicate<int> matches)
	{
		if (rowCount < 0)
		{
			rowCount = 0;
		}
		if (matches == null)
		{
			return Identity(rowCount);
		}
		int[] intValues = new int[rowCount];
		int count = 0;
		for (int i = 0; i < rowCount; i++)
		{
			if (matches(i))
			{
				intValues[count++] = i;
			}
		}
		if (count == rowCount)
		{
			return intValues;
		}
		int[] intValuesArray = new int[count];
		Array.Copy(intValues, intValuesArray, count);
		return intValuesArray;
	}

	public static int[] Build(double[] keys, SortDirection direction)
	{
		if (keys == null)
		{
			return null;
		}
		if (direction == SortDirection.None)
		{
			return Identity(keys.Length);
		}
		int keyLength = keys.Length;
		int[] intValues = new int[keyLength];
		ulong[] ulongs = new ulong[keyLength];
		bool directionSortDirectionTerm = direction == SortDirection.Descending;
		int ordinal = 0;
		for (int i = 0; i < keyLength; i++)
		{
			if (!double.IsNaN(keys[i]))
			{
				intValues[ordinal] = i;
				ulong toSortableBits = ToSortableBits(keys[i]);
				ulongs[ordinal] = (directionSortDirectionTerm ? (~toSortableBits) : toSortableBits);
				ordinal++;
			}
		}
		int ordinal2 = ordinal;
		for (int j = 0; j < keyLength; j++)
		{
			if (double.IsNaN(keys[j]))
			{
				intValues[ordinal2++] = j;
			}
		}
		RadixSort(ulongs, intValues, ordinal);
		return intValues;
	}

	private static ulong ToSortableBits(double computed)
	{
		if (computed == 0.0)
		{
			return 9223372036854775808uL;
		}
		ulong doubleToUInt64Bits = BitConverter.DoubleToUInt64Bits(computed);
		return doubleToUInt64Bits ^ (ulong)(((long)doubleToUInt64Bits >> 63) | long.MinValue);
	}

	private static void RadixSort(ulong[] keys, int[] items, int count)
	{
		if (count < 2)
		{
			return;
		}
		ulong[] ulongs = new ulong[count];
		int[] intValues = new int[count];
		ulong[] keysArray = keys;
		int[] intValuesArray = items;
		ulong[] ulongsArray = ulongs;
		int[] intValues2 = intValues;
		Span<int> intSpan = stackalloc int[256];
		for (int i = 0; i < 64; i += 8)
		{
			intSpan.Clear();
			for (int j = 0; j < count; j++)
			{
				intSpan[(int)((keysArray[j] >> i) & 0xFF)]++;
			}
			if (intSpan[(int)((keysArray[0] >> i) & 0xFF)] != count)
			{
				int ordinal = 0;
				for (int k = 0; k < 256; k++)
				{
					int ordinal2 = intSpan[k];
					intSpan[k] = ordinal;
					ordinal += ordinal2;
				}
				for (int l = 0; l < count; l++)
				{
					int keysArrayTerm = intSpan[(int)((keysArray[l] >> i) & 0xFF)]++;
					ulongsArray[keysArrayTerm] = keysArray[l];
					intValues2[keysArrayTerm] = intValuesArray[l];
				}
				ulong[] ulongs2 = ulongsArray;
				ulongsArray = keysArray;
				keysArray = ulongs2;
				int[] intValues3 = intValues2;
				intValues2 = intValuesArray;
				intValuesArray = intValues3;
			}
		}
		if (intValuesArray != items)
		{
			Array.Copy(intValuesArray, items, count);
		}
	}

	public static int[] Build(string[] keys, SortDirection direction)
	{
		if (keys == null)
		{
			return null;
		}
		if (direction == SortDirection.None)
		{
			return Identity(keys.Length);
		}
		int keyLength = keys.Length;
		int count = 0;
		for (int i = 0; i < keyLength; i++)
		{
			if (!string.IsNullOrEmpty(keys[i]))
			{
				count++;
			}
		}
		int[] intValues = new int[keyLength];
		int[] intValuesArray = new int[count];
		int ordinal = 0;
		int count2 = count;
		for (int j = 0; j < keyLength; j++)
		{
			if (string.IsNullOrEmpty(keys[j]))
			{
				intValues[count2++] = j;
			}
			else
			{
				intValuesArray[ordinal++] = j;
			}
		}
		if (count > 1)
		{
			int sign = ((direction != SortDirection.Descending) ? 1 : (-1));
			Array.Sort(intValuesArray, (int x, int y) =>
			{
				int signCompareTerm = sign * NaturalStringComparer.Instance.Compare(keys[x], keys[y]);
				return (signCompareTerm == 0) ? x.CompareTo(y) : signCompareTerm;
			});
		}
		Array.Copy(intValuesArray, intValues, count);
		return intValues;
	}

	public static bool TryDetectNumeric(string[] keys, out double[] numeric, IFormatProvider formatProvider = null)
	{
		numeric = null;
		if (keys == null)
		{
			return false;
		}
		if (formatProvider == null)
		{
			formatProvider = CultureInfo.CurrentCulture;
		}
		double[] numericArray = new double[keys.Length];
		for (int i = 0; i < keys.Length; i++)
		{
			string keysTerm = keys[i];
			if (string.IsNullOrWhiteSpace(keysTerm))
			{
				numericArray[i] = double.NaN;
				continue;
			}
			if (!double.TryParse(keysTerm, NumberStyles.Float | NumberStyles.AllowThousands, formatProvider, out var result))
			{
				return false;
			}
			if (!HasValidGrouping(keysTerm, NumberFormatInfo.GetInstance(formatProvider)))
			{
				return false;
			}
			numericArray[i] = result;
		}
		numeric = numericArray;
		return true;
	}

	private static bool HasValidGrouping(string textValue, NumberFormatInfo format)
	{
		string numberGroupSeparator = format.NumberGroupSeparator;
		if (numberGroupSeparator.Length != 1)
		{
			return true;
		}
		char c = numberGroupSeparator[0];
		int indexOf = textValue.IndexOf(c);
		if (indexOf < 0)
		{
			return true;
		}
		int count = textValue.Length;
		if (format.NumberDecimalSeparator.Length > 0)
		{
			int indexOf2 = textValue.IndexOf(format.NumberDecimalSeparator, StringComparison.Ordinal);
			if (indexOf2 >= 0)
			{
				count = indexOf2;
			}
		}
		int ordinal = 0;
		for (int i = 0; i < indexOf; i++)
		{
			if (char.IsAsciiDigit(textValue[i]))
			{
				ordinal++;
			}
		}
		if (ordinal < 1 || ordinal > 3)
		{
			return false;
		}
		for (int j = indexOf; j < count; j++)
		{
			if (textValue[j] != c)
			{
				continue;
			}
			for (int k = 1; k <= 3; k++)
			{
				if (j + k >= count || !char.IsAsciiDigit(textValue[j + k]))
				{
					return false;
				}
			}
			int ordinal2 = j + 4;
			if (ordinal2 < count && textValue[ordinal2] != c)
			{
				return false;
			}
		}
		return true;
	}

	public static int[] Invert(int[] permutation)
	{
		if (permutation == null)
		{
			return null;
		}
		int[] intValues = new int[permutation.Length];
		for (int i = 0; i < permutation.Length; i++)
		{
			intValues[permutation[i]] = i;
		}
		return intValues;
	}

	public static int[] Invert(int[] permutation, int dataRowCount)
	{
		if (permutation == null)
		{
			return null;
		}
		if (dataRowCount < 0)
		{
			dataRowCount = 0;
		}
		int[] intValues = new int[dataRowCount];
		Array.Fill(intValues, -1);
		for (int i = 0; i < permutation.Length; i++)
		{
			int permutation2 = permutation[i];
			if (permutation2 >= 0 && permutation2 < dataRowCount)
			{
				intValues[permutation2] = i;
			}
		}
		return intValues;
	}
}
