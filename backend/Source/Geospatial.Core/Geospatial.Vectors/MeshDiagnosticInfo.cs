using System.Collections.Generic;

namespace Geospatial.Vectors;

public class MeshDiagnosticInfo
{
	public Tin PerimeterDevTin;

	public Tin MeshTin;

	public byte[] CellConnectivityGraph;

	public Dictionary<string, Tin> Tins = new Dictionary<string, Tin>();

	public List<Tin> StepByStep = new List<Tin>();

	public List<BurnedSegmentDiagnosticInfo> BurnedSegments = new List<BurnedSegmentDiagnosticInfo>();
}
