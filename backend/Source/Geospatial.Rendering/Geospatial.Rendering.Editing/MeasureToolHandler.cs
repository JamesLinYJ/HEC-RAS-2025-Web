using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.Rendering.Helpers;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rendering.Editing;

public sealed class MeasureToolHandler : IEditHandler
{
	private static readonly TextVectorSymbology DynamicTextSymbology = CreateTextSymbology();

	private static readonly TextVectorSymbology PersistentTextSymbology = CreateTextSymbology();

	private readonly IVectorManager _vectorManager;

	private MeasureMode _mode;

	private readonly MeasureSlopeDelegate _getSlope;

	private readonly List<Geospatial.Vectors.Point> _points = new List<Geospatial.Vectors.Point>();

	private const string _previewLineLabel = "Measure_PreviewLine";

	private const string _previewPointLabel = "Measure_PreviewPoint";

	private const string _previewPolygonLabel = "Measure_PreviewPolygon";

	private const string _previewTextLabel = "Measure_PreviewText";

	private const string _persistentLineLabel = "Measure_PersistentLine";

	private const string _persistentPolygonLabel = "Measure_PersistentPolygon";

	private const string _persistentTextLabel = "Measure_PersistentText";

	private bool _isMeasuring;

	public MeasureMode Mode
	{
		get
		{
			return _mode;
		}
		set
		{
			_mode = value;
		}
	}

	public MeasureToolHandler(IVectorManager vectorManager, MeasureMode mode, MeasureSlopeDelegate getSlope)
	{
		_vectorManager = vectorManager;
		_mode = mode;
		_getSlope = getSlope;
	}

	public void Reset()
	{
		_points.Clear();
		_isMeasuring = false;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		switch (cmd.Command)
		{
		case CommandType.Click:
		case CommandType.BeginDragging:
			if (!_isMeasuring)
			{
				BeginMeasurement(cmd.Location.QueryPoint);
			}
			else
			{
				_points.Add(cmd.Location.QueryPoint);
			}
			return HandlerResult.WasHandled;
		case CommandType.DoubleClick:
			if (_isMeasuring)
			{
				FinalizeMeasurement(cmd.Location.QueryPoint, ctx.Preview);
				return HandlerResult.WasHandled;
			}
			break;
		case CommandType.Close:
			if (_isMeasuring)
			{
				FinalizeMeasurement(cmd.Location?.QueryPoint ?? Geospatial.Vectors.Point.NullPoint, ctx.Preview);
			}
			return HandlerResult.WasHandled;
		}
		return HandlerResult.NotHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (!_isMeasuring)
		{
			ctx.Preview.Touch("Measure_PersistentText");
			ctx.Preview.Touch("Measure_PersistentPolygon");
			ctx.Preview.Touch("Measure_PersistentLine");
			if (hit == null)
			{
				return true;
			}
			RenderPreviewPoint(hit.QueryPoint, ctx.Preview);
			return true;
		}
		if (hit == null)
		{
			return true;
		}
		RenderPreviewGeometry(hit.QueryPoint, ctx.Preview);
		RenderPreviewLabel(hit.QueryPoint, ctx.Preview);
		return true;
	}

	private void BeginMeasurement(Geospatial.Vectors.Point anchor)
	{
		_points.Clear();
		_points.Add(anchor);
		_isMeasuring = true;
	}

	private void FinalizeMeasurement(Geospatial.Vectors.Point closePoint, OverlayChannels overlay)
	{
		if (!_isMeasuring)
		{
			return;
		}
		if (_points.Count == 1)
		{
			if (closePoint.IsNull())
			{
				_points.Clear();
				_isMeasuring = false;
				return;
			}
			_points.Add(closePoint);
		}
		RenderPersistentGeometry(closePoint, overlay);
		RenderPersistentLabel(closePoint, overlay);
		_points.Clear();
		_isMeasuring = false;
	}

