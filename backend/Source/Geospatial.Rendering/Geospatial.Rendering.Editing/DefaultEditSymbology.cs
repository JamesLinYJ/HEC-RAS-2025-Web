using System.Drawing;

namespace Geospatial.Rendering.Editing;

public static class DefaultEditSymbology
{
	public static VectorSymbology MarqueeBackdropSymbology = new VectorSymbology(Color.FromArgb(255, 255, 255, 255), 2.0, Color.FromArgb(48, 255, 255, 255));

	public static VectorSymbology MarqueeStrokeSymbology = new VectorSymbology(Color.FromArgb(255, 0, 0, 0), 2.0, Color.FromArgb(0, 0, 0, 0))
	{
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 3f, 3f }
	};

	public static VectorSymbology OpenSymbology = new VectorSymbology(Color.FromArgb(255, 252, 145, 58), 3.0, Color.FromArgb(32, 252, 145, 58))
	{
		PointSize = 5
	};

	public static VectorSymbology InternalPointSymbology = new VectorSymbology(Color.FromArgb(255, 48, 116, 179), 1.0, Color.FromArgb(255, 48, 116, 179))
	{
		PointSize = 3
	};

	public static VectorSymbology AppendSegmentSymbology = new VectorSymbology(Color.FromArgb(255, 91, 204, 59), 3.0, Color.FromArgb(224, 91, 204, 59))
	{
		PointSize = 3,
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 1f, 1.5f }
	};

	public static VectorSymbology AppendSegmentBackgroundSymbology = new VectorSymbology(Color.FromArgb(255, 20, 77, 5), 3.0, Color.FromArgb(255, 20, 77, 5))
	{
		PointSize = 3
	};

	public static VectorSymbology AppendPointSymbology = new VectorSymbology(Color.FromArgb(255, 20, 77, 5), 1.0, Color.FromArgb(255, 91, 204, 59))
	{
		PointSize = 4
	};

	public static VectorSymbology SelectedSymbology = new VectorSymbology(Color.FromArgb(255, 255, 0, 255), 3.0, Color.FromArgb(64, 255, 0, 255));

	public static VectorSymbology SelectedPolygonSymbology = new VectorSymbology(Color.FromArgb(255, 255, 0, 255), 2.0, Color.FromArgb(64, 255, 0, 255));

	public static VectorSymbology SelectedPointSymbology = new VectorSymbology(Color.FromArgb(255, 222, 2, 222), 2.0, Color.FromArgb(255, 252, 136, 252))
	{
		PointSize = 3
	};

	public static VectorSymbology EditLineSymbology = new VectorSymbology(Color.FromArgb(255, 255, 161, 10), 2.0, Color.FromArgb(64, 255, 161, 10))
	{
		PointSize = 3
	};

	public static VectorSymbology EditPointSymbology = new VectorSymbology(Color.FromArgb(255, 158, 98, 2), 1.0, Color.FromArgb(255, 255, 187, 79))
	{
		PointSize = 3
	};

	public static VectorSymbology SelectLineSymbology_OLD = new VectorSymbology(Color.FromArgb(255, 84, 201, 255), 2.0, Color.FromArgb(64, 84, 201, 255))
	{
		PointSize = 3
	};

	public static VectorSymbology SelectLineSymbology = new VectorSymbology(Color.FromArgb(255, 255, 255, 0), 2.0, Color.FromArgb(96, 255, 255, 0))
	{
		PointSize = 3
	};

	public static VectorSymbology SelectPolygonSymbology = new VectorSymbology(SelectLineSymbology.PenColor, 0.0, SelectLineSymbology.FillColor);

	public static VectorSymbology SelectPointSymbology = new VectorSymbology(Color.FromArgb(255, 21, 87, 158), 1.0, Color.FromArgb(255, 64, 156, 255))
	{
		PointSize = 3
	};

	public static VectorSymbology InternalPointErrorSymbology = new VectorSymbology(Color.Red, 3.0, Color.Red)
	{
		PointSize = 5
	};

	public static VectorSymbology LineErrorSymbology = new VectorSymbology(Color.FromArgb(255, 247, 86, 74), 2.0, Color.FromArgb(0, 0, 0, 0))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologyArcSymbology = new VectorSymbology(Color.FromArgb(255, 32, 0, 64), 2.0, Color.FromArgb(32, 56, 142, 255));

	public static VectorSymbology TopologyArcSymbologyDesatured = TopologyArcSymbology.CloneDesaturated(0.25f);

	public static VectorSymbology TopologyNodeSymbology = new VectorSymbology(Color.FromArgb(255, 91, 55, 158), 2.0, Color.FromArgb(255, 197, 166, 255))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologyDeletableNodeSymbology = new VectorSymbology(Color.FromArgb(255, 91, 55, 158), 2.0, Color.FromArgb(255, 66, 135, 245))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologyNodeSymbologyDesaturated = TopologyNodeSymbology.CloneDesaturated(0.25f);

	public static VectorSymbology TopologyEditNodeSymbology = new VectorSymbology(Color.FromArgb(255, 158, 98, 2), 2.0, Color.FromArgb(255, 255, 187, 79))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologyArcInternalPointSymbology = new VectorSymbology(Color.FromArgb(255, 84, 83, 83), 1.0, Color.FromArgb(255, 179, 179, 179))
	{
		PointSize = 3
	};

	public static VectorSymbology TopologySelectedSymbology = new VectorSymbology(Color.FromArgb(255, 222, 2, 222), 2.0, Color.FromArgb(255, 252, 136, 252))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologySelectedRegionSymbology = new VectorSymbology(SelectedPolygonSymbology.PenColor, 2.0, SelectedPolygonSymbology.FillColor)
	{
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 0.25f, 0.75f }
	};

	public static VectorSymbology TopologyAddNodeSymbology = new VectorSymbology(Color.FromArgb(255, 20, 77, 5), 2.0, Color.FromArgb(255, 91, 204, 59))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologyControlPointSymbology = new VectorSymbology(Color.FromArgb(255, 201, 38, 26), 1.0, Color.FromArgb(224, 201, 38, 26))
	{
		PointSize = 5
	};

	public static VectorSymbology DesaturedControlPointSymbology = TopologyControlPointSymbology.CloneDesaturated(0.25f);

	public static VectorSymbology TopologySelectPointSymbology = new VectorSymbology(Color.FromArgb(255, 235, 82, 70), 1.0, Color.FromArgb(224, 235, 82, 70))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologySplitArcSymbology = new VectorSymbology(Color.FromArgb(255, 91, 204, 59), 3.0, Color.FromArgb(255, 91, 204, 59));

	public static VectorSymbology TopologyErrorDottedSymbology = new VectorSymbology(Color.FromArgb(255, 255, 0, 0), 3.0, Color.FromArgb(64, 255, 0, 0))
	{
		PointSize = 5,
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 2f, 2f }
	};

	public static VectorSymbology TopologyErrorSymbology = new VectorSymbology(Color.FromArgb(255, 255, 156, 156), 3.0, Color.FromArgb(64, 255, 128, 128))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologyErrorNodeSymbology = new VectorSymbology(Color.FromArgb(255, 255, 0, 0), 1.0, Color.FromArgb(255, 255, 128, 128))
	{
		PointSize = 5
	};

	public static VectorSymbology TopologySplitArcSegmentSymbology = new VectorSymbology(Color.FromArgb(255, 129, 235, 99), 3.0, Color.FromArgb(224, 79, 208, 247))
	{
		PointSize = 3,
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 1f, 1.5f }
	};

	public static VectorSymbology TopologySplitArcSegmentBackgroundSymbology = new VectorSymbology(Color.FromArgb(255, 20, 105, 1), 3.0, Color.FromArgb(224, 79, 208, 247))
	{
		PointSize = 3
	};

	public static VectorSymbology PolylineSplitFirstHalfForegroundSymbology = new VectorSymbology(Color.FromArgb(255, 91, 204, 59), 3.0, Color.FromArgb(224, 91, 204, 59))
	{
		PointSize = 3,
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 1f, 1.5f }
	};

	public static VectorSymbology PolylineSplitFirstHalfBackgroundSymbology = new VectorSymbology(Color.FromArgb(255, 20, 77, 5), 3.0, Color.FromArgb(255, 20, 77, 5))
	{
		PointSize = 3
	};

	public static VectorSymbology PolylineSplitSecondHalfForegroundSymbology = new VectorSymbology(Color.FromArgb(255, 79, 208, 247), 3.0, Color.FromArgb(224, 79, 208, 247))
	{
		PointSize = 3,
		IsDotted = true,
		DottedStrokeStyle = new float[2] { 1f, 1.5f }
	};

	public static VectorSymbology PolylineSplitSecondHalfBackgroundSymbology = new VectorSymbology(Color.FromArgb(255, 20, 77, 99), 3.0, Color.FromArgb(255, 20, 77, 99))
	{
		PointSize = 3
	};

	public static VectorSymbology PolylineLineSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolylineVertexSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolylineMapLayerLineSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolylineMapLayerVertexSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolygonShapeSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolygonVertexSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolygonMapLayerShapeSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PolygonMapLayerVertexSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PointSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology PointMapLayerSymbology = VectorSymbology.DefaultSymbology();

	public static VectorSymbology MeshSymbology = new VectorSymbology(Color.FromArgb(128, 0, 0, 0), 1.0);

	public static VectorSymbology CulvertBarrelSymbology = new VectorSymbology(Color.Blue, 4.0);

	public static VectorSymbology BoundaryConditionLineSymbology = new VectorSymbology(Color.FromArgb(255, 5, 213, 255), 3.0);

	public static VectorSymbology TopologyEditArcSymbology => EditLineSymbology;
}
