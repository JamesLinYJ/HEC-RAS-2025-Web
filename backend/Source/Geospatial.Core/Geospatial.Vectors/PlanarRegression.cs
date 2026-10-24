using System;

namespace Geospatial.Vectors;

public class PlanarRegression
{
	private double _baseX;

	private double _baseY;

	private double _sumX2;

	private double _sumX;

	private double _sumY2;

	private double _sumY;

	private double _sumZ;

	private double _sumXY;

	private double _sumYZ;

	private double _sumXZ;

	private double _sumWeight;

	private int _n;

	public double SumWeights => _sumWeight;

	public int Count => _n;

	public PlanarRegression()
		: this(0.0, 0.0)
	{
	}

	public PlanarRegression(double baseX, double baseY)
	{
		_baseX = baseX;
		_baseY = baseY;
	}

	public PlanarRegression(Point basePt)
		: this(basePt.X, basePt.Y)
	{
	}

	public void Clear(double baseX, double baseY)
	{
		_baseX = baseX;
		_baseY = baseY;
		Clear();
	}

	public void Clear(Point pt)
	{
		Clear(pt.X, pt.Y);
	}

	public void Clear()
	{
		_sumX2 = 0.0;
		_sumX = 0.0;
		_sumY2 = 0.0;
		_sumY = 0.0;
		_sumZ = 0.0;
		_sumXY = 0.0;
		_sumYZ = 0.0;
		_sumXZ = 0.0;
		_sumWeight = 0.0;
		_n = 0;
	}

	public void Add(Point pt, double z)
	{
		Add(pt.X, pt.Y, z);
	}

	public void Add(Point pt, double z, double weight)
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
		_sumZ += z;
		_sumXY += wxAboveBaseX * wyAboveBaseY;
		_sumYZ += wyAboveBaseY * z;
		_sumXZ += wxAboveBaseX * z;
		_n++;
	}

	public void Add(double wx, double wy, double z, double weight)
	{
		if (weight != 0.0)
		{
			double wxAboveBaseX = wx - _baseX;
			double wyAboveBaseY = wy - _baseY;
			_sumX2 += wxAboveBaseX * wxAboveBaseX * weight;
			_sumX += wxAboveBaseX * weight;
			_sumY2 += wyAboveBaseY * wyAboveBaseY * weight;
			_sumY += wyAboveBaseY * weight;
			_sumZ += z * weight;
			_sumXY += wxAboveBaseX * wyAboveBaseY * weight;
			_sumYZ += wyAboveBaseY * z * weight;
			_sumXZ += wxAboveBaseX * z * weight;
			_n++;
			_sumWeight += weight;
		}
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
		if (_n == 0)
		{
			throw new Exception("No data added to PlanarRegression");
		}
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
}
