using System;

namespace Geospatial.Rendering;

public class BlurDouble
{
	public double Factor = 1.0;

	public int Offset;

	public int Width => Matrix.GetLength(1);

	public int Height => Matrix.GetLength(0);

	public double[,] Matrix { get; private set; }

	public BlurDouble()
	{
		Matrix = new double[1, 1];
		Matrix[0, 0] = 1.0;
	}

	public BlurDouble(double[,] mat)
	{
		if (mat.GetLength(0) != mat.GetLength(1))
		{
			throw new ArgumentException("mat");
		}
		Matrix = mat;
	}

	public static BlurDouble Gaussian(double sigma, int reach)
	{
		double[,] doubleValues = new double[reach * 2 + 1, reach * 2 + 1];
		double sigmaPolynomial = sigma * sigma;
		double computed = 0.0;
		for (int i = -reach; i <= reach; i++)
		{
			for (int j = -reach; j <= reach; j++)
			{
				doubleValues[i + reach, j + reach] = 1.0 / (Math.PI * 2.0 * sigmaPolynomial) * Math.Exp((double)(-(j * j + i * i)) / (2.0 * sigmaPolynomial));
				computed += doubleValues[i + reach, j + reach];
			}
		}
		double computed2 = 1.0 / computed;
		for (int k = -reach; k <= reach; k++)
		{
			for (int l = -reach; l <= reach; l++)
			{
				doubleValues[k + reach, l + reach] *= computed2;
			}
		}
		return new BlurDouble(doubleValues);
	}

	public static BlurDouble Box(int reach)
	{
		double[,] doubleValues = new double[reach * 2 + 1, reach * 2 + 1];
		int getLengthTerm = doubleValues.GetLength(0) * doubleValues.GetLength(1);
		for (int i = -reach; i <= reach; i++)
		{
			for (int j = -reach; j <= reach; j++)
			{
				doubleValues[i + reach, j + reach] = 1.0 / (double)getLengthTerm;
			}
		}
		return new BlurDouble(doubleValues);
	}

	public static BlurDouble BoxBright(int reach = 10, double factor = 0.3, int offset = 160)
	{
		BlurDouble blurDouble = Box(reach);
		blurDouble.Factor = factor;
		blurDouble.Offset = offset;
		return blurDouble;
	}
}
