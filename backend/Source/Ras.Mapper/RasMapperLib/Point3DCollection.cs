using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RasMapperLib;

[ComVisible(false)]
public class Point3DCollection : List<Point3D>
{
	public string XName;

	public string XUnits;

	public string YName;

	public string YUnits;

	public string ZName;

	public string ZUnits;

	public Point3DCollection()
	{
	}

	public Point3DCollection(Point3DCollection pts)
	{
		if (pts != null && pts.Count != 0)
		{
			Capacity = pts.Count;
			AddRange(pts);
		}
	}

	public Point3DCollection(string XName, string XUnits, string YName, string YUnits, string ZName, string ZUnits, string spaceAndCommaDelimetedData = "")
	{
		this.XName = XName;
		this.XUnits = XUnits;
		this.YName = YName;
		this.YUnits = YUnits;
		this.ZName = ZName;
		this.ZUnits = ZUnits;
		AddRange(spaceAndCommaDelimetedData);
	}

	public void AssignNamesUnits(string XName, string XUnits, string YName, string YUnits, string ZName, string ZUnits)
	{
		this.XName = XName;
		this.XUnits = XUnits;
		this.YName = YName;
		this.YUnits = YUnits;
		this.ZName = ZName;
		this.ZUnits = ZUnits;
	}

	public void Add(float x, float y, float z)
	{
		Add(new Point3D(x, y, z));
	}

	public void AddRange(string spaceAndCommaDelimetedData)
	{
		if (spaceAndCommaDelimetedData.Length != 0)
		{
			string[] parts = spaceAndCommaDelimetedData.Trim().Split(" ");
			foreach (string commaDelimitedString in parts)
			{
				Point3D commaDelimitedString2 = new Point3D(commaDelimitedString);
				Add(commaDelimitedString2);
			}
		}
	}

	public void RemoveRange(int indexToStartRemoving)
	{
		RemoveRange(indexToStartRemoving, checked(Count - indexToStartRemoving));
	}

	public Point3DCollection Copy()
	{
		Point3DCollection point3DCollection = new Point3DCollection(XName, XUnits, YName, YUnits, ZName, ZUnits);
		point3DCollection.Capacity = Count;
		using Enumerator itemEnumerator = GetEnumerator();
		while (itemEnumerator.MoveNext())
		{
			Point3D current = itemEnumerator.Current;
			point3DCollection.Add(current);
		}
		return point3DCollection;
	}

	public void AssignX(int index, float x)
	{
		Point3D point3D = base[index];
		point3D.X = x;
		base[index] = point3D;
	}

	public void AssignY(int index, float y)
	{
		Point3D point3D = base[index];
		point3D.Y = y;
		base[index] = point3D;
	}

	public void AssignXY(int index, float x, float y)
	{
		Point3D point3D = base[index];
		point3D.X = x;
		point3D.Y = y;
		base[index] = point3D;
	}

	public void AddToX(int index, float x)
	{
		Point3D point3D = base[index];
		point3D.X += x;
		base[index] = point3D;
	}

	public void AddToY(int index, float y)
	{
		Point3D point3D = base[index];
		point3D.Y += y;
		base[index] = point3D;
	}

	public void Translate(float dX, float dY)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point3D point3D = base[i];
				point3D.X += dX;
				point3D.Y += dY;
				base[i] = point3D;
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
				Point3D point3D = base[i];
				point3D.X *= ratio;
				base[i] = point3D;
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
				Point3D point3D = base[i];
				point3D.Y *= ratio;
				base[i] = point3D;
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
				Point3D point3D = base[i];
				xy[0, offset] = point3D.X;
				xy[1, offset] = point3D.Y;
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
				Point3D current = itemEnumerator.Current;
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
				Point3D current = itemEnumerator.Current;
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
				Point3D current = itemEnumerator.Current;
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
				Point3D current = itemEnumerator.Current;
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
		Sort(Point3D.CompareX);
	}

	public void SortY()
	{
		Sort(Point3D.CompareY);
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

	public Point3DCollection FilterXY(float filterTol, List<int> indexBuffer = null)
	{
		List<int> indexBufferList = ((indexBuffer != null) ? indexBuffer : new List<int>());
		FilterXYKeepList(filterTol, indexBufferList);
		Point3DCollection point3DCollection = new Point3DCollection();
		point3DCollection.Capacity = indexBufferList.Count;
		checked
		{
			int count = indexBufferList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point3DCollection.Add(base[indexBufferList[i]]);
			}
			return point3DCollection;
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
