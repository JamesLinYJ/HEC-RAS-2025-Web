using System;
using System.IO;
using Ras.Process.RasterCommands.Explorer;

namespace Ras.Process.RasterCommands;

internal static class RasterSourceFactory
{
	public static bool TryCreate(string filePath, RasterDriver driver, out IRasterMetadataSource source, out string error)
	{
		source = null;
		error = null;
		string fileName = Path.GetFileName(filePath);
		try
		{
			switch (driver)
			{
			case RasterDriver.Native:
				source = new NativeMetadataSource(filePath, fileName);
				return true;
			case RasterDriver.Gdal:
				source = new GdalMetadataSource(filePath, fileName);
				return true;
			default:
				try
				{
					source = new NativeMetadataSource(filePath, fileName);
					return true;
				}
				catch
				{
					source = new GdalMetadataSource(filePath, fileName);
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}
}
