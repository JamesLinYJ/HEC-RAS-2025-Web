using System;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.Editing;

public sealed class ExtentSelector
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ExtentSelector");

	private VectorID _previewBackdropId;

	private VectorID _previewStrokeId;

	private readonly Polygon _polygon;

	private readonly IVectorManager _vectorManager;

	private Point _startPt;

	private bool _isActive;

	private readonly Action<Extent, SelectMode> _select;

	public bool IsActive => _isActive;

	public ExtentSelector(IVectorManager vectorManager, Action<Extent, SelectMode> select)
	{
		_polygon = new Polygon(new Point[4]);
		_vectorManager = vectorManager ?? throw new ArgumentNullException("vectorManager");
		_select = select ?? throw new ArgumentNullException("select");
	}

	public void Begin(Point startPt)
	{
		if (_isActive)
		{
			_logger.LogError("Beginning extent selection when it's already active.");
		}
		_startPt = startPt;
		_isActive = true;
	}

	public void Quit(Point currentPt)
	{
		if (!_isActive)
		{
			_logger.LogError("Finishing extent selection when it's not active.");
			return;
		}
		_isActive = false;
		Clear();
	}

	public void End(Point currentPt, SelectMode mode)
	{
		Quit(currentPt);
		_select?.Invoke(Extent.FromUnsortedPoints(_startPt, currentPt), mode);
	}

	public void Clear()
	{
		_vectorManager.TryDeleteVector(ref _previewBackdropId);
		_vectorManager.TryDeleteVector(ref _previewStrokeId);
	}

	public void Render(Point currentPt)
	{
		if (IsActive)
		{
			Extent extent = Extent.FromUnsortedPoints(_startPt, currentPt);
			RenderExtentSelection(_vectorManager, _polygon, extent.UpperLeft, extent.LowerRight, ref _previewBackdropId, ref _previewStrokeId);
		}
	}

	public static void RenderExtentSelection(IVectorManager vectorManager, Polygon renderPolygon, Point topLeft, Point bottomRight, ref VectorID backdropId, ref VectorID strokeId)
	{
		double bottomRightTopLeftTerm = bottomRight.X - topLeft.X;
		double bottomRightTopLeftTerm2 = bottomRight.Y - topLeft.Y;
		renderPolygon.Points[0] = new Point(topLeft.X, topLeft.Y);
		renderPolygon.Points[1] = new Point(topLeft.X, topLeft.Y + bottomRightTopLeftTerm2);
		renderPolygon.Points[2] = new Point(topLeft.X + bottomRightTopLeftTerm, topLeft.Y + bottomRightTopLeftTerm2);
		renderPolygon.Points[3] = new Point(topLeft.X + bottomRightTopLeftTerm, topLeft.Y);
		vectorManager.AddOrUpdateVector(ref backdropId, renderPolygon, DefaultEditSymbology.MarqueeBackdropSymbology);
		vectorManager.AddOrUpdateVector(ref strokeId, renderPolygon, DefaultEditSymbology.MarqueeStrokeSymbology);
	}
}
