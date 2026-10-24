using System;

namespace Geospatial.Vectors;

public struct Ellipse : IMutableSpatialVersion, ISpatialVersion, IExtent
{
	public float RadiusX;

	public float RadiusY;

	public float RotationDegrees;

	public Point Center;

	public long VersionXY
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public Extent Extent
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public void ForceSetVersion(long longValue)
	{
		throw new NotImplementedException();
	}

	public void FreezeOne()
	{
		throw new NotImplementedException();
	}

	public void ThawOne()
	{
		throw new NotImplementedException();
	}
}
