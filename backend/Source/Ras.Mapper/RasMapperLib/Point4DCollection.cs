using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RasMapperLib;

[ComVisible(false)]
public class Point4DCollection : List<Point4D>
{
	public string XName;

	public string XUnits;

	public string YName;

	public string YUnits;

	public string ZName;

	public string ZUnits;

	public string MName;

	public string MUnits;

	public Point4DCollection()
	{
	}

	public Point4DCollection(Point4DCollection pts)
	{
		if (pts != null && pts.Count != 0)
		{
			Capacity = pts.Count;
			AddRange(pts);
		}
	}

	public Point4DCollection(string XName, string XUnits, string YName, string YUnits, string ZName, string ZUnits, string MName, string MUnits, string spaceAndCommaDelimetedData = "")
	{
		this.XName = XName;
		this.XUnits = XUnits;
		this.YName = YName;
		this.YUnits = YUnits;
		this.ZName = ZName;
		this.ZUnits = ZUnits;
		this.MName = MName;
		this.MUnits = MUnits;
		AddRange(spaceAndCommaDelimetedData);
	}

	public void AssignNamesUnits(string XName, string XUnits, string YName, string YUnits, string ZName, string ZUnits, string MName, string MUnits)
	{
		this.XName = XName;
		this.XUnits = XUnits;
		this.YName = YName;
		this.YUnits = YUnits;
		this.ZName = ZName;
		this.ZUnits = ZUnits;
		this.MName = MName;
		this.MUnits = MUnits;
	}

	public void Add(float x, float y, float z, float m)
	{
		Add(new Point4D(x, y, z, m));
	}

	public void AddRange(string spaceAndCommaDelimetedData)
	{
		if (spaceAndCommaDelimetedData.Length != 0)
		{
			string[] parts = spaceAndCommaDelimetedData.Trim().Split(" ");
			foreach (string commaDelimitedString in parts)
			{
				Point4D commaDelimitedString2 = new Point4D(commaDelimitedString);
				Add(commaDelimitedString2);
			}
		}
	}

	public void RemoveRange(int indexToStartRemoving)
	{
		RemoveRange(indexToStartRemoving, checked(Count - indexToStartRemoving));
	}

	public Point4DCollection Copy()
	{
		Point4DCollection point4DCollection = new Point4DCollection(XName, XUnits, YName, YUnits, ZName, ZUnits, MName, MUnits);
		point4DCollection.Capacity = Count;
		using Enumerator itemEnumerator = GetEnumerator();
		while (itemEnumerator.MoveNext())
		{
			Point4D current = itemEnumerator.Current;
			point4DCollection.Add(current);
		}
		return point4DCollection;
	}

	public void AssignX(int index, float x)
	{
		Point4D point4D = base[index];
		point4D.X = x;
		base[index] = point4D;
	}

	public void AssignY(int index, float y)
	{
		Point4D point4D = base[index];
		point4D.Y = y;
		base[index] = point4D;
	}

	public void AssignXY(int index, float x, float y)
	{
		Point4D point4D = base[index];
		point4D.X = x;
		point4D.Y = y;
		base[index] = point4D;
	}

	public void AddToX(int index, float x)
	{
		Point4D point4D = base[index];
		point4D.X += x;
		base[index] = point4D;
	}

	public void AddToY(int index, float y)
	{
		Point4D point4D = base[index];
		point4D.Y += y;
		base[index] = point4D;
	}

