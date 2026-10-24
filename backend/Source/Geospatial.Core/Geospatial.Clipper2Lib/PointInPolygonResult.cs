using System;

namespace Geospatial.Clipper2Lib;

[Flags]
public enum PointInPolygonResult
{
	IsOn = 0,
	IsInside = 1,
	IsOutside = 2
}
