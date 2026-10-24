namespace RasMapperLib;

public class EmptyVolumeElevationInfo : IVolumeElevationInfo
{
	public Point2DCollection GetTable()
	{
		return new Point2DCollection();
	}

	Point2DCollection IVolumeElevationInfo.GetTable()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetTable
		return this.GetTable();
	}
}
