using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Hydraulics.StorageAreas;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics;

[DebuggerDisplay("{Name}")]
public class StorageArea : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private const StorageAreaComputeMethod DefaultComputeMethod = StorageAreaComputeMethod.ElevationVolumeCurve;

	private Polygon _polygon;

	private VersionTracker _tracker;

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	public long VersionXY => _tracker.GetVersion(_polygon);

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Polygon")]
	public Polygon Polygon
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

	public Extent Extent => Polygon?.Extent ?? Extent.NullExtent;

	[HashContributor]
	[Metadata("Mode")]
	public StorageAreaComputeMethod ComputeMethod { get; set; }

	[HashContributor]
	[Metadata("Avg Area")]
	public double Area { get; set; }

	[HashContributor]
	[Metadata("Min Elev")]
	public double MinimumElevation { get; set; }

	[HashContributor]
	[DatasetName("Volume Elevation")]
	public StationElevationProfile ElevationVolumeCurve { get; set; }

	[HashContributor]
	[Metadata("Elev Vol Count")]
	public int VolumeElevationCurveCount => ElevationVolumeCurve?.Count ?? 0;

	public StorageArea()
	{
	}

	public StorageArea(Polygon pgon)
	{
		Polygon = pgon;
		ComputeMethod = StorageAreaComputeMethod.ElevationVolumeCurve;
	}

	public StationElevationProfile ComputeVolumeElevationCurve(IRaster<float> raster)
	{
		if (!Polygon.IsValidPolygon(Polygon))
		{
			return null;
		}
		RasterDefinition queryRaster = RasterDefinition.ComputeBoundingRaster(Polygon.Extent, 10000);
		float[] resample = raster.Resample(new ResampleArgs(queryRaster, InterpolationMode.Linear));
		if (resample == null || resample.Length != queryRaster.CellCount)
		{
			return null;
		}
		throw new NotImplementedException();
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
