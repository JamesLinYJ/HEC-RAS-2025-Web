namespace Utility.NumericalAnalysis;

public delegate bool TryFitMinimumLinkSegment<TFit>(int startIndex, int endIndex, out TFit fit);
