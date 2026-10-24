using System;
using System.Collections.Generic;
using System.Threading;

namespace Geospatial.Vectors;

public class ShapeFunction
{
	private struct PixelComputeBuffer
	{
		public double[] CWCanGive;

		public double[] CCWCanGive;

		public double[] XProducts;

		public int Count;

		public PixelComputeBuffer(int count)
		{
			this = default;
			Resize(count);
		}

		public void Resize(int count)
		{
			CWCanGive = new double[count];
			CCWCanGive = new double[count];
			XProducts = new double[count];
			Count = count;
		}
	}

	private Polygon _polygon;

	private ThreadLocal<PixelComputeBuffer> _buffers;

	public ShapeFunction(Polygon pgon)
	{
		_polygon = pgon;
		_buffers = new ThreadLocal<PixelComputeBuffer>(() => new PixelComputeBuffer(_polygon.PointCount));
	}

	public double[] ComputeWeights(Point pt)
	{
		int pointCount = _polygon.PointCount;
		PixelComputeBuffer pixelComputeBuffer = _buffers.Value;
		double[] pixelXProducts = GetPixelXProducts(pt, pixelComputeBuffer);
		for (int i = 0; i < pixelXProducts.Length; i++)
		{
			if (pixelXProducts[i] == 0.0)
			{
				pixelXProducts[i] = 1E-05;
			}
		}
		double[] weights = new double[pointCount];
		double computed = 0.0;
		for (int j = 0; j < pointCount; j++)
		{
			int ordinal = j;
			int previousPointIndex = _polygon.PreviousPointIndex(ordinal);
			double weights2 = 1.0;
			for (int k = 0; k < pointCount; k++)
			{
				if (k != ordinal && k != previousPointIndex)
				{
					weights2 *= pixelXProducts[k];
				}
			}
			weights[j] = weights2;
			computed += weights2;
		}
		for (int l = 0; l < pointCount; l++)
		{
			weights[l] /= computed;
		}
		return weights;
	}

	public double ComputeZ(Point pt)
	{
		double[] weights = ComputeWeights(pt);
		double computed = 0.0;
		for (int i = 0; i < _polygon.PointCount; i++)
		{
			computed += weights[i] * _polygon.Z[i];
		}
		return computed;
	}

	public double ComputeM(Point pt)
	{
		double[] weights = ComputeWeights(pt);
		double computed = 0.0;
		for (int i = 0; i < _polygon.PointCount; i++)
		{
			computed += weights[i] * _polygon.M[i];
		}
		return computed;
	}

	public double Compute(Point pt, IList<double> polygonPointValues)
	{
		if (polygonPointValues == null || polygonPointValues.Count != _polygon.PointCount)
		{
			throw new ArgumentException("polygonPointValues");
		}
		double[] weights = ComputeWeights(pt);
		double computed = 0.0;
		for (int i = 0; i < _polygon.PointCount; i++)
		{
			computed += weights[i] * polygonPointValues[i];
		}
		return computed;
	}

	public float Compute(Point pt, IList<float> polygonPointValues)
	{
		if (polygonPointValues == null || polygonPointValues.Count != _polygon.PointCount)
		{
			throw new ArgumentException("polygonPointValues");
		}
		double[] weights = ComputeWeights(pt);
		double computed = 0.0;
		for (int i = 0; i < _polygon.PointCount; i++)
		{
			computed += weights[i] * (double)polygonPointValues[i];
		}
		return (float)computed;
	}

