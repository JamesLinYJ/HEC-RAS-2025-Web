namespace Geospatial;

public static class ISpatialVersionHelpers
{
	public static FreezeHandle FreezeSafely(this ISpatialVersion iSpatialVersion)
	{
		return new FreezeHandle(iSpatialVersion);
	}
}
