using System;

namespace RasMapperLib;

public class PlanarRegressionZ
{
	private double _baseX;

	private double _baseY;

	private double _sumX2;

	private double _sumX;

	private double _sumY2;

	private double _sumY;

	private double _sumZ2;

	private double _sumZ;

	private double _sumXY;

	private double _sumYZ;

	private double _sumXZ;

	private int _n;

	private double _sumWeight;

	public double SumWeights => _sumWeight;

	public PlanarRegressionZ(double baseX, double baseY)
	{
		_sumX2 = 0.0;
		_sumX = 0.0;
		_sumY2 = 0.0;
		_sumY = 0.0;
		_sumZ2 = 0.0;
		_sumZ = 0.0;
		_sumXY = 0.0;
		_sumYZ = 0.0;
		_sumXZ = 0.0;
		_n = 0;
		_sumWeight = 0.0;
		_baseX = baseX;
		_baseY = baseY;
	}

	public PlanarRegressionZ(Point2Double basePt)
	{
		_sumX2 = 0.0;
		_sumX = 0.0;
		_sumY2 = 0.0;
		_sumY = 0.0;
		_sumZ2 = 0.0;
		_sumZ = 0.0;
		_sumXY = 0.0;
		_sumYZ = 0.0;
		_sumXZ = 0.0;
		_n = 0;
		_sumWeight = 0.0;
		_baseX = basePt.X;
		_baseY = basePt.Y;
	}

	public void Clear()
	{
		_sumX2 = 0.0;
		_sumX = 0.0;
		_sumY2 = 0.0;
		_sumY = 0.0;
		_sumZ2 = 0.0;
		_sumZ = 0.0;
		_sumXY = 0.0;
		_sumYZ = 0.0;
		_sumXZ = 0.0;
		_sumWeight = 0.0;
		_n = 0;
	}

	public void Add(Point2Double pt, double z)
	{
		Add(pt.X, pt.Y, z);
	}

	public void Add(Point2Double pt, double z, double weight)
	{
		Add(pt.X, pt.Y, z, weight);
	}

	public void Add(double wx, double wy, double z)
	{
		double wxAboveBaseX = wx - _baseX;
		double wyAboveBaseY = wy - _baseY;
		_sumX2 += wxAboveBaseX * wxAboveBaseX;
		_sumX += wxAboveBaseX;
		_sumY2 += wyAboveBaseY * wyAboveBaseY;
		_sumY += wyAboveBaseY;
		_sumZ2 += z * z;
		_sumZ += z;
		_sumXY += wxAboveBaseX * wyAboveBaseY;
		_sumYZ += wyAboveBaseY * z;
		_sumXZ += wxAboveBaseX * z;
		checked
		{
			_n++;
		}
	}

	public void Add(double wx, double wy, double z, double weight)
	{
		checked
		{
			if (weight != 0.0)
			{
				double wxAboveBaseX = wx - _baseX;
				double wyAboveBaseY = wy - _baseY;
				_sumX2 += wxAboveBaseX * wxAboveBaseX * weight;
				_sumX += wxAboveBaseX * weight;
				_sumY2 += wyAboveBaseY * wyAboveBaseY * weight;
				_sumY += wyAboveBaseY * weight;
				_sumZ2 += z * z * weight;
				_sumZ += z * weight;
				_sumXY += wxAboveBaseX * wyAboveBaseY * weight;
				_sumYZ += wyAboveBaseY * z * weight;
				_sumXZ += wxAboveBaseX * z * weight;
				_n++;
				_sumWeight += weight;
			}
		}
	}

	public int Count()
	{
		return _n;
	}

	public double Determinant()
	{
		return _sumX2 * (_sumY2 * (double)_n - _sumY * _sumY) - _sumXY * (_sumXY * (double)_n - _sumY * _sumX) + _sumX * (_sumXY * _sumY - _sumY2 * _sumX);
	}

	public double DeterminantWithWeights()
	{
		return _sumX2 * (_sumY2 * _sumWeight - _sumY * _sumY) - _sumXY * (_sumXY * _sumWeight - _sumY * _sumX) + _sumX * (_sumXY * _sumY - _sumY2 * _sumX);
	}

	public double SolveZ()
	{
		if (_n == 1)
		{
			return _sumZ;
		}
		if (_n == 2)
		{
			return _sumZ * 0.5;
		}
		double determinant = Determinant();
		if (determinant == 0.0)
		{
			return _sumZ / (double)_n;
		}
		return (_sumX2 * (_sumY2 * _sumZ - _sumYZ * _sumY) - _sumXY * (_sumXY * _sumZ - _sumYZ * _sumX) + _sumXZ * (_sumXY * _sumY - _sumY2 * _sumX)) / determinant;
	}

	public double SolveZWithWeights()
	{
		if (_n == 0)
		{
			return double.NaN;
		}
		if (_n == 1)
		{
			return _sumZ / _sumWeight;
		}
		if (_n == 2)
		{
			return _sumZ / _sumWeight;
		}
		double determinantWithWeights = DeterminantWithWeights();
		if (Math.Abs(determinantWithWeights) < 1E-08)
		{
			return _sumZ / _sumWeight;
		}
		return (_sumX2 * (_sumY2 * _sumZ - _sumYZ * _sumY) - _sumXY * (_sumXY * _sumZ - _sumYZ * _sumX) + _sumXZ * (_sumXY * _sumY - _sumY2 * _sumX)) / determinantWithWeights;
	}

	public static void TestPlanarRegression()
	{
		PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(5.0, 5.0);
		planarRegressionZ.Add(6.008150222, 4.586558102, 4.195316606);
		planarRegressionZ.Add(1.442180026, 8.467361133, 4.994225573);
		planarRegressionZ.Add(0.050255198, 0.897003848, 0.861065723);
		planarRegressionZ.SolveZ();
		PlanarRegressionZ planarRegressionZ2 = new PlanarRegressionZ(5.0, 5.0);
		planarRegressionZ2.Add(9.97938180403745, 7.05178554646226, 4.50333680431107);
		planarRegressionZ2.Add(2.78871106847265, 3.30474755041277, 4.21891800860762);
		planarRegressionZ2.Add(6.66318929956475, 5.11193116419044, 4.31045398141075);
		planarRegressionZ2.Add(6.12428159481191, 4.66096402607044, 4.34559393291479);
		planarRegressionZ2.Add(3.54357993830027, 5.56205347297975, 4.30408356021628);
		planarRegressionZ2.Add(0.361998413834748, 0.223982752287641, 4.10257367275548);
		planarRegressionZ2.Add(6.10669190590231, 2.97382772680076, 4.23044518783453);
		planarRegressionZ2.Add(8.33214633615365, 6.59999136232042, 4.44001531968612);
		planarRegressionZ2.Add(8.75212390811609, 9.06043677705433, 4.52034004014624);
		planarRegressionZ2.Add(8.47159711000224, 3.43409260052968, 4.2864700646942);
		planarRegressionZ2.SolveZ();
	}
}
