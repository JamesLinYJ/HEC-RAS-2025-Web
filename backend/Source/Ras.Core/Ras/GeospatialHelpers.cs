using System.IO;
using Geospatial.GDALAssist;
using H5Assist;
using Ras.Layers;
using Utility.Extensions;
using Utility.Units;

namespace Ras;

public static class GeospatialHelpers
{
	public static Projection TryIdentifyProjection(string filename)
	{
		try
		{
			string extension = Path.GetExtension(filename);
			if ((bool)Project.IsValidProjectFile(filename))
			{
				return Project.TryParseProjectionFrom(filename);
			}
			if (extension.RoughlyEquals(".prj"))
			{
				return new ESRIProjection(filename);
			}
			if (extension.RoughlyEquals(".h5"))
			{
				try
				{
					using H5Reader hr = new H5Reader(filename);
					Project.LoadProjectMetadataFromH5Header(hr, out string _, out Projection projection, out UnitSystem _);
					if (projection != null)
					{
						return projection;
					}
				}
				catch
				{
				}
			}
			try
			{
				using GDALRaster gDALRaster = new GDALRaster(filename);
				return gDALRaster.GetProjection();
			}
			catch
			{
			}
		}
		catch
		{
		}
		return null;
	}
}
