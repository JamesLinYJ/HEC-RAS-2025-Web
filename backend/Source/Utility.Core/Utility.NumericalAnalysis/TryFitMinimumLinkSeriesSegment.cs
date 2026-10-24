namespace Utility.NumericalAnalysis;

public delegate bool TryFitMinimumLinkSeriesSegment<TFit>(int seriesIndex, int startIndex, int endIndex, out TFit fit);
