using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface IVectorResampler<T> : IResample<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	bool IsIndependent { get; }

	IReadOnlyList<ComplexPolygon> PerimeterRing { get; }
}
