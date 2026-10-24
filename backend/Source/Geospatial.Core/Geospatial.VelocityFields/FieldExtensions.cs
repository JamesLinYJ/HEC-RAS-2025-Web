using System;
using Geospatial.Vectors;

namespace Geospatial.VelocityFields;

public static class FieldExtensions
{
	public static Vector[,] Rasterize(this IStaticVelocityField field, Extent bbox, int pxWidth, int pxHeight)
	{
		Vector[,] vectors = new Vector[pxHeight, pxWidth];
		double widthOverPxWidth = bbox.Width / (double)pxWidth;
		double heightOverPxHeight = bbox.Height / (double)pxHeight;
		float computed = (float)(widthOverPxWidth + heightOverPxHeight) / 2f;
		Math.Round(widthOverPxWidth, 2);
		Math.Round(heightOverPxHeight, 2);
		for (int i = 0; i < pxHeight; i++)
		{
			for (int j = 0; j < pxWidth; j++)
			{
				Point point = bbox.LowerLeft + new Vector(((float)j + 0.5f) * computed, ((float)i + 0.5f) * computed);
				vectors[i, j] = field.GetVelocity(point);
				if (vectors[i, j] != Vector.NullVelocity)
				{
					vectors[i, j] /= computed;
				}
			}
		}
		return vectors;
	}
}
