using System;
using System.Collections.Generic;
using Utility.Extensions;
using Utility.Progress;

namespace Geospatial.GDALAssist;

public class GdalRasterCollection : IDisposable
{
	public readonly Dictionary<string, GDALRaster> Rasters;

	private bool _disposedValue;

	public GdalRasterCollection()
	{
		Rasters = new Dictionary<string, GDALRaster>(StringComparer.OrdinalIgnoreCase);
	}

	public static GdalRasterCollection Open(IList<string> startingFiles, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		GdalRasterCollection gdalRasterCollection = new GdalRasterCollection();
		gdalRasterCollection.AddRange(startingFiles, prog);
		return gdalRasterCollection;
	}

	public GDALRaster Acquire(string filename)
	{
		lock (Rasters)
		{
			if (Rasters.TryGetValue(filename, out var rasters))
			{
				return rasters;
			}
			rasters = GDALRaster.TryOpen(filename);
			if (rasters == null)
			{
				return null;
			}
			Rasters[filename] = rasters;
			return rasters;
		}
	}

	public GDALRaster Acquire(DatasetIdentifier id)
	{
		lock (Rasters)
		{
			if (Rasters.TryGetValue(id.ConnectionString, out var rasters))
			{
				return rasters;
			}
			rasters = GDALRaster.TryOpen(id);
			if (rasters == null)
			{
				return null;
			}
			Rasters[id.ConnectionString] = rasters;
			return rasters;
		}
	}

	public void AddRange(IList<string> filenames, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		int count = filenames.Count;
		int ordinal = 0;
		foreach (string filename in filenames)
		{
			GDALRaster gDALRaster = GDALRaster.TryOpen(filename);
			lock (Rasters)
			{
				if (gDALRaster != null)
				{
					Rasters[filename] = gDALRaster;
				}
				else
				{
					prog.ReportWarning(filename.InDoubleQuotes() + " could not be opened.");
				}
				ordinal++;
				prog.ReportProgress((int)(100f * (float)ordinal / (float)count));
			}
		}
	}

	public void AddRange(IList<string> filenames, string driverShortName)
	{
		foreach (string filename in filenames)
		{
			GDALRaster rasters = new GDALRaster(filename, driverShortName);
			lock (Rasters)
			{
				Rasters[filename] = rasters;
			}
		}
	}

	public void Remove(string filename)
	{
		if (Rasters.TryGetValue(filename, out var localValue))
		{
			localValue.Dispose();
			Rasters.Remove(filename);
		}
	}

	public void RemoveRange(IEnumerable<string> filenames)
	{
		foreach (string filename in filenames)
		{
			Remove(filename);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposedValue)
		{
			return;
		}
		if (disposing)
		{
			foreach (KeyValuePair<string, GDALRaster> raster in Rasters)
			{
				raster.Value?.Dispose();
			}
		}
		_disposedValue = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
