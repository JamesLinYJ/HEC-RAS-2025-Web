using H5Assist;
using H5Assist.ORM;

namespace Geospatial.IO.ORM;

public static class GeospatialH5Metadata
{
	public static bool Read<T>(T instance, H5Reader hr, string rootH5Group, string prefix = "")
	{
		GeospatialH5Serializers.Init();
		return H5Metadata.Read(instance, hr, rootH5Group, prefix);
	}

	public static void Write<T>(T instance, H5Writer hw, string rootH5Group, string prefix = "")
	{
		GeospatialH5Serializers.Init();
		H5Metadata.Write(instance, hw, rootH5Group, prefix);
	}
}
