using System;
using Geospatial.Rasters.Convolution;
using Geospatial.Rendering.Systems;

namespace Geospatial.Systems.Shapes;

public interface IText : IShape, IDisposable
{
	ILabelDefinition Label { get; set; }

	Matrix3x3D DataTransform { get; set; }

	void DisposeInternalResources(bool disposing)
	{
	}
}