	private static void BensWeights(Point point, Mesh mesh, int cIdx, float areaTolerance, PixelComputeBuffer buf, ref float[] fpWeights, ref float[] velocityWeights)
	{
		Cell cell = mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		int length2 = length;
		double[] pixelXProducts = GetPixelXProducts(mesh, cIdx, point, buf);
		for (int i = 0; i < pixelXProducts.Length; i++)
		{
			if (pixelXProducts[i] == 0.0)
			{
				pixelXProducts[i] = 1E-05;
			}
		}
		double[] doubleValues = new double[length2];
		double computed = 0.0;
		for (int j = 0; j < length2; j++)
		{
			int ordinal = j;
			int length2Offset = j - 1;
			if (length2Offset == -1)
			{
				length2Offset = length2 - 1;
			}
			double unity = 1.0;
			for (int k = 0; k < length2; k++)
			{
				if (k != ordinal && k != length2Offset)
				{
					unity *= pixelXProducts[k];
				}
			}
			int index = j;
			int length2Offset2 = j - 1;
			if (length2Offset2 == -1)
			{
				length2Offset2 = length2 - 1;
			}
			int faceIndex = faces[index];
			int faceIndex2 = faces[length2Offset2];
			int fPPrev = mesh.Faces[faceIndex2].GetFPPrev(cIdx);
			int fPNext = mesh.Faces[faceIndex2].GetFPNext(cIdx);
			int fPNext2 = mesh.Faces[faceIndex].GetFPNext(cIdx);
			Point c = mesh.FacePoints[fPPrev].Point;
			Point a = mesh.FacePoints[fPNext].Point;
			Point b = mesh.FacePoints[fPNext2].Point;
			computed += (doubleValues[j] = unity * Vector.CrossProduct(in a, in b, in c));
		}
		fpWeights = new float[length2];
		bool condition = false;
		for (int l = 0; l < length2; l++)
		{
			if (doubleValues[l] < 0.0)
			{
				condition = true;
			}
			fpWeights[l] = (float)(doubleValues[l] / computed);
		}
		velocityWeights = new float[length * 2];
		if (condition)
		{
			float fpWeightSum = 0f;
			for (int m = 0; m < fpWeights.Length; m++)
			{
				if (fpWeights[m] < 0f)
				{
					fpWeights[m] = 0f;
				}
				else
				{
					fpWeightSum += fpWeights[m];
				}
			}
			if (fpWeightSum != 0f)
			{
				for (int n = 0; n < length2; n++)
				{
					fpWeights[n] /= fpWeightSum;
					velocityWeights[n] = fpWeights[n];
				}
			}
		}
		Donate(fpWeights, velocityWeights, buf);
	}

	private static void Donate(float[] fpWeights, float[] velocityWeights, PixelComputeBuffer buf)
	{
		int count = buf.Count;
		double[] cWCanGive = buf.CWCanGive;
		double[] cCWCanGive = buf.CCWCanGive;
		for (int i = 0; i < count; i++)
		{
			int countOffset = i - 1;
			if (countOffset == -1)
			{
				countOffset = count - 1;
			}
			int ordinal = i + 1;
			if (ordinal == count)
			{
				ordinal = 0;
			}
			float fpWeight = fpWeights[i];
			float fpWeight2 = fpWeights[countOffset];
			float fpWeight3 = fpWeights[ordinal];
			float fpWeight2PlusFpWeight3 = fpWeight2 + fpWeight3;
			if (fpWeight2PlusFpWeight3 != 0f)
			{
				double fpWeightOverFpWeight2PlusFpWeight3 = (double)fpWeight / (double)fpWeight2PlusFpWeight3;
				cWCanGive[i] = fpWeightOverFpWeight2PlusFpWeight3 * (double)fpWeight2;
				cCWCanGive[i] = fpWeightOverFpWeight2PlusFpWeight3 * (double)fpWeight3;
			}
			velocityWeights[i] = fpWeights[i];
		}
		for (int j = 0; j < count; j++)
		{
			int ordinal2 = j;
			int ordinal3 = j + 1;
			if (ordinal3 == count)
			{
				ordinal3 = 0;
			}
			_ = fpWeights[ordinal2];
			_ = fpWeights[ordinal3];
			double cCWCanGive2 = cCWCanGive[ordinal2];
			double cWCanGive2 = cWCanGive[ordinal3];
			double cCWCanGive2CWCanGive2Term = Math.Min(cCWCanGive2, cWCanGive2);
			velocityWeights[ordinal2] -= (float)cCWCanGive2CWCanGive2Term;
			velocityWeights[ordinal3] -= (float)cCWCanGive2CWCanGive2Term;
			velocityWeights[count + j] = (float)cCWCanGive2CWCanGive2Term * 2f;
		}
	}

	private static double[] GetPixelXProducts(Mesh mesh, int cIdx, Point p, PixelComputeBuffer buf)
	{
		Cell cell = mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		double[] xProducts = buf.XProducts;
		for (int i = 0; i < length; i++)
		{
			int faceIndex = faces[i];
			Face face = mesh.Faces[faceIndex];
			int fPPrev = face.GetFPPrev(cIdx);
			int fPNext = face.GetFPNext(cIdx);
			Point b = mesh.FacePoints[fPPrev].Point;
			Point c = mesh.FacePoints[fPNext].Point;
			xProducts[i] = Vector.CrossProduct(in p, in b, in c);
		}
		return xProducts;
	}

	private double[] GetPixelXProducts(Point p, PixelComputeBuffer buf)
	{
		double[] xProducts = buf.XProducts;
		for (int i = 0; i < _polygon.SegmentCount; i++)
		{
			Segment segment = _polygon.Segments[i];
			xProducts[i] = Vector.CrossProduct(in p, in segment.B, in segment.A);
		}
		return xProducts;
	}
}
