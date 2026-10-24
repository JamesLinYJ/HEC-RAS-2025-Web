using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class TerrainQueryVolumeElevationInfo : IVolumeElevationInfo
{
	private TerrainLayer _trLayer;

	private Polygon _polygon;

	private Point2DCollection _table;

	private double _tol;

	public TerrainQueryVolumeElevationInfo(Polygon polygon, TerrainLayer tlayer, double filterTolerance = 0.01)
	{
		_polygon = polygon;
		_trLayer = tlayer;
		_tol = filterTolerance;
	}

	public Point2DCollection GetTable()
	{
		ComputeTableIfNecessary();
		return _table;
	}

	Point2DCollection IVolumeElevationInfo.GetTable()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetTable
		return this.GetTable();
	}

	private void ComputeTableIfNecessary()
	{
		double volumeFactor = 1000.0;
		if (Operators.CompareString(SharedData.UnitsSystem(), SharedData.sUSCustomaryUnits, TextCompare: false) == 0)
		{
			volumeFactor = 43560.0;
		}
		if (_table == null && _trLayer != null)
		{
			_table = _trLayer.ComputeElevationVolumeTable(_polygon, _tol, volumeFactor);
		}
	}
}
