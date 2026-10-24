using System;

namespace Utility.NumericalAnalysis;

public static class MinimumLinkPolynomialSimplifier
{
	public static bool TryFind(EndpointPolynomialLinkCatalog catalog, double maximumNormalizedError, ReadOnlySpan<int> requiredKnotIndexes, out MinimumLinkResult<EndpointPolynomialFit<double>> result)
	{
		if (catalog == null)
		{
			throw new ArgumentNullException("catalog");
		}
		if (!double.IsFinite(maximumNormalizedError) || maximumNormalizedError < 0.0)
		{
			throw new ArgumentOutOfRangeException("maximumNormalizedError");
		}
		return MinimumLinkSimplifier.TryFind(catalog.PointCount, requiredKnotIndexes, (TryFitMinimumLinkSegment<EndpointPolynomialFit<double>>)TryFit, out result);
		bool TryFit(int start, int end, out EndpointPolynomialFit<double> fit)
		{
			fit = catalog.GetFit(start, end);
			return catalog.GetMaximumNormalizedError(start, end) <= maximumNormalizedError + 1E-12;
		}
	}

	public static bool TryFindShared(EndpointPolynomialLinkCatalog[] catalogs, double maximumNormalizedError, ReadOnlySpan<int> requiredKnotIndexes, out MinimumLinkMultiSeriesResult<EndpointPolynomialFit<double>> result)
	{
		if (catalogs == null || catalogs.Length == 0)
		{
			throw new ArgumentException("At least one catalog is required.", "catalogs");
		}
		int pointCount = (catalogs[0] ?? throw new ArgumentException("Catalogs cannot contain null.", "catalogs")).PointCount;
		for (int i = 1; i < catalogs.Length; i++)
		{
			if (catalogs[i] == null || catalogs[i].PointCount != pointCount)
			{
				throw new ArgumentException("All catalogs must contain the same number of points.", "catalogs");
			}
		}
		return MinimumLinkSimplifier.TryFindShared(pointCount, catalogs.Length, requiredKnotIndexes, (TryFitMinimumLinkSeriesSegment<EndpointPolynomialFit<double>>)TryFit, out result);
		bool TryFit(int series, int start, int end, out EndpointPolynomialFit<double> fit)
		{
			EndpointPolynomialLinkCatalog endpointPolynomialLinkCatalog = catalogs[series];
			fit = endpointPolynomialLinkCatalog.GetFit(start, end);
			return endpointPolynomialLinkCatalog.GetMaximumNormalizedError(start, end) <= maximumNormalizedError + 1E-12;
		}
	}
}
