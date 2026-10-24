using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class StitchFeature : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private ComplexPolygon _polygon;

	private VersionTracker _tracker;

	public long VersionXY => _tracker.GetVersion(_polygon);

	[HashContributor]
	[DatasetName("Polygon")]
	public ComplexPolygon Polygon
	{
		get
		{
			return _polygon;
		}
		set
		{
			_tracker.Assign(ref _polygon, value);
		}
	}

	[HashContributor]
	[DatasetName("Spacing")]
	public double SampleSpacing { get; set; }

	public Extent Extent => _polygon.Extent;

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public StitchFeature()
	{
		Polygon = null;
		SampleSpacing = 0.0;
	}

	public StitchFeature(ComplexPolygon polygon, double sampleSpacing)
	{
		_polygon = polygon;
		SampleSpacing = sampleSpacing;
	}
}
