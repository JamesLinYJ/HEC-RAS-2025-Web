using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Utility.Logging;

namespace Ras.Synthetics;

public static class MeshGenerator
{
	public static Mesh FromExtent(Extent baseExt, int cellsW, int cellsH, bool generateHexCells = false)
	{
		Polygon perimeter = Polygon.FromExtent(baseExt);
		List<Point> points = new List<Point>();
		double widthOverCellsW = baseExt.Width / (double)cellsW;
		double heightOverCellsH = baseExt.Height / (double)cellsH;
		for (int i = 0; i < cellsH; i++)
		{
			double computed = 0.0;
			int cellsW2 = cellsW;
			if (generateHexCells && i % 2 == 1)
			{
				cellsW2--;
				computed = widthOverCellsW * 0.5;
			}
			for (int j = 0; j < cellsW2; j++)
			{
				points.Add(new Point(baseExt.Left + computed + ((double)j + 0.5) * widthOverCellsW, baseExt.Top - ((double)i + 0.5) * heightOverCellsH));
			}
		}
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		if (!MeshFactory.TryCreateMesh(perimeter, points, null, out var mesh, out var _, meshGenerationParams))
		{
			throw new Exception("Error generating mesh - check returned MeshError.");
		}
		return mesh;
	}

	public static Mesh FromCircle(Extent baseExt, double cellSize)
	{
		double scaledWidth = baseExt.Width / 2.0;
		double cellSize2 = cellSize;
		List<Point> points = new List<Point>();
		List<Point> pointsList = new List<Point>();
		points.Add(new Point(0.0, 0.0));
		int count = (int)Math.Round(scaledWidth / cellSize2 + 0.49999);
		cellSize2 = scaledWidth / ((double)count - 0.5);
		int count2 = 1;
		int count2Term = 1;
		double piOverCount2Sum = 0.0;
		double cellSize2Term = 0.0;
		for (int i = 1; i < count; i++)
		{
			cellSize2Term = cellSize2 * (double)i;
			count2 = (int)Math.Pow(2.0, Math.Ceiling(Math.Log2(Math.PI * 2.0 * cellSize2Term / cellSize2)));
			if (count2 != count2Term)
			{
				piOverCount2Sum += Math.PI / (double)count2;
			}
			for (int j = 0; j < count2; j++)
			{
				double piCount2Term = piOverCount2Sum + Math.PI * 2.0 * (double)j / (double)count2;
				points.Add(new Point(cellSize2Term * Math.Cos(piCount2Term), cellSize2Term * Math.Sin(piCount2Term)));
			}
			count2Term = count2;
		}
		piOverCount2Sum += Math.PI / (double)count2;
		scaledWidth = ((double)count - 0.5) * cellSize2;
		for (int k = 0; k < count2; k++)
		{
			double piCount2Term2 = piOverCount2Sum + Math.PI * 2.0 * (double)k / (double)count2;
			pointsList.Add(new Point(scaledWidth * Math.Cos(piCount2Term2), scaledWidth * Math.Sin(piCount2Term2)));
		}
		Polygon perimeter = new Polygon(pointsList);
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		MeshFactory.TryCreateMesh(perimeter, points, null, out var mesh, out var _, meshGenerationParams);
		return mesh;
	}

	public static Mesh FromCircle(Extent baseExt, Func<double, double> sizeFunction)
	{
		double scaledWidth = baseExt.Width / 2.0;
		List<Point> points = new List<Point>();
		List<Point> pointsList = new List<Point>();
		points.Add(new Point(0.0, 0.0));
		double computed = 0.0;
		int count = 1;
		int countTerm = 1;
		double piOverCountSum = 0.0;
		double sizeFunctionTerm;
		while (true)
		{
			sizeFunctionTerm = sizeFunction(computed);
			int ordinal = (int)Math.Round((scaledWidth - computed) / sizeFunctionTerm + 0.49999);
			if (ordinal <= 0)
			{
				break;
			}
			sizeFunctionTerm = (scaledWidth - computed) / ((double)ordinal - 0.5);
			computed += sizeFunctionTerm;
			if (computed > scaledWidth)
			{
				break;
			}
			count = (int)Math.Pow(2.0, Math.Ceiling(Math.Log2(Math.PI * 2.0 * computed / sizeFunctionTerm)));
			if (count != countTerm)
			{
				piOverCountSum += Math.PI / (double)count;
			}
			for (int i = 0; i < count; i++)
			{
				double piCountTerm = piOverCountSum + Math.PI * 2.0 * (double)i / (double)count;
				points.Add(new Point(computed * Math.Cos(piCountTerm), computed * Math.Sin(piCountTerm)));
			}
			countTerm = count;
		}
		piOverCountSum += Math.PI / (double)count;
		computed -= 0.5 * sizeFunctionTerm;
		for (int j = 0; j < count; j++)
		{
			double piCountTerm2 = piOverCountSum + Math.PI * 2.0 * (double)j / (double)count;
			pointsList.Add(new Point(computed * Math.Cos(piCountTerm2), computed * Math.Sin(piCountTerm2)));
		}
		Polygon perimeter = new Polygon(pointsList);
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		MeshFactory.TryCreateMesh(perimeter, points, null, out var mesh, out var _, meshGenerationParams);
		return mesh;
	}

