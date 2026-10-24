using System.Collections.Generic;

namespace RasMapperLib;

public class InMemoryVolumeElevationInfo : IVolumeElevationInfo
{
	private Point2DCollection _table;

	public InMemoryVolumeElevationInfo(Point2DCollection table)
	{
		_table = table;
	}

	public InMemoryVolumeElevationInfo(IList<Point2D> points)
	{
		_table = new Point2DCollection(points);
	}

	public override string ToString()
	{
		return "(" + _table.Count + " Points)";
	}

	public Point2DCollection GetTable()
	{
		return _table;
	}

	Point2DCollection IVolumeElevationInfo.GetTable()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetTable
		return this.GetTable();
	}
}
