using System.Linq;
using System.Threading;
using Geospatial;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Ras.Layers;

public abstract class FeatureLayerBase<T> : TableLayerBase<T> where T : IFeatureRow, new()
{
	private static readonly ILogger _logger = LogHelper.GetLogger("FeatureLayerBase");

	private readonly Versioned<Extent> _extCache = new Versioned<Extent>();

	private readonly ReaderWriterLock _lock = new ReaderWriterLock();

	public Extent Extent => _extCache.Get(VersionXY, this, ComputeExtent);

	public ReaderWriterLock Lock => _lock;

	private static Extent ComputeExtent(FeatureLayerBase<T> flb)
	{
		return flb.ComputeExtent();
	}

	private Extent ComputeExtent()
	{
		return Extent.Union(this.Select((T cs) => cs.Extent));
	}
}
