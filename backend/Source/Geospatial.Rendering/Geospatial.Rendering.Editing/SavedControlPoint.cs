using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public struct SavedControlPoint(int fid, Point originalPoint, object[] metaDataRow)
{
	public int FID = fid;

	public Point OriginalPoint = originalPoint;

	public object[] MetaDataRow = metaDataRow;
}
