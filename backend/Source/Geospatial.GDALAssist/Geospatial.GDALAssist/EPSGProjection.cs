using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.GDALAssist;

public class EPSGProjection : Projection
{
	private static readonly ILogger _logger = LogHelper.GetLogger("EPSGProjection");

	private static readonly ThreadLocal<Dictionary<int, EPSGProjection>> _cache = new ThreadLocal<Dictionary<int, EPSGProjection>>(() => new Dictionary<int, EPSGProjection>());

	public EPSGProjection(int epsgCode)
	{
		_srs.ImportFromEPSG(epsgCode);
		if (Projection.UseGlobalStaticSRSList)
		{
			Projection.GlobalSRSLookup(ref _srs);
		}
		_cmdLineProjection = "EPSG:" + epsgCode;
	}

	public static Projection GetOrMake(int epsgCode)
	{
		Dictionary<int, EPSGProjection> cacheTerm = _cache.Value;
		if (cacheTerm.TryGetValue(epsgCode, out var localValue))
		{
			return localValue;
		}
		EPSGProjection ePSGProjection = null;
		try
		{
			ePSGProjection = new EPSGProjection(epsgCode);
		}
		catch (Exception exception)
		{
			_logger.LogTrace(exception, "Could not create projection for EPSG:{Code}", epsgCode);
		}
		cacheTerm[epsgCode] = ePSGProjection;
		return ePSGProjection;
	}
}
