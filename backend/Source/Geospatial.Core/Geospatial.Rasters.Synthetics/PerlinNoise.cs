using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters.Synthetics;

public class PerlinNoise : IResample<float>, ISurface<float>
{
	private static readonly int[] permutation;

	private static readonly int[] p;

	public int repeat = 16;

	private int _octaves;

	private double _persistence;

	private double _lenScale;

	public Extent ValidExtent { get; }

	public float? NoDataValue => -9999f;

	static PerlinNoise()
	{
		permutation = new int[256]
		{
			151, 160, 137, 91, 90, 15, 131, 13, 201, 95,
			96, 53, 194, 233, 7, 225, 140, 36, 103, 30,
			69, 142, 8, 99, 37, 240, 21, 10, 23, 190,
			6, 148, 247, 120, 234, 75, 0, 26, 197, 62,
			94, 252, 219, 203, 117, 35, 11, 32, 57, 177,
			33, 88, 237, 149, 56, 87, 174, 20, 125, 136,
			171, 168, 68, 175, 74, 165, 71, 134, 139, 48,
			27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
			60, 211, 133, 230, 220, 105, 92, 41, 55, 46,
			245, 40, 244, 102, 143, 54, 65, 25, 63, 161,
			1, 216, 80, 73, 209, 76, 132, 187, 208, 89,
			18, 169, 200, 196, 135, 130, 116, 188, 159, 86,
			164, 100, 109, 198, 173, 186, 3, 64, 52, 217,
			226, 250, 124, 123, 5, 202, 38, 147, 118, 126,
			255, 82, 85, 212, 207, 206, 59, 227, 47, 16,
			58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
			119, 248, 152, 2, 44, 154, 163, 70, 221, 153,
			101, 155, 167, 43, 172, 9, 129, 22, 39, 253,
			19, 98, 108, 110, 79, 113, 224, 232, 178, 185,
			112, 104, 218, 246, 97, 228, 251, 34, 242, 193,
			238, 210, 144, 12, 191, 179, 162, 241, 81, 51,
			145, 235, 249, 14, 239, 107, 49, 192, 214, 31,
			181, 199, 106, 157, 184, 84, 204, 176, 115, 121,
			50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
			222, 114, 67, 29, 24, 72, 243, 141, 128, 195,
			78, 66, 215, 61, 156, 180
		};
		p = new int[512];
		for (int i = 0; i < 512; i++)
		{
			p[i] = permutation[i % 256];
		}
	}

	public PerlinNoise(Extent ext, int octaves, double persistence, double lengthScale = 1.0)
	{
		ValidExtent = ext;
		_octaves = octaves;
		_persistence = persistence;
		_lenScale = lengthScale;
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		Extent validExtent = ValidExtent;
		Span<float> outDataSpan = outData.Span;
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		RasterDefinition raster = args.Raster;
		for (int i = 0; i < raster.CellCount; i++)
		{
			Point pt = raster.CellCenter(i);
			if (!validExtent.Contains(pt))
			{
				outDataSpan[i] = valueOrDefault;
			}
			else
			{
				outDataSpan[i] = (float)OctavePerlin(pt.X * _lenScale, pt.Y * _lenScale, 0.0, _octaves, _persistence);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		Extent validExtent = ValidExtent;
		Span<Point> pointsSpan = points.Span;
		Span<float> outDataSpan = outData.Span;
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point pt = pointsSpan[i];
			if (!validExtent.Contains(pt))
			{
				outDataSpan[i] = valueOrDefault;
			}
			else
			{
				outDataSpan[i] = (float)OctavePerlin(pt.X, pt.Y);
			}
		}
	}

	public double OctavePerlin(double x, double y)
	{
		return OctavePerlin(x * _lenScale, y * _lenScale, 0.0, _octaves, _persistence);
	}

	public double OctavePerlin(double x, double y, double z)
	{
		return OctavePerlin(x * _lenScale, y * _lenScale, z, _octaves, _persistence);
	}

	public double OctavePerlin(double x, double y, double z, int octaves, double persistence)
	{
		double computed = 0.0;
		double unity = 1.0;
		double unity2 = 1.0;
		double computed2 = 0.0;
		for (int i = 0; i < octaves; i++)
		{
			double perlin = Perlin(x * unity, y * unity, z * unity);
			computed += perlin * unity2;
			computed2 += unity2;
			unity2 *= persistence;
			unity *= 2.0;
		}
		return computed / computed2;
	}

	private double Perlin(double x, double y, double z)
	{
		if (repeat > 0)
		{
			x %= (double)repeat;
			y %= (double)repeat;
			z %= (double)repeat;
		}
		int ordinal = (int)x & 0xFF;
		int ordinal2 = (int)y & 0xFF;
		int ordinal3 = (int)z & 0xFF;
		double computed = x - (double)(int)x;
		double computed2 = y - (double)(int)y;
		double computed3 = z - (double)(int)z;
		double x2 = Fade(computed);
		double x3 = Fade(computed2);
		double x4 = Fade(computed3);
		int hash = p[p[p[ordinal] + ordinal2] + ordinal3];
		int hash2 = p[p[p[ordinal] + Inc(ordinal2)] + ordinal3];
		int hash3 = p[p[p[ordinal] + ordinal2] + Inc(ordinal3)];
		int hash4 = p[p[p[ordinal] + Inc(ordinal2)] + Inc(ordinal3)];
		int hash5 = p[p[p[Inc(ordinal)] + ordinal2] + ordinal3];
		int hash6 = p[p[p[Inc(ordinal)] + Inc(ordinal2)] + ordinal3];
		int hash7 = p[p[p[Inc(ordinal)] + ordinal2] + Inc(ordinal3)];
		int hash8 = p[p[p[Inc(ordinal)] + Inc(ordinal2)] + Inc(ordinal3)];
		double a = LERP(Grad(hash, computed, computed2, computed3), Grad(hash5, computed - 1.0, computed2, computed3), x2);
		double b = LERP(Grad(hash2, computed, computed2 - 1.0, computed3), Grad(hash6, computed - 1.0, computed2 - 1.0, computed3), x2);
		double a2 = LERP(a, b, x3);
		double a3 = LERP(Grad(hash3, computed, computed2, computed3 - 1.0), Grad(hash7, computed - 1.0, computed2, computed3 - 1.0), x2);
		b = LERP(Grad(hash4, computed, computed2 - 1.0, computed3 - 1.0), Grad(hash8, computed - 1.0, computed2 - 1.0, computed3 - 1.0), x2);
		double b2 = LERP(a3, b, x3);
		return (LERP(a2, b2, x4) + 1.0) / 2.0;
	}

	private int Inc(int ordinal)
	{
		ordinal++;
		if (repeat > 0)
		{
			ordinal %= repeat;
		}
		return ordinal;
	}

	private static double Grad(int hash, double x, double y, double z)
	{
		int hashTerm = hash & 0xF;
		double computed = ((hashTerm < 8) ? x : y);
		double computed2;
		if (hashTerm < 4)
		{
			computed2 = y;
		}
		else
		{
			computed2 = ((hashTerm != 12 && hashTerm != 14) ? z : x);
		}
		return (((hashTerm & 1) == 0) ? computed : (0.0 - computed)) + (((hashTerm & 2) == 0) ? computed2 : (0.0 - computed2));
	}

	private static double Fade(double t)
	{
		return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
	}

	private static double LERP(double a, double b, double x)
	{
		return a + x * (b - a);
	}
}
