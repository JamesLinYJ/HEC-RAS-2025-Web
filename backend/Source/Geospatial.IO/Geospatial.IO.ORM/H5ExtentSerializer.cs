using System;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.ORM;

namespace Geospatial.IO.ORM;

public class H5ExtentSerializer : H5Serializer
{
	public override object Read(H5Reader hr, string group, string attrPrefix)
	{
		return HDF5VectorReader.TryReadExtent(hr, group, attrPrefix);
	}

	public override void Write(H5Writer hw, string group, string attrPrefix, object o)
	{
		if (o is Extent ext)
		{
			HDF5VectorReader.WriteAttribute(hw, group, ext, attrPrefix);
			return;
		}
		throw new Exception("Object not of type Extent");
	}
}
