namespace Geospatial.Clipper2Lib;

internal readonly struct IntersectNode(Point64 pt, Active edge1, Active edge2)
{
	public readonly Point64 pt = pt;

	public readonly Active edge1 = edge1;

	public readonly Active edge2 = edge2;
}
