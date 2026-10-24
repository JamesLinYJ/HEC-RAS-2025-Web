namespace Geospatial.Rendering.Editing;

public readonly struct SplitPreviewSymbology
{
	public VectorSymbology FirstHalfBackground { get; init; }

	public VectorSymbology FirstHalfForeground { get; init; }

	public VectorSymbology SecondHalfBackground { get; init; }

	public VectorSymbology SecondHalfForeground { get; init; }

	public VectorSymbology SplitPoint { get; init; }

	public static SplitPreviewSymbology Polyline => new SplitPreviewSymbology
	{
		FirstHalfBackground = DefaultEditSymbology.PolylineSplitFirstHalfBackgroundSymbology,
		FirstHalfForeground = DefaultEditSymbology.PolylineSplitFirstHalfForegroundSymbology,
		SecondHalfBackground = DefaultEditSymbology.PolylineSplitSecondHalfBackgroundSymbology,
		SecondHalfForeground = DefaultEditSymbology.PolylineSplitSecondHalfForegroundSymbology,
		SplitPoint = DefaultEditSymbology.AppendPointSymbology
	};

	public static SplitPreviewSymbology Topology => new SplitPreviewSymbology
	{
		FirstHalfBackground = DefaultEditSymbology.TopologySplitArcSegmentBackgroundSymbology,
		FirstHalfForeground = DefaultEditSymbology.TopologySplitArcSegmentSymbology,
		SecondHalfBackground = DefaultEditSymbology.TopologySplitArcSegmentBackgroundSymbology,
		SecondHalfForeground = DefaultEditSymbology.TopologySplitArcSegmentSymbology,
		SplitPoint = DefaultEditSymbology.TopologyAddNodeSymbology
	};
}
