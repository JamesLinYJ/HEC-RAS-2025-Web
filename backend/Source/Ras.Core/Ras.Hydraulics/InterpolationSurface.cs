using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics;

[DebuggerDisplay("{USXS} to {DSXS}")]
public class InterpolationSurface : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private VersionTracker _tracker;

	private Tin _tin;

	[HashContributor]
	[Metadata("US XS")]
	public int USXS { get; set; }

	[HashContributor]
	[Metadata("DS XS")]
	public int DSXS { get; set; }

	[HashContributor]
	[DatasetName("TIN")]
	public Tin Tin
	{
		get
		{
			return _tin;
		}
		set
		{
			_tracker.Assign(ref _tin, value);
		}
	}

	public Extent Extent => Tin?.Extent ?? Extent.NullExtent;

	public long VersionXY => _tracker.GetVersion(_tin);

	public InterpolationSurface()
	{
	}

	public InterpolationSurface(Tin tin, int usxs, int dsxs)
	{
		Tin = tin;
		USXS = usxs;
		DSXS = dsxs;
	}

	public Tin GenerateVelocitySurfaceTin(float[] upstreamStations, float[] downstreamStations)
	{
		throw new NotImplementedException();
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