	private void RenderPreviewPoint(Geospatial.Vectors.Point location, OverlayChannels overlay)
	{
		overlay.TryGetVector("Measure_PreviewPoint", out var id);
		_vectorManager.AddOrUpdateVector(ref id, location, DefaultEditSymbology.AppendPointSymbology);
		overlay.RegisterVector("Measure_PreviewPoint", id);
	}

	private void RenderPreviewGeometry(Geospatial.Vectors.Point cursorLocation, OverlayChannels overlay)
	{
		List<Geospatial.Vectors.Point> points = new List<Geospatial.Vectors.Point>(_points) { cursorLocation };
		overlay.TryGetVector("Measure_PreviewLine", out var id);
		_vectorManager.AddOrUpdateVector(ref id, points, DefaultEditSymbology.SelectLineSymbology, VectorType.Line);
		overlay.RegisterVector("Measure_PreviewLine", id);
		if (IsAreaMode())
		{
			Polygon polygon = BuildPolygon(points);
			if (polygon != null && polygon.Count > 2)
			{
				overlay.TryGetVector("Measure_PreviewPolygon", out var id2);
				_vectorManager.AddOrUpdateVector(ref id2, polygon, DefaultEditSymbology.SelectLineSymbology);
				overlay.RegisterVector("Measure_PreviewPolygon", id2);
			}
		}
		RenderPreviewPoint(cursorLocation, overlay);
	}

	private void RenderPreviewLabel(Geospatial.Vectors.Point location, OverlayChannels overlay)
	{
		overlay.TryGetVector("Measure_PreviewText", out var id);
		LabelDefinition labelDefinition = new LabelDefinition(IsAreaMode() ? ("Area " + MathHelpers.DoubleToSigFigString(Math.Abs(ComputePreviewArea(location)), 3)) : ("Length " + MathHelpers.DoubleToSigFigString(ComputePreviewLength(location), 3)), location);
		_vectorManager.AddOrUpdateVector(ref id, labelDefinition, DynamicTextSymbology);
		_vectorManager.SetVectorZIndex(id, 999);
		overlay.RegisterVector("Measure_PreviewText", id);
	}

	private void RenderPersistentGeometry(Geospatial.Vectors.Point closePoint, OverlayChannels overlay)
	{
		List<Geospatial.Vectors.Point> points = new List<Geospatial.Vectors.Point>(_points);
		if (!closePoint.IsNull())
		{
			points.Add(closePoint);
		}
		if (IsAreaMode())
		{
			Polygon polygon = BuildPolygon(points);
			if (polygon != null && polygon.Count > 2)
			{
				overlay.TryGetVector("Measure_PersistentPolygon", out var id);
				_vectorManager.AddOrUpdateVector(ref id, polygon.Points.ToList(), DefaultEditSymbology.SelectLineSymbology, VectorType.Polygon);
				overlay.RegisterVector("Measure_PersistentPolygon", id);
			}
		}
		else
		{
			overlay.TryGetVector("Measure_PersistentLine", out var id2);
			_vectorManager.AddOrUpdateVector(ref id2, points.ToList(), DefaultEditSymbology.TopologyArcSymbology, VectorType.Line);
			overlay.RegisterVector("Measure_PersistentLine", id2);
		}
	}

