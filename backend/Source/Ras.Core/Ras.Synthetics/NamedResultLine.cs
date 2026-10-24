using Geospatial.Vectors;

namespace Ras.Synthetics;

public readonly record struct NamedResultLine(Polyline Line, string Name);
