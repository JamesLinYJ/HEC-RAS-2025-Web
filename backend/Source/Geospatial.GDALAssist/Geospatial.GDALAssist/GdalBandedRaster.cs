using System;
using Geospatial.Rasters;
using Geospatial.Vectors;
using OSGeo.GDAL;
using Utility.Memory;

namespace Geospatial.GDALAssist;

public class GdalBandedRaster<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly Pool<GDALRaster> _gdrs;

	private readonly GDALRaster _gdr;

	private readonly bool _openOnDemand;

	private readonly string _gdalConnectionString;

	private readonly object _parallelLock = new object();

	public bool AllowParallelResampling { get; set; } = true;

	public int ProfileCount { get; private set; }

	public Extent ValidExtent { get; private set; }

	public T? NoDataValue { get; private set; }

	public RasterDefinition RasterDefinition { get; private set; }

	public static GdalBandedRaster<T> Pooled(string connectionString, out Pool<GDALRaster> poolResources)
	{
		poolResources = new Pool<GDALRaster>(() => new GDALRaster(connectionString, Access.GA_ReadOnly, useShared: false));
		return new GdalBandedRaster<T>(poolResources);
	}

	public GdalBandedRaster(GDALRaster gdr)
	{
		_gdr = gdr;
		_openOnDemand = false;
		_gdalConnectionString = string.Empty;
		InitFrom(_gdr);
	}

	private GdalBandedRaster(Pool<GDALRaster> gdrs)
	{
		_gdrs = gdrs;
		_openOnDemand = false;
		_gdalConnectionString = string.Empty;
		using PoolHandle<GDALRaster> poolHandle = gdrs.Acquire();
		InitFrom(poolHandle.Data);
	}

	public GdalBandedRaster(string filename)
	{
		_gdalConnectionString = filename;
		_openOnDemand = true;
		using GDALRaster gdr = new GDALRaster(filename);
		InitFrom(gdr);
	}

	private void UseGDR(Action<GDALRaster> action)
	{
		if (_openOnDemand)
		{
			using (GDALRaster gdalConnectionString = new GDALRaster(_gdalConnectionString))
			{
				action(gdalConnectionString);
				return;
			}
		}
		if (_gdr == null)
		{
			using (PoolHandle<GDALRaster> poolHandle = _gdrs.Acquire())
			{
				action(poolHandle.Data);
				return;
			}
		}
		action(_gdr);
	}

	private void InitFrom(GDALRaster gdr)
	{
		ProfileCount = gdr.GetBandCount();
		NoDataValue = gdr.TryGetNoData<T>(1, out T noDataValue) ? noDataValue : null;
		RasterDefinition = gdr.GetNativeRaster();
		ValidExtent = RasterDefinition.Extent;
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		if (AllowParallelResampling)
		{
			UseGDR((GDALRaster gdr) =>
			{
				ResampleInternal(gdr, args, bandIndex, outData);
			});
			return;
		}
		lock (_parallelLock)
		{
			UseGDR((GDALRaster gdr) =>
			{
				ResampleInternal(gdr, args, bandIndex, outData);
			});
		}
	}

	private void ResampleInternal(GDALRaster gdr, ResampleArgs args, int bandIndex, Memory<T> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		gdr.ReadBand(bandIndex + 1, args.Raster, outData, out var _);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] result)
	{
		if (AllowParallelResampling)
		{
			UseGDR((GDALRaster gdr) =>
			{
				gdr.ReadPoints(points, bandStart + 1, bandCount, result);
			});
			return;
		}
		lock (_parallelLock)
		{
			UseGDR((GDALRaster gdr) =>
			{
				gdr.ReadPoints(points, bandStart + 1, bandCount, result);
			});
		}
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> result)
	{
		if (AllowParallelResampling)
		{
			UseGDR((GDALRaster gdr) =>
			{
				gdr.ReadPoints(points, profileIndex + 1, 1, new Memory<T>[1] { result });
			});
			return;
		}
		lock (_parallelLock)
		{
			UseGDR((GDALRaster gdr) =>
			{
				gdr.ReadPoints(points, profileIndex + 1, 1, new Memory<T>[1] { result });
			});
		}
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		UseGDR((GDALRaster gdr) =>
		{
			for (int i = startBandIndex; i < startBandIndex + bandCount; i++)
			{
				ResampleInternal(gdr, args, i, outData[i - startBandIndex]);
			}
		});
	}

	public void DebugRefresh()
	{
		_gdr.DebugRefresh();
	}
}
