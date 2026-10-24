using System.Collections.Generic;

namespace Geospatial.Clipper2Lib;

public class OutPt2
{
	public OutPt2? next;

	public OutPt2? prev;

	public Point64 pt;

	public int ownerIdx;

	public List<OutPt2?>? edge;

	public OutPt2(Point64 pt)
	{
		this.pt = pt;
	}
}
