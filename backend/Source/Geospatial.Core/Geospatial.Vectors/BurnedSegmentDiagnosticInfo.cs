namespace Geospatial.Vectors;

public class BurnedSegmentDiagnosticInfo
{
	public Segment Segment;

	public int IndexA;

	public int IndexB;

	public BurnedSegmentDiagnosticInfo(Segment seg, int idxA, int idxB)
	{
		Segment = seg;
		IndexA = idxA;
		IndexB = idxB;
	}
}
