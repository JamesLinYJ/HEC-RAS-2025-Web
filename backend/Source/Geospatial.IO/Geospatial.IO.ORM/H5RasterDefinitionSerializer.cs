using Geospatial.Rasters;
using H5Assist;
using H5Assist.ORM;

namespace Geospatial.IO.ORM;

public class H5RasterDefinitionSerializer : H5Serializer
{
	public override object Read(H5Reader hr, string group, string attrPrefix)
	{
		return HDF5VectorReader.TryReadRasterDefinition(hr, group, attrPrefix);
	}

	public override void Write(H5Writer hw, string group, string attrPrefix, object o)
	{
		if (o is RasterDefinition raster)
		{
			HDF5VectorReader.WriteAttribute(hw, group, raster, attrPrefix);
		}
	}
}
