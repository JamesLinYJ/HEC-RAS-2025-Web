using System;

namespace Geospatial.Vectors.MeshTopology;

public class ConstantSizeFunction : ISizeFunction
{
	public double size;

	public ConstantSizeFunction(double size)
	{
		if (size <= 0.0)
		{
			throw new ArgumentOutOfRangeException("size");
		}
		this.size = size;
	}

	public double Query(Point p, bool checkInside = false)
	{
		return size;
	}

	public void QueryRange(Point point, bool checkInside, out double maximum, out double minimum)
	{
		minimum = (maximum = size);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(size);
	}
}
