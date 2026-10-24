using System.Collections.Generic;

namespace Geospatial.IO.Network.OSM;

public static class OsmPresets
{
	private readonly struct ClauseSpec
	{
		public OsmElementKinds Kinds { get; }

		public OsmTagFilter[] Filters { get; }

		public ClauseSpec(OsmElementKinds kinds, params OsmTagFilter[] filters)
		{
			Kinds = kinds;
			Filters = filters;
		}

		public OverpassClause Build()
		{
			return new OverpassClause(Kinds, Filters);
		}
	}

	public static IReadOnlyList<OsmPreset> All { get; } = BuildCatalog();

	private static IReadOnlyList<OsmPreset> BuildCatalog()
	{
		List<OsmPreset> osmPresets = new List<OsmPreset>();
		osmPresets.Add(Preset("Buildings", "Building footprints, for structure inventory and hydraulic obstructions.", "Buildings", OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("building", OsmTagOperator.Exists)));
		osmPresets.Add(Preset("Major Roads", "Motorways, trunk, primary, and secondary roads (and their link ramps) - key hydraulic barriers and evacuation routes.", "MajorRoads", OsmElementKinds.Way, new OsmTagFilter("highway", OsmTagOperator.OneOf, "motorway;trunk;primary;secondary;motorway_link;trunk_link;primary_link;secondary_link")));
		osmPresets.Add(Preset("All Roads", "Every tagged road, from motorways to footpaths. Can return a very large dataset over wide extents.", "AllRoads", OsmElementKinds.Way, new OsmTagFilter("highway", OsmTagOperator.Exists)));
		osmPresets.Add(Preset("Railways", "Rail, light rail, subway, tram, and narrow-gauge tracks.", "Railways", OsmElementKinds.Way, new OsmTagFilter("railway", OsmTagOperator.OneOf, "rail;light_rail;subway;tram;narrow_gauge")));
		osmPresets.Add(Preset("Bridges", "Structures tagged as bridges, spanning roads, railways, or waterways.", "Bridges", OsmElementKinds.Way, new OsmTagFilter("bridge", OsmTagOperator.Exists)));
		osmPresets.Add(Preset("Culverts", "Ways tagged as culverts, where a road, rail, or waterway passes underground.", "Culverts", OsmElementKinds.Way, new OsmTagFilter("tunnel", OsmTagOperator.Equals, "culvert")));
		osmPresets.Add(Preset("Dams & Weirs", "Dams and weirs, mapped either as crossing lines or as areas.", "DamsAndWeirs", OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("waterway", OsmTagOperator.OneOf, "dam;weir")));
		osmPresets.Add(Preset("Levees & Embankments", "Flood-control levees/dykes, and roads or rail built up on embankments.", "LeveesAndEmbankments", new ClauseSpec(OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("man_made", OsmTagOperator.OneOf, "dyke;embankment")), new ClauseSpec(OsmElementKinds.Way, new OsmTagFilter("embankment", OsmTagOperator.Equals, "yes"))));
		osmPresets.Add(Preset("Waterways", "River, stream, canal, drain, and ditch centerlines.", "Waterways", OsmElementKinds.Way, new OsmTagFilter("waterway", OsmTagOperator.OneOf, "river;stream;canal;drain;ditch")));
		osmPresets.Add(Preset("Water Bodies", "Lakes, ponds, and reservoirs mapped as areas.", "WaterBodies", new ClauseSpec(OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("natural", OsmTagOperator.Equals, "water")), new ClauseSpec(OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("landuse", OsmTagOperator.Equals, "reservoir"))));
		osmPresets.Add(Preset("Wetlands", "Marsh, swamp, and other wetland areas.", "Wetlands", OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("natural", OsmTagOperator.Equals, "wetland")));
		osmPresets.Add(Preset("Land Use", "Land use polygons, useful for assigning roughness values.", "LandUse", OsmElementKinds.Way | OsmElementKinds.Relation, new OsmTagFilter("landuse", OsmTagOperator.Exists)));
		osmPresets.Add(Preset("Administrative Boundaries", "Administrative boundary relations (countries, states, counties, municipalities).", "AdministrativeBoundaries", OsmElementKinds.Relation, new OsmTagFilter("boundary", OsmTagOperator.Equals, "administrative")));
		osmPresets.Add(Preset("Places", "City, town, village, and hamlet location points.", "Places", OsmElementKinds.Node, new OsmTagFilter("place", OsmTagOperator.OneOf, "city;town;village;hamlet")));
		return osmPresets;
	}

	private static OsmPreset Preset(string name, string description, string label, OsmElementKinds kinds, params OsmTagFilter[] filters)
	{
		return Preset(name, description, label, new ClauseSpec(kinds, filters));
	}

	private static OsmPreset Preset(string name, string description, string label, params ClauseSpec[] clauses)
	{
		return new OsmPreset(name, description, () =>
		{
			OverpassQuery overpassQuery = new OverpassQuery
			{
				Label = label
			};
			ClauseSpec[] clausesArray = clauses;
			foreach (ClauseSpec clauseSpec in clausesArray)
			{
				overpassQuery.Clauses.Add(clauseSpec.Build());
			}
			return overpassQuery;
		});
	}
}
