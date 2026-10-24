using System.Threading;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist.ORM;

namespace Geospatial.IO.ORM;

public static class GeospatialH5Serializers
{
	private static readonly Lock _lock = new Lock();

	public static bool IsInitialized = false;

	public static void Init()
	{
		if (IsInitialized)
		{
			return;
		}
		using (_lock.EnterScope())
		{
			if (!IsInitialized)
			{
				H5Serializers.Register(typeof(RasterDefinition), new H5RasterDefinitionSerializer());
				H5Serializers.Register(typeof(Extent), new H5ExtentSerializer());
				IsInitialized = true;
			}
		}
	}
}
