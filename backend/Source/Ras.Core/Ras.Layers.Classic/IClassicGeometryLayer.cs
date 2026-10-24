namespace Ras.Layers.Classic;

public interface IClassicGeometryLayer : IFeatureLayer
{
	ClassicGeometry Geometry { get; }
}
