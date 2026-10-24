using System.Collections.Generic;

namespace Geospatial.Vectors;

public class MeshError
{
	public List<int> BadCells;

	public List<int> BadFaces;

	public List<int> BadFacePoints;

	public List<Point> BadPoints;

	public bool HasFatalPerimeterError { get; set; }

	public MeshStatus Status { get; set; }

	public string StatusMessage { get; set; }

	public string GetMeshErrorMessage()
	{
		if (Status == MeshStatus.Complete)
		{
			return "";
		}
		if (Status == MeshStatus.PointsOutsidePerimeter)
		{
			return BadCells.Count + " Error(s) - Points Outside of Perimeter";
		}
		if (Status == MeshStatus.DuplicatePoints)
		{
			return BadCells.Count + " Error(s) - Duplicate Cell Points";
		}
		if (Status == MeshStatus.DisconnectedGraphs)
		{
			return "Error - Disconnected Mesh Sections";
		}
		if (Status == MeshStatus.FacePerimeterConnectionError)
		{
			return BadCells.Count + " Error(s) - Face Perimeter Connection Error";
		}
		if (Status == MeshStatus.MaxFacesPerCellExceeded)
		{
			return BadCells.Count + " Error(s) - Maximum 8 Faces per Cell";
		}
		if (Status == MeshStatus.FewerThanMinPerimeterPoints)
		{
			return "Error - Needs More Perimeter Points";
		}
		if (Status == MeshStatus.FewerThanMinNumCells)
		{
			return "Error - Needs > 1 Cell";
		}
		if (Status == MeshStatus.CellAreaTotalError)
		{
			return "Cell Area Error";
		}
		if (Status == MeshStatus.MinFaceLenError)
		{
			return BadFaces.Count + " Error(s) - Minimum Face Length Exceeded";
		}
		if (Status == MeshStatus.PerimeterPolygonError)
		{
			return StatusMessage;
		}
		return "Unknown Error";
	}
}
