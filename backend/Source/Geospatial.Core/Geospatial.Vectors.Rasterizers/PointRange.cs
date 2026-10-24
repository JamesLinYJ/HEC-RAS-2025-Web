using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Utility.Memory;

namespace Geospatial.Vectors.Rasterizers;

public struct PointRange
{
	public readonly int QueryStartIndex;

	public readonly int QueryEndIndex;

	public readonly int LocalStartIndex;

	public int Count => QueryEndIndex - QueryStartIndex + 1;

	public PointRange(int queryStart, int queryEnd, int localStart)
	{
		QueryStartIndex = queryStart;
		QueryEndIndex = queryEnd;
		LocalStartIndex = localStart;
	}

	public static PointRange[] ComputeRanges(IList<int> sortedPixels)
	{
		List<int> intList = new List<int>();
		int sortedPixel = sortedPixels[0];
		intList.Add(0);
		for (int i = 1; i < sortedPixels.Count; i++)
		{
			int sortedPixel2 = sortedPixels[i];
			if (sortedPixel2 == sortedPixel + 1)
			{
				sortedPixel++;
				continue;
			}
			intList.Add(i);
			sortedPixel = sortedPixel2;
		}
		PointRange[] pointRanges = new PointRange[intList.Count];
		int ordinal = 0;
		for (int j = 0; j < intList.Count; j++)
		{
			int sortedPixel3 = sortedPixels[intList[j]];
			int sortedPixel4;
			if (j < intList.Count - 1)
			{
				sortedPixel4 = sortedPixels[intList[j + 1] - 1];
			}
			else
			{
				sortedPixel4 = sortedPixels[sortedPixels.Count - 1];
			}
			pointRanges[j] = new PointRange(sortedPixel3, sortedPixel4, ordinal);
			ordinal += sortedPixel4 - sortedPixel3 + 1;
		}
		return pointRanges;
	}

	public static PointRange[] ComputeRangesFromRowScanlines(List<PixelRange> pixelRanges, RasterDefinition rdef)
	{
		PointRange[] pointRanges = new PointRange[pixelRanges.Count];
		ComputeRangesFromRowScanlines(pixelRanges, pointRanges, rdef);
		return pointRanges;
	}

	public static Memory<PointRange> ComputeRangesFromRowScanlines(List<PixelRange> pixelRanges, BufferAllocator<PointRange> pointRangeBuffer, RasterDefinition rdef)
	{
		Memory<PointRange> result = pointRangeBuffer.Get(pixelRanges.Count);
		ComputeRangesFromRowScanlines(pixelRanges, result.Span, rdef);
		return result;
	}

	public static void ComputeRangesFromRowScanlines(List<PixelRange> pixelRanges, Span<PointRange> ranges, RasterDefinition rdef)
	{
		int ordinal = 0;
		for (int i = 0; i < pixelRanges.Count; i++)
		{
			PixelRange pixelRange = pixelRanges[i];
			int cellIndex = rdef.CellIndex(pixelRange.UpperLeft);
			int cellIndex2 = rdef.CellIndex(pixelRange.UpperRight);
			ranges[i] = new PointRange(cellIndex, cellIndex2, ordinal);
			ordinal += cellIndex2 - cellIndex + 1;
		}
	}
}
