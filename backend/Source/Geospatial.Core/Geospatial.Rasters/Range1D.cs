using System;
using System.Collections.Generic;
using Utility.DataStructures;

namespace Geospatial.Rasters;

public struct Range1D
{
	private const int NullValue = int.MinValue;

	public static readonly Range1D NullRange = new Range1D(int.MinValue, int.MinValue);

	public readonly int Start;

	public readonly int End;

	public readonly int Count => End - Start + 1;

	public bool IsNull()
	{
		if (Start == int.MinValue)
		{
			return End == int.MinValue;
		}
		return false;
	}

	public Range1D(int start, int end)
	{
		Start = start;
		End = end;
	}

	public bool Contains(Range1D other)
	{
		if (Start <= other.Start)
		{
			return End >= other.End;
		}
		return false;
	}

	public bool Contains(int idx)
	{
		if (idx >= Start)
		{
			return idx <= End;
		}
		return false;
	}

	public bool IsStrictlyBefore(Range1D other)
	{
		return End < other.Start;
	}

	public bool IsStrictlyBeforeWithGap(Range1D other)
	{
		return End < other.Start - 1;
	}

	public bool StartsBefore(Range1D other)
	{
		return Start < other.Start;
	}

	public bool IsStrictlyAfter(Range1D other)
	{
		return other.End < Start;
	}

	public bool IsStrictlyAfterWithGap(Range1D other)
	{
		return other.End < Start - 1;
	}

	public bool EndsAfter(Range1D other)
	{
		return End > other.End;
	}

	public bool IsNotAfter(Range1D other)
	{
		return !EndsAfter(other);
	}

	public bool IsNotBefore(Range1D other)
	{
		return !StartsBefore(other);
	}

	public bool IsConesecutiveOrOverlappingWith(Range1D other)
	{
		if (!IsStrictlyAfterWithGap(other))
		{
			return !IsStrictlyBeforeWithGap(other);
		}
		return false;
	}

	public Range1D Merge(Range1D other)
	{
		return new Range1D(Math.Min(Start, other.Start), Math.Max(End, other.End));
	}

	public static void InsertInto(List<Range1D> existingRanges, Range1D newRange)
	{
		if (existingRanges == null)
		{
			throw new ArgumentNullException("existingRanges");
		}
		if (existingRanges.Count == 0)
		{
			existingRanges.Add(newRange);
			return;
		}
		int minValue = int.MinValue;
		int minValue2 = int.MinValue;
		for (int i = 0; i < existingRanges.Count; i++)
		{
			Range1D other = existingRanges[i];
			if (newRange.IsStrictlyAfterWithGap(other))
			{
				continue;
			}
			if (minValue == int.MinValue)
			{
				if (newRange.IsStrictlyBeforeWithGap(other))
				{
					existingRanges.Insert(i, newRange);
					return;
				}
				if (newRange.IsNotAfter(other))
				{
					existingRanges[i] = newRange.Merge(other);
					return;
				}
				minValue = i;
			}
			else
			{
				if (newRange.IsStrictlyBeforeWithGap(other))
				{
					minValue2 = i - 1;
					break;
				}
				if (newRange.IsNotAfter(other))
				{
					minValue2 = i;
					break;
				}
			}
		}
		if (minValue == int.MinValue)
		{
			existingRanges.Add(newRange);
			return;
		}
		Range1D other2 = existingRanges[minValue];
		if (minValue2 == int.MinValue)
		{
			existingRanges.RemoveRange(minValue, existingRanges.Count - minValue);
			existingRanges.Add(newRange.Merge(other2));
			return;
		}
		if (minValue == minValue2)
		{
			existingRanges[minValue] = newRange.Merge(other2);
			return;
		}
		Range1D other3 = existingRanges[minValue2];
		Range1D existingRanges2 = newRange.Merge(other2).Merge(other3);
		existingRanges.RemoveRange(minValue + 1, minValue2 - minValue);
		existingRanges[minValue] = existingRanges2;
	}

	public static CompactList<Range1D> Merge(CompactList<Range1D> rangesThis, CompactList<Range1D> rangesOther)
	{
		if (rangesThis.Count == 0)
		{
			return rangesOther;
		}
		if (rangesOther.Count == 0)
		{
			return rangesThis;
		}
		int ordinal = 0;
		int ordinal2 = 0;
		Range1D initialValue = (rangesThis[ordinal].StartsBefore(rangesOther[ordinal2]) ? rangesThis[ordinal] : rangesOther[ordinal2]);
		CompactList<Range1D> result = new CompactList<Range1D>(initialValue);
		while (ordinal < rangesThis.Count || ordinal2 < rangesOther.Count)
		{
			Range1D other = result[result.Count - 1];
			if (ordinal < rangesThis.Count)
			{
				Range1D range1D = rangesThis[ordinal];
				if (range1D.IsConesecutiveOrOverlappingWith(result[result.Count - 1]))
				{
					result[result.Count - 1] = rangesThis[ordinal].Merge(other);
					ordinal++;
					continue;
				}
			}
			if (ordinal2 < rangesOther.Count)
			{
				Range1D range1D = rangesOther[ordinal2];
				if (range1D.IsConesecutiveOrOverlappingWith(result[result.Count - 1]))
				{
					result[result.Count - 1] = rangesOther[ordinal2].Merge(other);
					ordinal2++;
					continue;
				}
			}
			if (ordinal >= rangesThis.Count)
			{
				result.Add(rangesOther[ordinal2]);
				ordinal2++;
			}
			else if (ordinal2 >= rangesOther.Count)
			{
				result.Add(rangesThis[ordinal]);
				ordinal++;
			}
			else if (rangesThis[ordinal].StartsBefore(rangesOther[ordinal2]))
			{
				result.Add(rangesThis[ordinal]);
				ordinal++;
			}
			else
			{
				result.Add(rangesOther[ordinal2]);
				ordinal2++;
			}
		}
		return result;
	}

	public static bool operator ==(Range1D A, Range1D B)
	{
		if (A.Start == B.Start)
		{
			return A.End == B.End;
		}
		return false;
	}

	public static bool operator !=(Range1D A, Range1D B)
	{
		return !(A == B);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (obj is Range1D range1D)
		{
			return range1D == this;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return base.GetHashCode();
	}
}
