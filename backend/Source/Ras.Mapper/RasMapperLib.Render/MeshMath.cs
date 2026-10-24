using System.Collections.Generic;
using RasMapperLib.Mesh;

namespace RasMapperLib.Render;

public class MeshMath
{

	private List<FacePoint> _facePoints;

	private List<Face> _faces;

	private Cell[] _cells;

	public MeshMath(MeshFV2D mesh)
	{
		_facePoints = new List<FacePoint>();
		_faces = new List<Face>();
		mesh.LoadTo(this);
	}

	public void SetCells(Cell[] c)
	{
		_cells = c;
	}

	public void SetFacepoints(List<FacePoint> fp)
	{
		_facePoints = fp;
	}

	public void SetFaces(List<Face> f)
	{
		_faces = f;
	}
}
