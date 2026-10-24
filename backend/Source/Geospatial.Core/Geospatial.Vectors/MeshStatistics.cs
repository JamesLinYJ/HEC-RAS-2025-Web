namespace Geospatial.Vectors;

public class MeshStatistics
{
	public int CellMaximumAreaIndex;

	public int CellMinimumAreaIndex;

	public double CellMaximumArea;

	public double CellMinimumArea;

	public double CellAverageArea;

	public double CellsTotalArea;

	public static MeshStatistics ComputeCellStatistics(Mesh mesh)
	{
		MeshStatistics meshStatistics = new MeshStatistics
		{
			CellMaximumAreaIndex = -1,
			CellMinimumAreaIndex = -1,
			CellMaximumArea = 0.0,
			CellMinimumArea = double.MaxValue,
			CellAverageArea = 0.0,
			CellsTotalArea = 0.0
		};
		for (int i = 0; i < mesh.CellCount; i++)
		{
			double cellPolygonArea = mesh.CellPolygonArea(i);
			if (cellPolygonArea > meshStatistics.CellMaximumArea)
			{
				meshStatistics.CellMaximumArea = cellPolygonArea;
				meshStatistics.CellMaximumAreaIndex = i;
			}
			if (cellPolygonArea < meshStatistics.CellMinimumArea)
			{
				meshStatistics.CellMinimumArea = cellPolygonArea;
				meshStatistics.CellMinimumAreaIndex = i;
			}
			meshStatistics.CellsTotalArea += cellPolygonArea;
		}
		meshStatistics.CellAverageArea = meshStatistics.CellsTotalArea / (double)mesh.CellCount;
		return meshStatistics;
	}

	public static MeshStatistics ComputeCellStatistics(MeshFactory mesh)
	{
		MeshStatistics meshStatistics = new MeshStatistics
		{
			CellMaximumAreaIndex = -1,
			CellMinimumAreaIndex = -1,
			CellMaximumArea = 0.0,
			CellMinimumArea = double.MaxValue,
			CellAverageArea = 0.0,
			CellsTotalArea = 0.0
		};
		for (int i = 0; i < mesh.CellCount; i++)
		{
			double cellPolygonArea = mesh.CellPolygonArea(i);
			if (cellPolygonArea > meshStatistics.CellMaximumArea)
			{
				meshStatistics.CellMaximumArea = cellPolygonArea;
				meshStatistics.CellMaximumAreaIndex = i;
			}
			if (cellPolygonArea < meshStatistics.CellMinimumArea)
			{
				meshStatistics.CellMinimumArea = cellPolygonArea;
				meshStatistics.CellMinimumAreaIndex = i;
			}
			meshStatistics.CellsTotalArea += cellPolygonArea;
		}
		meshStatistics.CellAverageArea = meshStatistics.CellsTotalArea / (double)mesh.CellCount;
		return meshStatistics;
	}
}
