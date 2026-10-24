using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers.Selection;

public interface ISelection : IBasicSelection
{
	IList<object> IBasicSelection.GetSelectedVectors()
	{
		IReadOnlyList<ComplexPolygon> selectedComplexPolygons = GetSelectedComplexPolygons();
		IReadOnlyList<Polygon> selectedPolygons = GetSelectedPolygons();
		IReadOnlyList<Polyline> selectedPolylines = GetSelectedPolylines();
		IReadOnlyList<Point> selectedPoints = GetSelectedPoints();
		int selectedPolylinesTerm = selectedPolylines?.Count ?? 0;
		int selectedPolygonsTerm = selectedPolygons?.Count ?? 0;
		int selectedComplexPolygonsTerm = selectedComplexPolygons?.Count ?? 0;
		int selectedPointsTerm = selectedPoints?.Count ?? 0;
		int ordinal = selectedPolylinesTerm + selectedPolygonsTerm + selectedComplexPolygonsTerm + selectedPointsTerm;
		if (ordinal == 0)
		{
			return null;
		}
		List<object> objectList = new List<object>(ordinal);
		if (selectedPolylinesTerm > 0)
		{
			objectList.AddRange(selectedPolylines);
		}
		if (selectedPolygonsTerm > 0)
		{
			objectList.AddRange(selectedPolygons);
		}
		if (selectedComplexPolygonsTerm > 0)
		{
			objectList.AddRange(selectedComplexPolygons);
		}
		if (selectedPointsTerm > 0)
		{
			objectList.AddRange(selectedPoints.Cast<object>());
		}
		return objectList;
	}

	IReadOnlyList<Point> GetSelectedPoints()
	{
		return null;
	}

	IReadOnlyList<Polyline> GetSelectedPolylines()
	{
		return null;
	}

	IReadOnlyList<Polygon> GetSelectedPolygons()
	{
		return null;
	}

	IReadOnlyList<ComplexPolygon> GetSelectedComplexPolygons()
	{
		return null;
	}
}
