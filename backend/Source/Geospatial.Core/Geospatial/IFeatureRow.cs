using Geospatial.Vectors;
using Utility.Hashing;

namespace Geospatial;

public interface IFeatureRow : ITableRow, ISpatialVersion, IDataHash, IExtent
{
	void ISpatialVersion.FreezeOne()
	{
	}

	void ISpatialVersion.ThawOne()
	{
	}
}
