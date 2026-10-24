using System;

namespace Geospatial.Rasters.Convolution;

public class Blur
{
	public float Factor = 1f;

	public int Offset;

	public int Width => Matrix.GetLength(1);

	public int Height => Matrix.GetLength(0);

	public float[,] Matrix { get; private set; }

	public Blur()
	{
		Matrix = new float[1, 1];
		Matrix[0, 0] = 1f;
	}

	public Blur(float[,] mat)
	{
		if (mat.GetLength(0) != mat.GetLength(1))
		{
			throw new ArgumentException("mat");
		}
		Matrix = mat;
	}

	public static Blur Gaussian(float sigma, int reach)
	{
		float[,] floatValues = new float[reach * 2 + 1, reach * 2 + 1];
		float sigmaPolynomial = sigma * sigma;
		float computed = 0f;
		for (int i = -reach; i <= reach; i++)
		{
			for (int j = -reach; j <= reach; j++)
			{
				floatValues[i + reach, j + reach] = (float)(1.0 / (Math.PI * 2.0 * (double)sigmaPolynomial) * Math.Exp((float)(-(j * j + i * i)) / (2f * sigmaPolynomial)));
				computed += floatValues[i + reach, j + reach];
			}
		}
		float computed2 = 1f / computed;
		for (int k = -reach; k <= reach; k++)
		{
			for (int l = -reach; l <= reach; l++)
			{
				floatValues[k + reach, l + reach] *= computed2;
			}
		}
		return new Blur(floatValues);
	}

	public static Blur Box(int reach)
	{
		float[,] floatValues = new float[reach * 2 + 1, reach * 2 + 1];
		int getLengthTerm = floatValues.GetLength(0) * floatValues.GetLength(1);
		for (int i = -reach; i <= reach; i++)
		{
			for (int j = -reach; j <= reach; j++)
			{
				floatValues[i + reach, j + reach] = 1f / (float)getLengthTerm;
			}
		}
		return new Blur(floatValues);
	}

	public static Blur BoxBright(int reach)
	{
		Blur blur = Box(reach);
		blur.Factor = 1.2f;
		blur.Offset = 20;
		return blur;
	}
}
