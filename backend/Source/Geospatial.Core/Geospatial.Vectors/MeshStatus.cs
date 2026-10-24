namespace Geospatial.Vectors;

public enum MeshStatus
{
	Complete,
	FewerThanMinPerimeterPoints,
	PerimeterPolygonError,
	FewerThanMinNumCells,
	DuplicatePoints,
	PointsOutsidePerimeter,
	DisconnectedGraphs,
	FacePerimeterConnectionError,
	CellAreaTotalError,
	MaxFacesPerCellExceeded,
	MaxFacesPerFacePointExceeded,
	BreaklineError,
	UnknownError,
	MinFaceLenError,
	OldVersion
}
