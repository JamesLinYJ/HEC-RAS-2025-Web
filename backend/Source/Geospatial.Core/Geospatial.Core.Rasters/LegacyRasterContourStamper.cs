using System;
using System.Drawing;
using Geospatial.Rasters;

namespace Geospatial.Core.Rasters;

public static class LegacyRasterContourStamper
{
	public static void ComputeContour(float contourInterval, float strokeRadius, Color color, RasterDefinition raster, Span<float> values, Span<int> pixels)
	{
		for (int i = 0; i < raster.Rows - 1; i++)
		{
			for (int j = 0; j < raster.Cols - 1; j++)
			{
				float colsRasterTerm = values[i * raster.Cols + j];
				if (colsRasterTerm == -9999f)
				{
					continue;
				}
				float contourIntervalTerm = MathF.Round(colsRasterTerm / contourInterval) * contourInterval;
				float computed = MathF.Abs(colsRasterTerm - contourIntervalTerm);
				for (int k = 0; k <= 1; k++)
				{
					for (int l = 0; l <= 1; l++)
					{
						if (l == 0 && k == 0)
						{
							continue;
						}
						float colsRasterTerm2 = values[(i + k) * raster.Cols + (j + l)];
						if (colsRasterTerm2 == -9999f || colsRasterTerm2 == colsRasterTerm)
						{
							continue;
						}
						float computed2 = MathF.Abs(colsRasterTerm2 - contourIntervalTerm);
						float computed3 = ((colsRasterTerm2 < colsRasterTerm) ? colsRasterTerm2 : colsRasterTerm);
						if (((colsRasterTerm2 > colsRasterTerm) ? colsRasterTerm2 : colsRasterTerm) > contourIntervalTerm && computed3 <= contourIntervalTerm)
						{
							int ordinal = j;
							int ordinal2 = i;
							if (computed2 < computed)
							{
								ordinal += l;
								ordinal2 += k;
							}
							FillCircle(j, i, strokeRadius, color, raster, pixels);
						}
					}
				}
			}
		}
	}

	private static void FillCircle(int x, int y, float radius, Color color, RasterDefinition raster, Span<int> pixels)
	{
		int count = (int)MathF.Ceiling(radius);
		float radiusPolynomial = radius * radius;
		for (int i = -count; i <= count; i++)
		{
			for (int j = -count; j <= count; j++)
			{
				if (!((float)(j * j + i * i) > radiusPolynomial))
				{
					int colsRasterTerm = Math.Clamp(x + j, 0, raster.Cols - 1);
					int rowsRasterTerm = Math.Clamp(y + i, 0, raster.Rows - 1);
					pixels[rowsRasterTerm * raster.Cols + colsRasterTerm] = color.ToArgb();
				}
			}
		}
	}
}