	public void Translate(float dX, float dY)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point4D point4D = base[i];
				point4D.X += dX;
				point4D.Y += dY;
				base[i] = point4D;
			}
		}
	}

	public void RatioX(float ratio)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point4D point4D = base[i];
				point4D.X *= ratio;
				base[i] = point4D;
			}
		}
	}

	public void RatioY(float ratio)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point4D point4D = base[i];
				point4D.Y *= ratio;
				base[i] = point4D;
			}
		}
	}

	public string ExportToCSV()
	{
		return ExportToCSV("", "");
	}

	public string ExportToCSV(string xFormat, string yFormat)
	{
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				stringBuilder.Append(base[i].ConvertToCSV(xFormat, yFormat));
				if (i < Count - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			return stringBuilder.ToString();
		}
	}

	public void ExportToArray(int offset, float[,] xy)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point4D point4D = base[i];
				xy[0, offset] = point4D.X;
				xy[1, offset] = point4D.Y;
			}
		}
	}

	public float MaxX()
	{
		float minValue = float.MinValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point4D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.X > minValue)
				{
					minValue = current.X;
				}
			}
		}
		if (minValue < float.MaxValue)
		{
			return minValue;
		}
		return float.NaN;
	}

	public float MinX()
	{
		float maxValue = float.MaxValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point4D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.X < maxValue)
				{
					maxValue = current.X;
				}
			}
		}
		if (maxValue < float.MaxValue)
		{
			return maxValue;
		}
		return float.NaN;
	}

	public float MaxY()
	{
		float minValue = float.MinValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point4D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.Y > minValue)
				{
					minValue = current.Y;
				}
			}
		}
		if (minValue < float.MaxValue)
		{
			return minValue;
		}
		return float.NaN;
	}

	public float MinY()
	{
		float maxValue = float.MaxValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point4D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.Y < maxValue)
				{
					maxValue = current.Y;
				}
			}
		}
		if (maxValue < float.MaxValue)
		{
			return maxValue;
		}
		return float.NaN;
	}

	public void SortX()
	{
		Sort(Point4D.CompareX);
	}

	public void SortY()
	{
		Sort(Point4D.CompareY);
	}

	public void DebugPrint()
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public Point4DCollection FilterXY(float filterTol, List<int> indexBuffer = null)
	{
		List<int> indexBufferList = ((indexBuffer != null) ? indexBuffer : new List<int>());
		FilterXYKeepList(filterTol, indexBufferList);
		Point4DCollection point4DCollection = new Point4DCollection();
		point4DCollection.Capacity = indexBufferList.Count;
		checked
		{
			int count = indexBufferList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point4DCollection.Add(base[indexBufferList[i]]);
			}
			return point4DCollection;
		}
	}

	public void FilterXYKeepList(float filterTol, List<int> indexes)
	{
		indexes.Clear();
		int ordinal = 0;
		int countOffset = checked(Count - 1);
		if (Count > 0)
		{
			indexes.Add(ordinal);
		}
		if (Count > 1)
		{
			indexes.Add(countOffset);
		}
		FilterDouglasPeucker(ordinal, countOffset, filterTol, indexes);
		indexes.Sort();
	}

	private void FilterDouglasPeucker(int first, int last, float Tolerance, List<int> keepIndex)
	{
		checked
		{
			if (last - first <= 1)
			{
				return;
			}
			float computed = 0f;
			int ordinal = 0;
			float distanceToXY = base[first].DistanceToXY(base[last]);
			int ordinal2 = first + 1;
			int count = last - 1;
			for (int i = ordinal2; i <= count; i++)
			{
				float distanceToXYTerm = Math.Abs(base[first].X * base[last].Y + base[last].X * base[i].Y + base[i].X * base[first].Y - base[last].X * base[first].Y - base[i].X * base[last].Y - base[first].X * base[i].Y) / distanceToXY;
				if (distanceToXYTerm > computed)
				{
					computed = distanceToXYTerm;
					ordinal = i;
				}
			}
			if (computed > Tolerance)
			{
				keepIndex.Add(ordinal);
				FilterDouglasPeucker(first, ordinal, Tolerance, keepIndex);
				FilterDouglasPeucker(ordinal, last, Tolerance, keepIndex);
			}
		}
	}

	public void ToImmediateWindow(string headerline = "")
	{
		string.IsNullOrEmpty(headerline);
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public override string ToString()
	{
		return "Profile";
	}
}
