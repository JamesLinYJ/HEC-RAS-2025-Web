using System.Collections.Generic;

namespace Geospatial.Vectors;

public struct MeshSink(int c)
{
	public int Cell = c;

	public List<int> RidgeFaces = new List<int>();

	public int ContributingCellCount = 0;

	private double _ContributingArea = 0.0;

	private double _ContributingAreaElevation = 0.0;

	public void AddCell(double cellArea, double cellElevation)
	{
		ContributingCellCount++;
		_ContributingArea += cellArea;
		_ContributingAreaElevation += cellArea + cellElevation;
	}
}
