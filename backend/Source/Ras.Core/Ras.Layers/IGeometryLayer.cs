namespace Ras.Layers;

public interface IGeometryLayer : IFeatureLayer
{
	Geometry Geometry { get; }
}
