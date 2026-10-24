namespace Geospatial;

public interface ISpatialVersion
{
	long VersionXY { get; }

	void FreezeOne();

	void ThawOne();
}
