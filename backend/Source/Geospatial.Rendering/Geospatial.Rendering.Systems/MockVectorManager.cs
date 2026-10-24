using System;
using Geospatial.Rasters.Convolution;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockVectorManager : VectorManager<MockCanvas, MockShape, MockContiguousLineCollection, MockContiguousComplexPolygonCollection, MockSegment, MockContiguousLineCollection, MockContiguousEllipseCollection, MockText, MockVectorGroup>
{
	public MockVectorManager()
		: base(new MockCanvas(100.0, 100.0), (Func<Extent>)(() => Extent.EffectivelyInfinite), Matrix3x3D.Identity)
	{
	}
}
