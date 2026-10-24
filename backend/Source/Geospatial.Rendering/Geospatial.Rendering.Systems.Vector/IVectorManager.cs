using System;
using System.Collections.Generic;
using Geospatial.Rasters.Convolution;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;
using Utility.Metadata;

namespace Geospatial.Rendering.Systems.Vector;

public interface IVectorManager : IDisposable
{
	public delegate void TransformationFromBaseChangedEvent(Matrix3x3D transformation);

	static VectorID DebugVectorID;

	const double CLIP_RATIO = 2.0;

	ICanvas Canvas { get; }

	Matrix3x3D TransformationFromBase { get; set; }

	Matrix3x3D BaseTransform { get; }

	int VectorIDCount { get; }

	int VectorCount { get; }

	event TransformationFromBaseChangedEvent TransformationFromBaseChanged;

	void Dispose(bool disposing);

	void AddOrUpdateVectors(IList<object> vectors, VectorSymbology symbology, ref VectorID pointsID, ref VectorID segmentsID, ref VectorID polylinesID, ref VectorID polygonsID, ref VectorID complexPolygonsID);

	void AddOrUpdateVector(ref VectorID id, ILabelDefinition iLabelDefinition, VectorSymbology symb, VectorType type = VectorType.Text);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<ILabelDefinition> iLabelDefinitions, VectorSymbology symb, VectorType type = VectorType.Text);

	void AddOrUpdateVector(ref VectorID id, Point point, VectorSymbology symb, VectorType type = VectorType.Point);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Point> points, VectorSymbology symb, VectorType type = VectorType.Point);

	void AddOrUpdateVector(ref VectorID id, Segment segment, VectorSymbology symb, VectorType type = VectorType.Segment);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Segment> segments, VectorSymbology symb, VectorType type = VectorType.Segment);

	void AddOrUpdateVector(ref VectorID id, Polyline polyline, VectorSymbology symb, VectorType type = VectorType.Line);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Polyline> polylines, VectorSymbology symb, VectorType type = VectorType.Line);

	void AddOrUpdateVector(ref VectorID id, Polygon polygon, VectorSymbology symb, VectorType type = VectorType.Polygon);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Polygon> polygons, VectorSymbology symb, VectorType type = VectorType.Polygon);

	void AddOrUpdateVector(ref VectorID id, ComplexPolygon polygon, VectorSymbology symb, VectorType type = VectorType.ComplexPolygon);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<ComplexPolygon> polygons, VectorSymbology symb, VectorType type = VectorType.ComplexPolygon);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Memory<Point>> lines, VectorSymbology symb, VectorType type = VectorType.Line);

	void AddOrUpdateVector(ref VectorID id, Memory<Point> lines, VectorSymbology symb, VectorType type = VectorType.Line);

	void AddOrUpdateVector(ref VectorID id, IReadOnlyList<ImmutableMultiPartPolygon> lines, VectorSymbology symb, VectorType type = VectorType.ComplexPolygon);

	void AddOrUpdateVector(ref VectorID id, TriangulatedPolygonData data, VectorSymbology symb);

	void TryClearVector(ref VectorID id);

	void TryDeleteVector(ref VectorID id);

	void TryDeleteVector(VectorID id);

	void TryUpdateSymbology(VectorID id, VectorSymbology symb);

	VectorID AddTextVector(ILabelDefinition iLabelDefinition, VectorSymbology symbology);

	VectorID AddTextVector(IReadOnlyList<ILabelDefinition> iLabelDefinitions, VectorSymbology symbology);

	VectorID AddComplexPolygonVector(ComplexPolygon poly, VectorSymbology symbology);

	VectorID AddComplexPolygonVector(IReadOnlyList<ComplexPolygon> polygons, VectorSymbology symbology);

	VectorID AddPointVector(Point point, VectorSymbology symbology);

	VectorID AddPointVector(IReadOnlyList<Point> points, VectorSymbology symbology);

	VectorID AddPointVector(double x, double y, VectorSymbology symbology);

	VectorID AddPointVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology);

	VectorID AddPointVector(Segment seg, VectorSymbology symbology);

	VectorID AddPointVector(IReadOnlyList<Segment> segments, VectorSymbology symbology);

	VectorID AddPointVector(Polyline pl, VectorSymbology symbology);

	VectorID AddPointVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology);

	VectorID AddPointVector(Polygon polygon, VectorSymbology symbology);

	VectorID AddPointVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology);

	VectorID AddSegmentVector(Point point, VectorSymbology symbology);

	VectorID AddSegmentVector(double x, double y, VectorSymbology symbology);

	VectorID AddSegmentVector(IReadOnlyList<Point> points, VectorSymbology symbology);

	VectorID AddSegmentVector(double x1, double y1, double x2, double y2, VectorSymbology symbology);

	VectorID AddSegmentVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology);

	VectorID AddSegmentVector(Segment seg, VectorSymbology symbology);

	VectorID AddSegmentVector(IReadOnlyList<Segment> segments, VectorSymbology symbology);

	VectorID AddSegmentVector(Polyline line, VectorSymbology symbology);

	VectorID AddSegmentVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology);

	VectorID AddSegmentVector(Polygon polygon, VectorSymbology symbology);

	VectorID AddSegmentVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology);

	VectorID AddLineVector(Point point, VectorSymbology symbology);

	VectorID AddLineVector(double x, double y, VectorSymbology symbology);

	VectorID AddLineVector(IReadOnlyList<Point> points, VectorSymbology symbology);

	VectorID AddLineVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology);

	VectorID AddLineVector(Polyline line, VectorSymbology symbology);

	VectorID AddLineVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology);

	VectorID AddLineVector(Polygon polygon, VectorSymbology symbology);

	VectorID AddLineVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology);

	VectorID AddPolygonVector(Point point, VectorSymbology symbology);

	VectorID AddPolygonVector(double x, double y, VectorSymbology symbology);

	VectorID AddPolygonVector(IReadOnlyList<Point> points, VectorSymbology symbology);

	VectorID AddPolygonVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology);

	VectorID AddPolygonVector(Polyline line, VectorSymbology symbology);

	VectorID AddPolygonVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology);

	VectorID AddPolygonVector(Polygon polygon, VectorSymbology symbology);

	VectorID AddPolygonVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology);

	void AddVectors(List<object> vectors, VectorSymbology symbology, out VectorID pointsID, out VectorID segmentsID, out VectorID polylinesID, out VectorID polygonsID, out VectorID complexPolygonsID);

	VectorID AddVector(Point point, VectorSymbology symbology, VectorType type = VectorType.Point);

	VectorID AddVector(IReadOnlyList<Point> points, VectorSymbology symbology, VectorType type = VectorType.Point);

	VectorID AddVector(double x, double y, VectorSymbology symbology, VectorType type = VectorType.Point);

	VectorID AddVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology, VectorType type = VectorType.Point);

	VectorID AddVector(Segment seg, VectorSymbology symbology, VectorType type = VectorType.Segment);

	VectorID AddVector(IReadOnlyList<Segment> segments, VectorSymbology symbology, VectorType type = VectorType.Segment);

	VectorID AddVector(Polyline pl, VectorSymbology symbology, VectorType type = VectorType.Line);

	VectorID AddVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology, VectorType type = VectorType.Line);

	VectorID AddVector(Polygon polygon, VectorSymbology symbology, VectorType type = VectorType.Polygon);

	VectorID AddVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology, VectorType type = VectorType.Polygon);

	void UpdateComplexPolygonVector(VectorID id, ComplexPolygon polygon);

	void UpdateComplexPolygonVector(VectorID id, IReadOnlyList<ComplexPolygon> polygons);

	void UpdateTextVector(VectorID id, ILabelDefinition iLabelDefinition);

	void UpdateTextVector(VectorID id, IReadOnlyList<ILabelDefinition> iLabelDefinitions);

	void UpdatePointVector(VectorID id, Point pt);

	void UpdatePointVector(VectorID id, IReadOnlyList<Point> points);

	void UpdatePointVector(VectorID id, double x, double y);

	void UpdatePointVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys);

	void UpdatePointVector(VectorID id, Segment seg);

	void UpdatePointVector(VectorID id, IReadOnlyList<Segment> segments);

	void UpdatePointVector(VectorID id, Polyline line);

	void UpdatePointVector(VectorID id, IReadOnlyList<Polyline> lines);

	void UpdatePointVector(VectorID id, Polygon polygon);

	void UpdatePointVector(VectorID id, IReadOnlyList<Polygon> polygons);

	void UpdateSegmentVector(VectorID id, Point point);

	void UpdateSegmentVector(VectorID id, IReadOnlyList<Point> points);

	void UpdateSegmentVector(VectorID id, double x, double y);

	void UpdateSegmentVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys);

	void UpdateSegmentVector(VectorID id, Segment seg);

	void UpdateSegmentVector(VectorID id, IReadOnlyList<Segment> segments);

	void UpdateSegmentVector(VectorID id, Polyline pl);

	void UpdateSegmentVector(VectorID id, IReadOnlyList<Polyline> polylines);

	void UpdateSegmentVector(VectorID id, Polygon poly);

	void UpdateSegmentVector(VectorID id, IReadOnlyList<Polygon> polygons);

	void UpdateLineVector(VectorID id, Point pt);

	void UpdateLineVector(VectorID id, IReadOnlyList<Point> points);

	void UpdateLineVector(VectorID id, double x, double y);

	void UpdateLineVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys);

	void UpdateLineVector(VectorID id, Polyline line);

	void UpdateLineVector(VectorID id, IReadOnlyList<Polyline> lines);

	void UpdateLineVector(VectorID id, Polygon polygon);

	void UpdateLineVector(VectorID id, IReadOnlyList<Polygon> polygons);

	void UpdatePolygonVector(VectorID id, Point point);

	void UpdatePolygonVector(VectorID id, IReadOnlyList<Point> points);

	void UpdatePolygonVector(VectorID id, double x, double y);

	void UpdatePolygonVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys);

	void UpdatePolygonVector(VectorID id, Polyline pl);

	void UpdatePolygonVector(VectorID id, IReadOnlyList<Polyline> polylines);

	void UpdatePolygonVector(VectorID id, Polygon polygon);

	void UpdatePolygonVector(VectorID id, IReadOnlyList<Polygon> polygons);

	void UpdateVectors(List<object> vectors, ref VectorID pointsID, ref VectorID segmentsID, ref VectorID polylinesID, ref VectorID polygonsID, ref VectorID complexPolygonsID);

	void UpdateVector(VectorID id, IReadOnlyList<Point> points);

	void UpdateVector(VectorID id, double x, double y);

	void UpdateVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys);

	void UpdateVector(VectorID id, Segment seg);

	void UpdateVector(VectorID id, IReadOnlyList<Segment> segments);

	void UpdateVector(VectorID id, Polyline pl);

	void UpdateVector(VectorID id, IReadOnlyList<Polyline> lines);

	void UpdateVector(VectorID id, Polygon polygon);

	void UpdateVector(VectorID id, IReadOnlyList<Polygon> polygons);

	void UpdateVector(VectorID id, ComplexPolygon polygon);

	void UpdateVector(VectorID id, IReadOnlyList<ComplexPolygon> polygons);

	void DeleteVector(VectorID id);

	void TranslateAllVectorsDelta(double deltaXScreenSpace, double deltaYScreenSpace);

	void Zoom(Point centerOnScreenPixel, double scale);

	void ResetAllVectorTranslation();

	void ResetZoom();

	void SetVectorZIndex(VectorID id, int zIdx);

	void TrySetVectorZIndex(VectorID id, int zIdx);

	PolylineVectorAnimation TrackAnimation(Polyline pl);

	CulvertFlowAnimation TrackCulvertAnimation(Polyline pl);

	List<IMetadataBase> GetMetadata();

	void SetVectorDynamicDataSource(VectorID id, Func<object> dynamicDataFunc);

	void ForceRedrawDynamicDataSourceVector(VectorID id);

	void RedrawAllFromCachedData(VectorManagerRedrawReason reason = VectorManagerRedrawReason.Unknown);

	void SetVectorClipExtent(VectorID id, Memory<Point> extent);
}
