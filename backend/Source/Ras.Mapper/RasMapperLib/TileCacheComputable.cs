using System;
using System.Runtime.InteropServices;
using Utility.Progress;

namespace RasMapperLib;

[ComVisible(false)]
public class TileCacheComputable
{
	private readonly RASResultsMap _map;

	private readonly TerrainLayer _terrain;

	private readonly TileCacheComputeOptions _cacheOptions;

	public static void Compute(RASResultsMap map, TileCacheComputeOptions cacheOptions, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		new TileCacheComputable(map, cacheOptions).Run(reporter);
	}

	public static void Compute(TerrainLayer terrain, TileCacheComputeOptions cacheOptions, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		new TileCacheComputable(terrain, cacheOptions).Run(reporter);
	}

	private TileCacheComputable(RASResultsMap map, TileCacheComputeOptions cacheOptions)
	{
		_cacheOptions = cacheOptions;
		_map = map;
		_terrain = null;
	}

	private TileCacheComputable(TerrainLayer terrain, TileCacheComputeOptions cacheOptions)
	{
		_cacheOptions = cacheOptions;
		_terrain = terrain;
		_map = null;
	}

	private void Run(ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		reporter.ReportMessage("Creating RAS Tile Cache '" + _cacheOptions.DatasetIdentifier + "'");
		throw new NotImplementedException();
	}
}
