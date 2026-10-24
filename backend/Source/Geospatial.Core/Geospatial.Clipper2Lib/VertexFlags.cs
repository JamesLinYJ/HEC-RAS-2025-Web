using System;

namespace Geospatial.Clipper2Lib;

[Flags]
internal enum VertexFlags
{
	None = 0,
	OpenStart = 1,
	OpenEnd = 2,
	LocalMax = 4,
	LocalMin = 8
}
