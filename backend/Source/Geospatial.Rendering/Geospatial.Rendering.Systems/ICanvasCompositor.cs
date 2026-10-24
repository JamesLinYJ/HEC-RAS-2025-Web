using System;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public interface ICanvasCompositor : IDisposable
{
	IVectorManager CreateVectorManager(ICanvas canvas, Func<Extent> getScreenExtent);

	ICanvas CreateCanvas(int layerIdx, bool requiresDXAxisFlip);

	IImageProvider CreateImageProvider();

	IBitmapProvider CreateBitmapProvider();
}
