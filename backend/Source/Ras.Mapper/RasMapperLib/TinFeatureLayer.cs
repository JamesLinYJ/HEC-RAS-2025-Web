using System;
using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class TinFeatureLayer : FeatureLayer
{
	private const string sArea = "Area";

	private const string sPointCount = "Points";

	private const string sTriangleCount = "Triangles";

	private SpatialIndex<Tuple<int, int, int>> _spatialIndexTinPerimeters;

	public TinFeatureLayer()
		: base("TIN Layer")
	{
	}

	public TinFeatureLayer(string name)
		: base(name)
	{
	}

	public TinFeatureLayer(string name, string filename)
		: base(name, filename)
	{
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Points", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Triangles", typeof(int), _featureColumnGroup, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Area", typeof(double), _featureColumnGroup, DataColumnViewState.GrayedOut);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.AddRange(RasMapperLib.Tin.GetAvailablePlotOptions(SharedData.DebugMode));
		return staticPlotOptions;
	}

	public IEnumerable<Tin> Tins()
	{
		foreach (Tin tin in FeaturesOfType<Tin>())
		{
			yield return tin;
		}
	}

	public Tin Tin(int fid)
	{
		return (Tin)Feature(fid);
	}

	public Histogram SurfaceHistogram()
	{
		if (FeatureTable().Rows.Count == 0)
		{
			return null;
		}
		double minValue = double.MinValue;
		double maxValue = double.MaxValue;
		foreach (Tin tin in Tins())
		{
			if (SelectedPlotOptions["TIN Plot M Surface"])
			{
				if (tin.HasM)
				{
					if (tin.MaxM > minValue)
					{
						minValue = tin.MaxM;
					}
					if (tin.MinM < maxValue)
					{
						maxValue = tin.MinM;
					}
				}
			}
			else if (tin.HasZ)
			{
				if (tin.MaxZ > minValue)
				{
					minValue = tin.MaxZ;
				}
				if (tin.MinZ < maxValue)
				{
					maxValue = tin.MinZ;
				}
			}
		}
		if (maxValue < minValue)
		{
			return null;
		}
		Histogram histogram = new Histogram(maxValue, minValue);
		foreach (Tin tin2 in Tins())
		{
			if (SelectedPlotOptions["TIN Plot M Surface"])
			{
				histogram.AddSurfaceStatistics(tin2.SurfaceStatisticsM);
			}
			else
			{
				histogram.AddSurfaceStatistics(tin2.SurfaceStatisticsZ);
			}
		}
		return histogram;
	}

	private SpatialIndex<Tuple<int, int, int>> EnsureTinPerimeterSpatialIndex()
	{
		if (_spatialIndexTinPerimeters == null)
		{
			_spatialIndexTinPerimeters = SpatialIndex.GetPerimeterEdgeSpatialIndex(FeaturesOfType<Tin>().ToList());
		}
		return _spatialIndexTinPerimeters;
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		_spatialIndexTinPerimeters = null;
	}

	public SpatialIndex<Tuple<int, int, int>> GetTinPerimeterSpatialIndex()
	{
		return EnsureTinPerimeterSpatialIndex();
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(Tin) }.ToList();
	}

	protected override void ResetFeatureAttributes(int fid)
	{
		FeatureRow(fid)["Area"] = Tin(fid).Area;
		FeatureRow(fid)["Points"] = Tin(fid).Count;
		FeatureRow(fid)["Triangles"] = Tin(fid).TriangleCount();
	}
}
