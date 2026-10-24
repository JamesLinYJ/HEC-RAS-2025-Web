using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface ITileCache
{
	IDisposable Pin(Extent ext);
}
