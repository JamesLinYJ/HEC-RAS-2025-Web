using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{DebuggerDisplay()}")]
public class DiagnosticFace
{
	public Mesh Mesh;

	public Face Face;

	public int Index;

	public Polyline Polyline;

	public DiagnosticFace(Mesh m, Face f, int idx, Polyline pl)
	{
		Mesh = m;
		Face = f;
		Index = idx;
		Polyline = pl;
	}

	public string DebuggerDisplay()
	{
		return Face.ToString();
	}
}