	private void RenderPersistentLabel(Geospatial.Vectors.Point closePoint, OverlayChannels overlay)
	{
		overlay.TryGetVector("Measure_PersistentText", out var id);
		List<Geospatial.Vectors.Point> pointsList = new List<Geospatial.Vectors.Point>(_points);
		if (!closePoint.IsNull())
		{
			pointsList.Add(closePoint);
		}
		if (IsAreaMode())
		{
			double absolutePreviewArea = Math.Abs(ComputePreviewArea(closePoint));
			Polygon polygon = BuildPolygon(pointsList);
			Geospatial.Vectors.Point point;
			if (polygon == null)
			{
				point = pointsList[pointsList.Count - 1];
			}
			else
			{
				point = polygon.BestLabelPoint();
			}
			Geospatial.Vectors.Point location = point;
			string doubleToSigFigStringMathHelpersTerm = "Area " + MathHelpers.DoubleToSigFigString(absolutePreviewArea, 3);
			LabelDefinition labelDefinition = new LabelDefinition(doubleToSigFigStringMathHelpersTerm, location);
			_vectorManager.AddOrUpdateVector(ref id, labelDefinition, PersistentTextSymbology);
		}
		else
		{
			double previewLength = ComputePreviewLength(closePoint);
			string doubleToSigFigStringMathHelpersTerm2 = "Length " + MathHelpers.DoubleToSigFigString(previewLength, 3);
			if ((_getSlope != null && _points.Count > 1) || (!closePoint.IsNull() && _points.Count == 1))
			{
				Geospatial.Vectors.Point point2;
				if (!closePoint.IsNull())
				{
					point2 = closePoint;
				}
				else
				{
					List<Geospatial.Vectors.Point> points = _points;
					point2 = points[points.Count - 2];
				}
				Geospatial.Vectors.Point point22 = point2;
				Polyline polyline = new Polyline();
				List<Geospatial.Vectors.Point> points2 = _points;
				polyline.Add(points2[points2.Count - 1]);
				polyline.Add(point22);
				Polyline polyline2 = polyline;
				double getSlope = _getSlope(polyline2);
				if (!double.IsNaN(getSlope))
				{
					doubleToSigFigStringMathHelpersTerm2 = doubleToSigFigStringMathHelpersTerm2 + "\nSlope " + MathHelpers.DoubleToSigFigString(getSlope, 3);
				}
			}
			LabelDefinition labelDefinition2 = new LabelDefinition(doubleToSigFigStringMathHelpersTerm2, pointsList[pointsList.Count - 1]);
			_vectorManager.AddOrUpdateVector(ref id, labelDefinition2, PersistentTextSymbology);
		}
		_vectorManager.TrySetVectorZIndex(id, 999);
		overlay.RegisterVector("Measure_PersistentText", id);
	}

	private bool IsAreaMode()
	{
		return (_mode & MeasureMode.Area) == MeasureMode.Area;
	}

	private double ComputePreviewLength(Geospatial.Vectors.Point cursor)
	{
		if (_points.Count == 0)
		{
			return 0.0;
		}
		double distanceToPointsTerm = 0.0;
		for (int i = 1; i < _points.Count; i++)
		{
			distanceToPointsTerm += _points[i - 1].DistanceTo(_points[i]);
		}
		if (!cursor.IsNull())
		{
			double computed = distanceToPointsTerm;
			List<Geospatial.Vectors.Point> points = _points;
			distanceToPointsTerm = computed + points[points.Count - 1].DistanceTo(cursor);
		}
		return distanceToPointsTerm;
	}

	private double ComputePreviewArea(Geospatial.Vectors.Point cursor)
	{
		List<Geospatial.Vectors.Point> points = new List<Geospatial.Vectors.Point>(_points);
		if (!cursor.IsNull())
		{
			points.Add(cursor);
		}
		return BuildPolygon(points)?.Area ?? 0.0;
	}

	private static Polygon BuildPolygon(IReadOnlyList<Geospatial.Vectors.Point> points)
	{
		if (points.Count < 3)
		{
			return null;
		}
		List<Geospatial.Vectors.Point> pointsList = new List<Geospatial.Vectors.Point>(points);
		Geospatial.Vectors.Point point = pointsList[0];
		if (!point.EqualXY(pointsList[pointsList.Count - 1]))
		{
			pointsList.Add(pointsList[0]);
		}
		return new Polygon(pointsList);
	}

	private static TextVectorSymbology CreateTextSymbology()
	{
		return new TextVectorSymbology(Color.FromArgb(255, 255, 255, 255), 12.0)
		{
			HasShadow = true,
			HaloColor = Color.FromArgb(128, 0, 0, 0),
			HaloThickness = 1f,
			IsBold = true,
			TextEmphasis = 1
		};
	}
}
