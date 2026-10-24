namespace Geospatial;

public interface IMutableSpatialVersion : ISpatialVersion
{
	void ForceSetVersion(long longValue);
}
