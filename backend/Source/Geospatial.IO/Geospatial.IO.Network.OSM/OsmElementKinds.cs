using System;

namespace Geospatial.IO.Network.OSM;

[Flags]
public enum OsmElementKinds
{
	None = 0,
	Node = 1,
	Way = 2,
	Relation = 4,
	All = Node | Way | Relation
}
