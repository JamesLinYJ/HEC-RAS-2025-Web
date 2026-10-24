using System.Collections.Generic;

namespace Geospatial.Clipper2Lib;

internal class OutRec
{
	public int idx;

	public OutRec? owner;

	public Active? frontEdge;

	public Active? backEdge;

	public OutPt? pts;

	public PolyPathBase? polypath;

	public Rect64 bounds;

	public Path64 path = new Path64();

	public bool isOpen;

	public List<int>? splits;

	public OutRec? recursiveSplit;
}
