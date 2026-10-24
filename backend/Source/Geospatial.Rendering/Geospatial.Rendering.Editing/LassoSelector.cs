using System;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering.Editing;

public sealed class LassoSelector
{
	private static readonly ILogger _logger = LogHelper.GetLogger("LassoSelector");

	private readonly IVectorManager _vectorManager;

	private readonly Action<Polygon, SelectMode> _select;

	private readonly Polyline _line = new Polyline(100);

	private readonly Polygon _previewPolygon = new Polygon(100);

	private VectorID _previewBackdropId;

	private VectorID _previewStrokeId;

	private bool _isActive;

	public bool IsActive => _isActive;

	public LassoSelector(IVectorManager vectorManager, Action<Polygon, SelectMode> select)
	{
		_vectorManager = vectorManager ?? throw new ArgumentNullException("vectorManager");
		_select = select ?? throw new ArgumentNullException("select");
	}

	public void Begin(Point startPt)
	{
		if (_isActive)
		{
			_logger.LogError("Beginning lasso selection when it's already active.");
		}
		_line.Clear();
		_line.Add(startPt);
		_isActive = true;
	}

	public void AddPoint(Point pt)
	{
		if (_isActive)
		{
			_line.Add(pt);
		}
	}

	public void End(SelectMode mode)
	{
		if (!_isActive)
		{
			_logger.LogError("Finishing lasso selection when it's not active.");
			return;
		}
		_isActive = false;
		if (TryBuildClosedLasso(out var lasso))
		{
			_select(lasso, mode);
		}
		_line.Clear();
		Clear();
	}

	public void Quit()
	{
		if (_isActive)
		{
			_isActive = false;
			_line.Clear();
			Clear();
		}
	}

	public void Clear()
	{
		_vectorManager.TryDeleteVector(ref _previewBackdropId);
		_vectorManager.TryDeleteVector(ref _previewStrokeId);
	}

	public void Render()
	{
		if (_isActive && _line.Count >= 3)
		{
			_previewPolygon.Clear();
			for (int i = 0; i < _line.Count; i++)
			{
				_previewPolygon.Add(_line[i]);
			}
			_previewPolygon.Add(_line[0]);
			_vectorManager.AddOrUpdateVector(ref _previewBackdropId, _previewPolygon, DefaultEditSymbology.MarqueeBackdropSymbology);
			_vectorManager.AddOrUpdateVector(ref _previewStrokeId, _previewPolygon, DefaultEditSymbology.MarqueeStrokeSymbology);
		}
	}

	private bool TryBuildClosedLasso(out Polygon lasso)
	{
		lasso = null;
		if (_line.Count < 3)
		{
			return false;
		}
		Polygon polygon = new Polygon(_line.Count + 1);
		for (int i = 0; i < _line.Count; i++)
		{
			polygon.Add(_line[i]);
		}
		if (!polygon[0].Equals(polygon[polygon.Count - 1]))
		{
			polygon.Add(polygon[0]);
		}
		if (!Polygon.IsValidPolygon(polygon))
		{
			return false;
		}
		lasso = polygon;
		return true;
	}
}
