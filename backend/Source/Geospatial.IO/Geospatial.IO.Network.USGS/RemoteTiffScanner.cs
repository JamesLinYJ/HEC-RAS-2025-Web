using System;
using System.IO;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using TiffBinaryReader;
using Utility.Logging;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public static class RemoteTiffScanner
{
	public static async Task<TiffScanResult> ScanAsync(string url)
	{
		try
		{
			var (fileStreams, len) = await HttpRangeStream.CreatePoolAsync(SharedWebResourceManager.Client, url);
			using TiffFile source = await TiffFile.CreateFromStreamPoolAsync(fileStreams, new TiffReadConfigs
			{
				ParsePyramidLevels = false
			});
			string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".tif");
			try
			{
				TiffFile.WriteMetadataOnly(source, path);
				using (new GDALRaster(path))
				{
					Projection projection = GDALRaster.GetProjection(path);
					RasterDefinition nativeRaster = GDALRaster.GetNativeRaster(path);
					string projectionWkt = projection?.GetWKT() ?? "";
					if (string.IsNullOrEmpty(projectionWkt))
					{
						return new TiffScanResult
						{
							Url = url,
							FileSize = len,
							Result = OperationResult.Fail("Could not parse projection."),
							NativeRaster = nativeRaster,
							ProjectionWkt = projectionWkt
						};
					}
					if (!nativeRaster.IsValid())
					{
						return new TiffScanResult
						{
							Url = url,
							FileSize = len,
							Result = OperationResult.Fail("Could not parse extents."),
							NativeRaster = nativeRaster,
							ProjectionWkt = projectionWkt
						};
					}
					return new TiffScanResult
					{
						Url = url,
						FileSize = len,
						Result = true,
						NativeRaster = nativeRaster,
						ProjectionWkt = projectionWkt
					};
				}
			}
			finally
			{
				try
				{
					File.Delete(path);
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			return new TiffScanResult
			{
				Url = url,
				Result = OperationResult.Fail(GeneralMessageCodes.GetExceptionOccurred("Scanning URL", ex))
			};
		}
	}
}