	public static Mesh FromTwoCircles(double radiusInner, double radiusOuter, double cellSize, double aspectRatio)
	{
		SlidingMesh.DefaultCircumcenterClearance = 0.0;
		Topology topology = new Topology();
		topology.EnsureMeshMetadataColumns();
		double radiusInnerRadiusOuterTerm = (radiusInner + radiusOuter) / 2.0;
		int piCellSizeTerm = Math.Max(360, (int)Math.Floor(Math.PI * 2.0 * radiusInnerRadiusOuterTerm / cellSize));
		double deltaTheta = Math.PI * 2.0 / (double)piCellSizeTerm;
		int[] intValues = new int[8];
		int[] intValuesArray = new int[12];
		topology.AddNode(radiusInner, 0.0, out intValues[0]);
		topology.AddNode(0.0, radiusInner, out intValues[1]);
		topology.AddNode(0.0 - radiusInner, 0.0, out intValues[2]);
		topology.AddNode(0.0, 0.0 - radiusInner, out intValues[3]);
		topology.AddNode(radiusOuter, 0.0, out intValues[4]);
		topology.AddNode(0.0, radiusOuter, out intValues[5]);
		topology.AddNode(0.0 - radiusOuter, 0.0, out intValues[6]);
		topology.AddNode(0.0, 0.0 - radiusOuter, out intValues[7]);
		topology.AddArc(intValues[0], intValues[1], GenerateArcInternalPoints(radiusInner, 0), out intValuesArray[0]);
		topology.AddArc(intValues[1], intValues[2], GenerateArcInternalPoints(radiusInner, 1), out intValuesArray[1]);
		topology.AddArc(intValues[2], intValues[3], GenerateArcInternalPoints(radiusInner, 2), out intValuesArray[2]);
		topology.AddArc(intValues[3], intValues[0], GenerateArcInternalPoints(radiusInner, 3), out intValuesArray[3]);
		topology.AddArc(intValues[4], intValues[5], GenerateArcInternalPoints(radiusOuter, 0), out intValuesArray[4]);
		topology.AddArc(intValues[5], intValues[6], GenerateArcInternalPoints(radiusOuter, 1), out intValuesArray[5]);
		topology.AddArc(intValues[6], intValues[7], GenerateArcInternalPoints(radiusOuter, 2), out intValuesArray[6]);
		topology.AddArc(intValues[7], intValues[4], GenerateArcInternalPoints(radiusOuter, 3), out intValuesArray[7]);
		topology.AddArc(intValues[0], intValues[4], null, out intValuesArray[8]);
		topology.AddArc(intValues[1], intValues[5], null, out intValuesArray[9]);
		topology.AddArc(intValues[2], intValues[6], null, out intValuesArray[10]);
		topology.AddArc(intValues[3], intValues[7], null, out intValuesArray[11]);
		topology.Complete();
		topology.FillRegionsWithNoControlPoints(out var _);
		float tINResolution = -1f;
		topology.SetRegionData(0, new MeshRegionMetadata(PatchType.Empty, SizeFunction.Constant, (float)cellSize)
		{
			RegionRecipe = new TopologyRecipe(),
			Multipoint = true,
			TINResolution = tINResolution
		});
		topology.SetRegionData(1, new MeshRegionMetadata(PatchType.Quad, SizeFunction.Constant, (float)cellSize)
		{
			RegionRecipe = new TopologyRecipe(),
			Multipoint = true,
			TINResolution = tINResolution
		});
		topology.SetRegionData(2, new MeshRegionMetadata(PatchType.Quad, SizeFunction.Constant, (float)cellSize)
		{
			RegionRecipe = new TopologyRecipe(),
			Multipoint = true,
			TINResolution = tINResolution
		});
		topology.SetRegionData(3, new MeshRegionMetadata(PatchType.Quad, SizeFunction.Constant, (float)cellSize)
		{
			RegionRecipe = new TopologyRecipe(),
			Multipoint = true,
			TINResolution = tINResolution
		});
		topology.SetRegionData(4, new MeshRegionMetadata(PatchType.Quad, SizeFunction.Constant, (float)cellSize)
		{
			RegionRecipe = new TopologyRecipe(),
			Multipoint = true,
			TINResolution = tINResolution
		});
		float constSz = (float)(cellSize * 2.0 * radiusInner / (radiusInner + radiusOuter));
		float constSz2 = (float)(cellSize * 2.0 * radiusOuter / (radiusInner + radiusOuter));
		float constSz3 = (float)(cellSize / aspectRatio);
		topology.SetArcConstantSize(intValuesArray[0], constSz);
		topology.SetArcConstantSize(intValuesArray[1], constSz);
		topology.SetArcConstantSize(intValuesArray[2], constSz);
		topology.SetArcConstantSize(intValuesArray[3], constSz);
		topology.SetArcConstantSize(intValuesArray[4], constSz2);
		topology.SetArcConstantSize(intValuesArray[5], constSz2);
		topology.SetArcConstantSize(intValuesArray[6], constSz2);
		topology.SetArcConstantSize(intValuesArray[7], constSz2);
		topology.SetArcConstantSize(intValuesArray[8], constSz3);
		topology.SetArcConstantSize(intValuesArray[9], constSz3);
		topology.SetArcConstantSize(intValuesArray[10], constSz3);
		topology.SetArcConstantSize(intValuesArray[11], constSz3);
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out var finalMesh);
		return finalMesh;
		List<Point> GenerateArcInternalPoints(double R, int quadrant)
		{
			double quadrantPiTerm = (double)quadrant * Math.PI / 2.0;
			double quadrantPiTerm2 = (double)(quadrant + 1) * Math.PI / 2.0;
			List<Point> points = new List<Point>();
			int count = (int)Math.Floor((quadrantPiTerm2 - quadrantPiTerm) / deltaTheta - 1.0);
			for (int i = 0; i < count; i++)
			{
				double deltaThetaTerm = quadrantPiTerm + deltaTheta * (double)(i + 1);
				points.Add(new Point(R * Math.Cos(deltaThetaTerm), R * Math.Sin(deltaThetaTerm)));
			}
			return points;
		}
	}

	public static void FromExtent(Extent baseExt, int cellsW, int cellsH, IResample<float> elevs, IResample<float> nvals, PropertyTableOptions opts, out Mesh mesh, out MeshPropertyTables props, bool generateHexCells = false)
	{
		mesh = FromExtent(baseExt, cellsW, cellsH, generateHexCells);
		if (mesh == null || !mesh.IsValid())
		{
			throw new Exception("Mesh generation problem.");
		}
		OperationResult operationResult = MeshPropertyTables.ComputeFrom(mesh, elevs, nvals, opts, out props);
		if (!operationResult)
		{
			throw new Exception("Could not compute property tables: " + operationResult.GetConcatenatedMessages());
		}
	}

	public static void FromCircle(Extent baseExt, double cellSize, IResample<float> elevs, IResample<float> nvals, PropertyTableOptions opts, out Mesh mesh, out MeshPropertyTables props)
	{
		mesh = FromCircle(baseExt, cellSize);
		if (mesh == null || !mesh.IsValid())
		{
			throw new Exception("Mesh generation problem.");
		}
		OperationResult operationResult = MeshPropertyTables.ComputeFrom(mesh, elevs, nvals, opts, out props);
		if (!operationResult)
		{
			throw new Exception("Could not compute property tables: " + operationResult.GetConcatenatedMessages());
		}
	}

	public static void FromCircle(Extent baseExt, Func<double, double> sizeFunction, IResample<float> elevs, IResample<float> nvals, PropertyTableOptions opts, out Mesh mesh, out MeshPropertyTables props)
	{
		mesh = FromCircle(baseExt, sizeFunction);
		if (mesh == null || !mesh.IsValid())
		{
			throw new Exception("Mesh generation problem.");
		}
		OperationResult operationResult = MeshPropertyTables.ComputeFrom(mesh, elevs, nvals, opts, out props);
		if (!operationResult)
		{
			throw new Exception("Could not compute property tables: " + operationResult.GetConcatenatedMessages());
		}
	}

	public static Mesh FourCellFlatMesh(out MeshPropertyTables props)
	{
		Polygon perim = Polygon.FromExtent(new Extent(0.0, 2.0, 0.0, 2.0));
		int count = 4;
		int count2 = 12;
		Face face = new Face(0, -1);
		face.fpA = 0;
		face.fpB = 7;
		Face face2 = face;
		face = new Face(0, -1);
		face.fpA = 1;
		face.fpB = 0;
		Face face3 = face;
		face = new Face(1, -1);
		face.fpA = 2;
		face.fpB = 1;
		Face face4 = face;
		face = new Face(1, -1);
		face.fpA = 3;
		face.fpB = 2;
		Face face5 = face;
		face = new Face(3, -1);
		face.fpA = 4;
		face.fpB = 3;
		Face face6 = face;
		face = new Face(3, -1);
		face.fpA = 5;
		face.fpB = 4;
		Face face7 = face;
		face = new Face(2, -1);
		face.fpA = 6;
		face.fpB = 5;
		Face face8 = face;
		face = new Face(2, -1);
		face.fpA = 7;
		face.fpB = 6;
		Face face9 = face;
		face = new Face(2, 0);
		face.fpA = 8;
		face.fpB = 7;
		Face face10 = face;
		face = new Face(0, 1);
		face.fpA = 8;
		face.fpB = 1;
		Face face11 = face;
		face = new Face(1, 3);
		face.fpA = 8;
		face.fpB = 3;
		Face face12 = face;
		face = new Face(3, 2);
		face.fpA = 8;
		face.fpB = 5;
		Face face13 = face;
		Face[] faces = new Face[12]
		{
			face2, face3, face4, face5, face6, face7, face8, face9, face10, face11,
			face12, face13
		};
		float computed = 0f;
		float faceManningsTable = 0.04f;
		float[] floatValues = new float[count];
		for (int i = 0; i < count; i++)
		{
			floatValues[i] = computed;
		}
		List<Point> points = new List<Point>();
		points.Add(new Point(0.5, 1.5));
		points.Add(new Point(1.5, 1.5));
		points.Add(new Point(0.5, 0.5));
		points.Add(new Point(1.5, 0.5));
		Cell cell = default;
		cell.Faces = new int[4] { 1, 0, 8, 9 };
		Cell cell2 = default;
		cell2.Faces = new int[4] { 2, 9, 10, 3 };
		Cell cell3 = default;
		cell3.Faces = new int[4] { 8, 7, 6, 11 };
		Cell cell4 = default;
		cell4.Faces = new int[4] { 10, 11, 5, 4 };
		Cell[] cells = new Cell[4] { cell, cell2, cell3, cell4 };
		FacePoint facePoint = new FacePoint(0.0, 2.0);
		facePoint.Faces = new int[2] { 0, 1 };
		FacePoint facePoint2 = facePoint;
		facePoint = new FacePoint(1.0, 2.0);
		facePoint.Faces = new int[3] { 1, 9, 2 };
		FacePoint facePoint3 = facePoint;
		facePoint = new FacePoint(2.0, 2.0);
		facePoint.Faces = new int[2] { 2, 3 };
		FacePoint facePoint4 = facePoint;
		facePoint = new FacePoint(2.0, 1.0);
		facePoint.Faces = new int[3] { 3, 10, 4 };
		FacePoint facePoint5 = facePoint;
		facePoint = new FacePoint(2.0, 0.0);
		facePoint.Faces = new int[2] { 4, 5 };
		FacePoint facePoint6 = facePoint;
		facePoint = new FacePoint(1.0, 0.0);
		facePoint.Faces = new int[3] { 5, 11, 6 };
		FacePoint facePoint7 = facePoint;
		facePoint = new FacePoint(0.0, 0.0);
		facePoint.Faces = new int[2] { 6, 7 };
		FacePoint facePoint8 = facePoint;
		facePoint = new FacePoint(0.0, 1.0);
		facePoint.Faces = new int[3] { 7, 8, 0 };
		FacePoint facePoint9 = facePoint;
		facePoint = new FacePoint(1.0, 1.0);
		facePoint.Faces = new int[4] { 8, 11, 10, 9 };
		FacePoint facePoint10 = facePoint;
		FacePoint[] facePoints = new FacePoint[9] { facePoint2, facePoint3, facePoint4, facePoint5, facePoint6, facePoint7, facePoint8, facePoint9, facePoint10 };
		Mesh mesh = new Mesh(perim, cells, faces, facePoints, points.ToArray(), null);
		props = MeshPropertyTables.CreateEmpty(mesh);
		props.InitCellTables(count * 2);
		props.InitFaceTables(count2 * 2);
		for (int j = 0; j < count; j++)
		{
			props.CellTableStart[j] = j * 2;
			props.CellTableCount[j] = 2;
			props.CellElevationTable[j * 2] = 0f;
			props.CellVolumeTable[j * 2] = 0f;
			props.CellElevationTable[j * 2 + 1] = 0.1f;
			props.CellVolumeTable[j * 2 + 1] = 0.1f;
		}
		props.ComputeCellWettedAreaTables();
		for (int k = 0; k < count2; k++)
		{
			props.FaceTableStart[k] = k * 2;
			props.FaceTableCount[k] = 2;
			props.FaceElevationTable[k * 2] = 0f;
			props.FaceAreaTable[k * 2] = 0f;
			props.FaceElevationTable[k * 2 + 1] = 0.1f;
			props.FaceAreaTable[k * 2 + 1] = 0.1f;
			props.FaceManningsTable[k * 2] = faceManningsTable;
			props.FaceManningsTable[k * 2 + 1] = faceManningsTable;
			props.FaceWettedPerimeterTable[k * 2] = 0f;
			props.FaceWettedPerimeterTable[k * 2 + 1] = 1f;
		}
		props.ComputeFaceHydraulicRadiusTables();
		return mesh;
	}

	public static Mesh FourTriangleFlatMesh(out MeshPropertyTables props)
	{
		Polygon perim = Polygon.FromExtent(new Extent(0.0, 2.0, 0.0, 2.0));
		int count = 4;
		int count2 = 8;
		Face face = new Face(2, -1);
		face.fpA = 0;
		face.fpB = 1;
		Face face2 = face;
		face = new Face(1, -1);
		face.fpA = 1;
		face.fpB = 2;
		Face face3 = face;
		face = new Face(0, -1);
		face.fpA = 2;
		face.fpB = 3;
		Face face4 = face;
		face = new Face(3, -1);
		face.fpA = 3;
		face.fpB = 0;
		Face face5 = face;
		face = new Face(3, 2);
		face.fpA = 0;
		face.fpB = 4;
		Face face6 = face;
		face = new Face(2, 1);
		face.fpA = 1;
		face.fpB = 4;
		Face face7 = face;
		face = new Face(1, 0);
		face.fpA = 2;
		face.fpB = 4;
		Face face8 = face;
		face = new Face(0, 3);
		face.fpA = 3;
		face.fpB = 4;
		Face face9 = face;
		Face[] faces = new Face[8] { face2, face3, face4, face5, face6, face7, face8, face9 };
		float faceManningsTable = 0.04f;
		List<Point> points = new List<Point>();
		points.Add(new Point(1.0, 1.6666666666666667));
		points.Add(new Point(1.6666666666666667, 1.0));
		points.Add(new Point(1.0, 1.0 / 3.0));
		points.Add(new Point(1.0 / 3.0, 1.0));
		Cell cell = default;
		cell.Faces = new int[3] { 2, 7, 6 };
		Cell cell2 = default;
		cell2.Faces = new int[3] { 1, 6, 5 };
		Cell cell3 = default;
		cell3.Faces = new int[3] { 0, 5, 4 };
		Cell cell4 = default;
		cell4.Faces = new int[3] { 3, 4, 7 };
		Cell[] cells = new Cell[4] { cell, cell2, cell3, cell4 };
		FacePoint facePoint = new FacePoint(0.0, 0.0);
		facePoint.Faces = new int[3] { 0, 4, 3 };
		FacePoint facePoint2 = facePoint;
		facePoint = new FacePoint(2.0, 0.0);
		facePoint.Faces = new int[3] { 1, 5, 0 };
		FacePoint facePoint3 = facePoint;
		facePoint = new FacePoint(2.0, 2.0);
		facePoint.Faces = new int[3] { 2, 6, 1 };
		FacePoint facePoint4 = facePoint;
		facePoint = new FacePoint(0.0, 2.0);
		facePoint.Faces = new int[3] { 3, 7, 2 };
		FacePoint facePoint5 = facePoint;
		facePoint = new FacePoint(1.0, 1.0);
		facePoint.Faces = new int[4] { 6, 7, 4, 5 };
		FacePoint facePoint6 = facePoint;
		FacePoint[] facePoints = new FacePoint[5] { facePoint2, facePoint3, facePoint4, facePoint5, facePoint6 };
		Mesh mesh = new Mesh(perim, cells, faces, facePoints, points.ToArray(), null);
		props = MeshPropertyTables.CreateEmpty(mesh);
		props.InitCellTables(count * 2);
		props.InitFaceTables(count2 * 2);
		for (int i = 0; i < count; i++)
		{
			props.CellTableStart[i] = i * 2;
			props.CellTableCount[i] = 2;
			props.CellElevationTable[i * 2] = 0f;
			props.CellVolumeTable[i * 2] = 0f;
			props.CellElevationTable[i * 2 + 1] = 0.1f;
			props.CellVolumeTable[i * 2 + 1] = 0.1f;
		}
		props.ComputeCellWettedAreaTables();
		for (int j = 0; j < count2; j++)
		{
			float faceWettedPerimeterTable = ((j < 4) ? 2f : ((float)Math.Sqrt(2.0)));
			props.FaceTableStart[j] = j * 2;
			props.FaceTableCount[j] = 2;
			props.FaceElevationTable[j * 2] = 0f;
			props.FaceAreaTable[j * 2] = 0f;
			props.FaceElevationTable[j * 2 + 1] = 0.1f;
			props.FaceAreaTable[j * 2 + 1] = 0.1f * faceWettedPerimeterTable;
			props.FaceManningsTable[j * 2] = faceManningsTable;
			props.FaceManningsTable[j * 2 + 1] = faceManningsTable;
			props.FaceWettedPerimeterTable[j * 2] = 0f;
			props.FaceWettedPerimeterTable[j * 2 + 1] = faceWettedPerimeterTable;
		}
		props.ComputeFaceHydraulicRadiusTables();
		return mesh;
	}

	public static Mesh BasicTwoCellMesh(out MeshPropertyTables props, float c1Elev = 0f, float c2Elev = 0f, float fElev = 0f)
	{
		Polygon perim = Polygon.FromExtent(new Extent(0.0, 2.0, 0.0, 1.0));
		float[] cellMinimumElevations = new float[2] { c1Elev, c2Elev };
		List<Point> points = new List<Point>();
		points.Add(new Point(0.5, 0.5));
		Cell cell = default;
		cell.Faces = new int[2] { 0, 1 };
		points.Add(new Point(1.5, 0.5));
		Cell cell2 = default;
		cell2.Faces = new int[2] { 1, 2 };
		Cell[] cells = new Cell[2] { cell, cell2 };
		Face face = new Face(0, -1);
		face.fpA = 0;
		face.fpB = 1;
		Face face2 = face;
		face = new Face(0, 1);
		face.fpA = 0;
		face.fpB = 1;
		Face face3 = face;
		face = new Face(1, -1);
		face.fpA = 1;
		face.fpB = 0;
		Face face4 = face;
		Face[] faces = new Face[3] { face2, face3, face4 };
		float[] faceMinimumElevations = new float[3] { 0f, fElev, 0f };
		FacePoint facePoint = new FacePoint(1.0, 1.0);
		facePoint.Faces = new int[3] { 0, 1, 2 };
		FacePoint facePoint2 = new FacePoint(1.0, 0.0);
		facePoint2.Faces = new int[3] { 0, 1, 2 };
		FacePoint[] facePoints = new FacePoint[2] { facePoint, facePoint2 };
		Memory<Point>[] pointSpanArray = new Memory<Point>[faces.Length];
		pointSpanArray[0] = new Point[2]
		{
			new Point(0.0, 1.0),
			new Point(0.0, 0.0)
		};
		pointSpanArray[2] = new Point[2]
		{
			new Point(2.0, 0.0),
			new Point(2.0, 1.0)
		};
		Mesh mesh = new Mesh(perim, cells, faces, facePoints, points.ToArray(), pointSpanArray);
		props = MeshPropertyTables.DebugAssignMinElevations(mesh, 0f);
		props.CellMinimumElevations = cellMinimumElevations;
		props.FaceMinimumElevations = faceMinimumElevations;
		return mesh;
	}
}
