using System;
using Geospatial.Rasters;

namespace Geospatial.Vectors;

public class PlanarRegression3x3
{
	public static void Solve(RasterDefinition rDef, Memory<float> data, Memory<float> outA, Memory<float> outB, float noDataValue)
	{
		Span<float> floatSpan = data.Span;
		Span<float> outASpan = outA.Span;
		Span<float> outBSpan = outB.Span;
		float scaledCellSize = (float)(rDef.CellSize * 6.0);
		int rows = rDef.Rows;
		int cols = rDef.Cols;
		for (int i = 1; i < rows - 1; i++)
		{
			for (int j = 1; j < cols - 1; j++)
			{
				int colsRDefTerm = i * rDef.Cols + j;
				int index = colsRDefTerm - cols;
				int index2 = colsRDefTerm + cols;
				int ordinal = colsRDefTerm - 1;
				int index3 = ordinal - cols;
				int index4 = ordinal + cols;
				int ordinal2 = colsRDefTerm + 1;
				int index5 = ordinal2 - cols;
				int index6 = ordinal2 + cols;
				outASpan[colsRDefTerm] = (floatSpan[ordinal2] + floatSpan[index5] + floatSpan[index6] - floatSpan[ordinal] - floatSpan[index3] - floatSpan[index4]) / scaledCellSize;
				outBSpan[colsRDefTerm] = (floatSpan[index3] + floatSpan[index] + floatSpan[index5] - floatSpan[index4] - floatSpan[index2] - floatSpan[index6]) / scaledCellSize;
			}
		}
	}
}
