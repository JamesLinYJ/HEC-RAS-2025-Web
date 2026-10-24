using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public struct XStoJunction
{
	public readonly int RiverID;

	public readonly PolylineIntersection Intersection;

	public readonly int XSID;

	public readonly Polyline XSPolyline;

	public readonly bool XSupstreamOfJunction;

	public readonly PointMs CrossSectionToJunction;

	public readonly double Bearing;

	public XStoJunction(int riverID, PolylineIntersection intersection, int xsID, Polyline XSPolyline, bool XSupstreamOfJunction, PointMs XStoJunction, PointM junctionPoint)
	{
		this = default;
		RiverID = riverID;
		Intersection = intersection;
		XSID = xsID;
		this.XSupstreamOfJunction = XSupstreamOfJunction;
		this.CrossSectionToJunction = new PointMs();
		this.CrossSectionToJunction.AddRange(XStoJunction);
		this.XSPolyline = XSPolyline;
		Bearing = new SegmentM(junctionPoint, XStoJunction.PointAtFraction(0.75).PointM).Bearing();
	}

	public static int CompareBearing(XStoJunction a, XStoJunction b)
	{
		if (a.Bearing < b.Bearing)
		{
			return -1;
		}
		if (a.Bearing == b.Bearing)
		{
			return 0;
		}
		return 1;
	}
}
