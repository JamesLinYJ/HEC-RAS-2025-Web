using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics.Culverts;

namespace Ras.Observables;

public sealed class ObservableCulvertBarrel : ObservableObject, IObservableFeature<CulvertBarrel>
{
	private readonly CulvertBarrel _barrel;

	public CulvertBarrel BaseFeature => _barrel;

	public string Name
	{
		get
		{
			return _barrel.Name;
		}
		set
		{
			SetProperty(_barrel.Name, value, _barrel, (CulvertBarrel b, string v) =>
			{
				b.Name = v;
			}, "Name");
		}
	}

	public Polyline Polyline
	{
		get
		{
			return _barrel.Polyline;
		}
		set
		{
			SetProperty(_barrel.Polyline, value, _barrel, (CulvertBarrel b, Polyline v) =>
			{
				b.Polyline = v;
			}, "Polyline");
		}
	}

	public string BarrelPropertyName
	{
		get
		{
			return _barrel.BarrelPropertyName;
		}
		set
		{
			SetProperty(_barrel.BarrelPropertyName, value, _barrel, (CulvertBarrel b, string v) =>
			{
				b.BarrelPropertyName = v;
			}, "BarrelPropertyName");
		}
	}

	public double UpstreamInvert
	{
		get
		{
			return _barrel.UpstreamInvert;
		}
		set
		{
			SetProperty(_barrel.UpstreamInvert, value, _barrel, (CulvertBarrel b, double v) =>
			{
				b.UpstreamInvert = v;
			}, "UpstreamInvert");
		}
	}

	public double DownstreamInvert
	{
		get
		{
			return _barrel.DownstreamInvert;
		}
		set
		{
			SetProperty(_barrel.DownstreamInvert, value, _barrel, (CulvertBarrel b, double v) =>
			{
				b.DownstreamInvert = v;
			}, "DownstreamInvert");
		}
	}

	public string UpstreamOpeningName
	{
		get
		{
			return _barrel.UpstreamOpeningName;
		}
		set
		{
			SetProperty(_barrel.UpstreamOpeningName, value, _barrel, (CulvertBarrel b, string v) =>
			{
				b.UpstreamOpeningName = v;
			}, "UpstreamOpeningName");
		}
	}

	public string DownstreamOpeningName
	{
		get
		{
			return _barrel.DownstreamOpeningName;
		}
		set
		{
			SetProperty(_barrel.DownstreamOpeningName, value, _barrel, (CulvertBarrel b, string v) =>
			{
				b.DownstreamOpeningName = v;
			}, "DownstreamOpeningName");
		}
	}

	public Point UpstreamPoint => _barrel.UpstreamPoint;

	public Point DownstreamPoint => _barrel.DownstreamPoint;

	public double Length => _barrel.Length;

	public Extent Extent => _barrel.Extent;

	public BarrelPropertyTables Tables
	{
		get
		{
			return _barrel.Tables;
		}
		set
		{
			SetProperty(_barrel.Tables, value, _barrel, (CulvertBarrel b, BarrelPropertyTables v) =>
			{
				b.Tables = v;
			}, "Tables");
		}
	}

	public ObservableCulvertBarrel(CulvertBarrel barrel)
	{
		_barrel = barrel;
	}
}
