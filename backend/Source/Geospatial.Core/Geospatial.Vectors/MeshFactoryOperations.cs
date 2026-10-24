using System.Collections.Generic;

namespace Geospatial.Vectors;

public static class MeshFactoryOperations
{
	public const double MinFaceLengthRatio = 0.05;

	public const double FaceCollapseBreaklineRatio = 0.4;

	public const double MinFaceLenSI = 0.003;

	public const double MinFaceLenUS = 0.01;

	public const double TinFilterToleranceSI = 0.03;

	public const double TinFilterToleranceUSC = 0.1;

	public const double TinFilterToleranceDxyFactor = 0.0001;

	public const bool CollapsePerimeterFaces = true;

	public const int MaxFaceCountPerCell = 8;

	public const int MaxFaceCountPerFacePoint = 8;

	public static double MinAllowedFaceLength()
	{
		return 0.01;
	}

	public static Extent RatioMBR(IList<Point> cellCenters, Extent mbr)
	{
		if (cellCenters.Count < 100)
		{
			mbr = mbr.RatioAroundCenter(3.0);
		}
		else if (cellCenters.Count < 1000)
		{
			mbr = mbr.RatioAroundCenter(2.0);
		}
		else if (cellCenters.Count < 10000)
		{
			mbr = mbr.RatioAroundCenter(1.1);
		}
		return mbr;
	}

	public static bool ValidateInput(Polygon perim, IList<Point> cells, out MeshError error)
	{
		error = new MeshError();
		if (perim.Count <= 3)
		{
			error.StatusMessage = "Need at least three points on perimeter of the 2D-area.";
			error.Status = MeshStatus.FewerThanMinPerimeterPoints;
			return false;
		}
		if (perim.HasConsecutiveIdenticalPoints())
		{
			error.StatusMessage = "Polygon error - please inspect/filter your perimeter to remove duplicate points.";
			error.Status = MeshStatus.PerimeterPolygonError;
			return false;
		}
		if (cells.Count <= 1)
		{
			error.StatusMessage = "Need at least two cells in the 2D-area.";
			error.Status = MeshStatus.FewerThanMinNumCells;
			return false;
		}
		error.StatusMessage = "";
		error.Status = MeshStatus.Complete;
		return true;
	}

	public static bool ValidatePointsInsidePerim(IList<Point> cellCenters, Tin perimeterDevTin, double tolerance, out MeshError error)
	{
		error = new MeshError();
		List<int> badCells = new List<int>();
		int t = 0;
		for (int i = 0; i < cellCenters.Count; i++)
		{
			if (!perimeterDevTin.ContainsPointBufferedFavorOutOfBounds(cellCenters[i], tolerance, ref t))
			{
				badCells.Add(i);
			}
		}
		if (badCells.Count > 0)
		{
			error.StatusMessage = badCells.Count + " point(s) detected outside the perimeter of the 2D-area.";
			error.Status = MeshStatus.PointsOutsidePerimeter;
			error.BadCells = badCells;
			return false;
		}
		return true;
	}
}
