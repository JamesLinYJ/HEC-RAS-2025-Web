namespace Geospatial.Vectors;

public static class PolygonFilters
{
	public static readonly PolygonFilter None = (Polygon p, double _) => p;

	public static readonly PolygonFilter RadialDistance = (Polygon p, double prec) => p.CloneFilteredRadialDistance(prec * 0.5);

	public static readonly PolygonFilter CornerKeeper = (Polygon p, double prec) =>
	{
		double scaledPrec = prec * 0.5;
		return p.CloneFilteredCornerKeeper(scaledPrec, scaledPrec * scaledPrec);
	};

	public static readonly PolygonFilter GridQuantization = (Polygon p, double prec) => p.CloneFilteredGridQuantization(prec * 0.5);

	public static readonly PolygonFilter DouglasPeucker = (Polygon p, double prec) => p.CloneFiltered(prec * 0.5);

	public static PolygonFilter Default = CornerKeeper;
}
