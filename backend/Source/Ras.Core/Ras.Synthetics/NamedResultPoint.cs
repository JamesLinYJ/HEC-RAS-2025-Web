using Geospatial.Vectors;

namespace Ras.Synthetics;

public readonly record struct NamedResultPoint(Point Location, string Name);
