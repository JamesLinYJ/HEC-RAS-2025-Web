namespace RasMapperLib.Mesh;

public enum MeshStatus
{
	FewerThanMinPerimeterPoints,
	PerimeterPolygonError,
	FewerThanMinNumCells,
	DuplicatePoints,
	PointsOutsidePerimeter,
	DisconnectedGraphs,
	FacePerimeterConnectionError,
	CellAreaTotalError,
	MaxFacesPerCellExceeded,
	MaxFacesPerFacepointExceeded,
	BreaklineError,
	UnknownError,
	MinFaceLenError,
	OldVersion,
	Complete
}
