using System;
using Geospatial.Rasters;

namespace Geospatial.IO;

public class RasterDriver
{
	public string UniqueID;

	public string FriendlyName;

	public string Description;

	public Func<string, IResample<float>> Instantiate;
}
