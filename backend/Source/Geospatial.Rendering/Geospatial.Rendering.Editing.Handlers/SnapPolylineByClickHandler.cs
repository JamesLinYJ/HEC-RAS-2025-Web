using System;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing.Handlers;

public sealed class SnapPolylineByClickHandler : PolylineTransformHandler
{
	public static class Options
	{
		public static readonly OptionKey<double> SampleSpacing = OptionKey<double>.Define("Polyline.Snap.SampleSpacing", "Sample Spacing", "Distance between sample points (in map units). Smaller = more precise but slower.", 3.0);

		public static readonly OptionKey<PolylineSnapSearchMode> SearchMode = OptionKey<PolylineSnapSearchMode>.Define("Polyline.Snap.SearchMode", "Search Mode", "Perpendicular: search across the line direction. Radial: search in all directions.", PolylineSnapSearchMode.Perpendicular);

		public static readonly OptionKey<PolylineSnapElevationMode> ElevationMode = OptionKey<PolylineSnapElevationMode>.Define("Polyline.Snap.ElevationMode", "Elevation Mode", "Snap to the highest or lowest sampled elevation.", PolylineSnapElevationMode.Highest);

		public static readonly OptionKey<int> PointsPerRing = OptionKey<int>.Define("Polyline.Snap.PointsPerRing", "Points Per Ring", "Sample points per ring in radial search. Only used when Search Mode = Radial.", 16);

		public static readonly OptionKey<int> Iterations = OptionKey<int>.Define("Polyline.Snap.Iterations", "Iterations", "Number of times to run the snap pass for progressive refinement.", 1);
	}

	private readonly Func<IResample<float>> _getRaster;

	private VectorID _hoverHighlightId;

	public SnapPolylineByClickHandler(IPolylineCollection polylines, Func<IResample<float>> getRaster)
		: base(polylines, "polyline_snap_preview", "Snap polyline")
	{
		_getRaster = getRaster;
	}

	protected override Polyline TransformPolyline(Polyline src, EditContext ctx, RasterDefinition window)
	{
		IResample<float> resample = _getRaster?.Invoke();
		if (resample == null)
		{
			return null;
		}
		PolylineSnapOptions options = ReadOptions(ctx, window.CellSize);
		return PolylineSnapHelpers.SnapPolyline(src, resample, options);
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		if (!TryHit(hit, out var fid))
		{
			return false;
		}
		ctx.Preview.TryGetVector(PreviewChannel, out _hoverHighlightId);
		ctx.VectorManager.AddOrUpdateVector(ref _hoverHighlightId, Polylines[fid], DefaultEditSymbology.OpenSymbology);
		ctx.Preview.RegisterVector(PreviewChannel, _hoverHighlightId);
		return true;
	}

	internal static PolylineSnapOptions ReadOptions(EditContext ctx, double cellSize)
	{
		return new PolylineSnapOptions
		{
			SearchRadius = SnapTolerance.EffectiveRadius(ctx, cellSize),
			SampleSpacing = ctx.Options.Get(Options.SampleSpacing),
			SearchMode = ctx.Options.Get(Options.SearchMode),
			ElevationMode = ctx.Options.Get(Options.ElevationMode),
			PointsPerRing = ctx.Options.Get(Options.PointsPerRing),
			Iterations = ctx.Options.Get(Options.Iterations)
		};
	}
}
