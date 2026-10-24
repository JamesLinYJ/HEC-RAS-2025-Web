using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Vectors;
using Ras.Hydraulics;
using Utility.Extensions;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Rivers")]
public class RiverLayer : ClassicGeometryLayerBase<River>
{
	public static readonly string HDF5GroupName = "/Geometry/River Centerlines";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => HDF5GroupName;

	internal RiverLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<River>(this, (River riv) => riv.Polyline, (Polyline pl) => new River("", "", pl));
		}
		return _plWrapper;
	}

	public List<string> GetRiverNames()
	{
		return Features.Select((River r) => r.RiverName).Distinct().ToList();
	}

	public List<River> GetReachesOnRiver(string riverName)
	{
		return Features.Where((River r) => r.RiverName == riverName).ToList();
	}

	public List<CrossSection> GetCrossSectionsUpstreamToDownstream(int rivIdx)
	{
		River river = base[rivIdx];
		return GetCrossSectionsUpstreamToDownstream(river.RiverName, river.ReachName);
	}

	public List<CrossSection> GetCrossSectionsUpstreamToDownstream(string riverName, string reachName)
	{
		List<CrossSection> crossSections = new List<CrossSection>();
		CrossSectionLayer crossSectionLayer = Geometry.CrossSectionLayer;
		for (int i = 0; i < crossSectionLayer.Count; i++)
		{
			CrossSection crossSection = crossSectionLayer[i];
			if (crossSection.RiverName.RoughlyEquals(riverName) && crossSection.ReachName.RoughlyEquals(reachName))
			{
				crossSections.Add(crossSection);
			}
		}
		return crossSections.OrderByDescending((CrossSection xs) => CrossSection.TryParseRiverStationNumeric(xs.RiverStation)).ToList();
	}

	public List<CrossSection> GetCrossSectionsUpstreamToDownstream(string riverName)
	{
		List<CrossSection> crossSections = new List<CrossSection>();
		IReadOnlyList<CrossSection> features = Geometry.CrossSectionLayer.Features;
		for (int i = 0; i < features.Count; i++)
		{
			CrossSection crossSection = features[i];
			if (crossSection.RiverName.RoughlyEquals(riverName))
			{
				crossSections.Add(crossSection);
			}
		}
		return crossSections.OrderByDescending((CrossSection xs) => xs.RiverStation).ToList();
	}

	public void Rename(int fid, string newRiverName, string newReachName)
	{
		throw new NotImplementedException();
	}

	public void Add(string riverName, string reachName, Polyline polyline)
	{
		River river = new River(riverName, reachName, polyline);
		Add(river);
	}

	public void Insert(int index, string riverName, string reachName, Polyline polyline)
	{
		River river = new River(riverName, reachName, polyline);
		Insert(index, river);
	}
}
