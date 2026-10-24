using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace RasMapperLib;

[ComVisible(false)]
public class DistinctSpatialIndex : SpatialIndex<int>
{
	private int _knownMaxValue;


	private ThreadLocal<BitArray> _bitmasks;

	public DistinctSpatialIndex(IExtent ext, int knownMaxValue, int maxBinCount = 4096)
		: base(ext, Math.Min(maxBinCount, SpatialIndex.ReasonableBinCount(knownMaxValue)))
	{
		_knownMaxValue = -1;
		_knownMaxValue = knownMaxValue;
		_bitmasks = new ThreadLocal<BitArray>([SpecialName] () => new BitArray(_knownMaxValue));
	}

	public static DistinctSpatialIndex DefaultSpatialIndex()
	{
		return new DistinctSpatialIndex(new Extent(1.0, 0.0, 1.0, 0.0), 1);
	}

	public void AddRange(IPoints points)
	{
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Add(points.PointM(i), i);
			}
		}
	}

	public void AddRangeInBounds(IPoints points)
	{
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (_raster.Contains(points.PointM(i)))
				{
					Add(points.PointM(i), i);
				}
			}
		}
	}

	public List<int> GetDistinctElements(Extent ext)
	{
		return GetDistinctElements(ext.MinX, ext.MaxX, ext.MinY, ext.MaxY);
	}

	public List<int> GetDistinctElements(double minX, double maxX, double minY, double maxY)
	{
		List<int> distinctElements = new List<int>();
		if (!_raster.Overlaps(minX, maxX, minY, maxY))
		{
			return distinctElements;
		}
		RowColRangeM range = _raster.RowColRange(minX, maxX, minY, maxY, limitToRaster: true);
		AddRange(range, distinctElements);
		return distinctElements;
	}

	private void AddRange(RowColRangeM range, List<int> segmentList)
	{
		BitArray bitArray = _bitmasks.Value;
		bitArray.SetAll(value: false);
		int minRow = range.MinRow;
		int maxRow = range.MaxRow;
		checked
		{
			for (int i = minRow; i <= maxRow; i++)
			{
				int minCol = range.MinCol;
				int maxCol = range.MaxCol;
				for (int j = minCol; j <= maxCol; j++)
				{
					int cellIndex = _raster.CellIndex(i, j);
					List<int> bin = Bins[cellIndex];
					int count = bin.Count - 1;
					for (int k = 0; k <= count; k++)
					{
						int bin2 = bin[k];
						if (!bitArray[bin2])
						{
							bitArray[bin2] = true;
							segmentList.Add(bin2);
						}
					}
				}
			}
		}
	}

	public static DistinctSpatialIndex CreateSegmentIndex(Polygon poly)
	{
		DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(poly, poly.SegmentCount());
		checked
		{
			int count = poly.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				distinctSpatialIndex.Add(poly.Segment(i), i);
			}
			return distinctSpatialIndex;
		}
	}
}
