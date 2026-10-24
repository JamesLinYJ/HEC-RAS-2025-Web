using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public interface IRasterLayer<T> where T : struct
{
	bool Resample(RasterM raster, ref T[] values);
}
