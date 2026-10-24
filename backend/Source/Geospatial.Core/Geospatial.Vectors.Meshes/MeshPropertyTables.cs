using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Utility;
using Utility.Logging;
using Utility.Progress;
using Utility.Systems;
using Utility.Unsafe;

namespace Geospatial.Vectors.Meshes;

public class MeshPropertyTables
{
	private struct PartialCellProps
	{
		public const int PointsPerPartial = 3;

		public const int SegmentsPerPartial = 2;

		public float Vol1;

		public float Elev1;

		public float Vol2;

		public float Elev2;

		public float Vol3;

		public float Elev3;

		public float FinalArea;

		private ushort _data;

		private const ushort TOP = 32768;

		private const ushort NTOP = 32767;

		public float VolTOP => Vol3;

		public float Area12 => (Vol2 - Vol1) / (Elev2 - Elev1);

		public float Area23 => (Vol3 - Vol2) / (Elev3 - Elev2);

		public bool IsTopOfTable
		{
			get
			{
				return (_data & 0x8000) != 0;
			}
			set
			{
				if (value)
				{
					_data |= 32768;
				}
				else
				{
					_data &= 32767;
				}
			}
		}

		public bool IsBottomOfTable => Vol1 == 0f;

		public ushort Index
		{
			get
			{
				return (ushort)(_data & 0x7FFF);
			}
			set
			{
				_data = (ushort)((_data & 0x8000) | value);
			}
		}
	}

	private static class PropertyTableErrors
	{
		public static readonly MessageClass TerrainMissing = new MessageClass("PropertyTables_TerrainMissing", MessageSeverity.Error, "Missing terrain data near ({Coordinate}).");

		public static readonly MessageClass FaceMissingTerrain = new MessageClass("PropertyTables_FaceMissingTerrain", MessageSeverity.Error, "Missing terrain data at Face {FaceIndex}.");

		public static Message GetTerrainMissing(Point pt)
		{
			return TerrainMissing.Create(pt);
		}

		public static Message GetFaceMissingTerrain(int fIdx)
		{
			return FaceMissingTerrain.Create(fIdx);
		}
	}

	public Mesh Mesh;

	public const bool USEPARTIALTABLES = false;

	public const bool PARTIAL_DIAGNOSTICS = false;

	public int REUSE;

	public int RECENTER;

	public int[] CellTableStart;

	public short[] CellTableCount;

	public float[] CellVolumeTable;

	public float[] CellElevationTable;

	public float[] CellMinimumElevations;

	public float[] FaceMinimumElevations;

	public float[] CellWettedAreaTable;

	private PartialCellProps[] _partialCellProps;

	public int[] FaceTableStart;

	public short[] FaceTableCount;

	public float[] FaceAreaTable;

	public float[] FaceElevationTable;

	public float[] FaceWettedPerimeterTable;

	public float[] FaceHydraulicRadiusTable;

	public float[] FaceHydraulicDepthTable;

	public float[] FaceConveyanceTable;

	public float[] FaceConveyanceA;

	public float[] FaceConveyanceB;

	public float[] FaceMeanElevation;

	public float[] FaceManningsTable;

	public float[] FaceBedFrictVarTable;

	public float[] FaceBedFrictVarA;

	public float[] FaceBedFrictVarB;

	public float[] FaceShearFluxVarTable;

	public float[] FaceShearFluxVarA;

	public float[] FaceShearFluxVarB;

	public double ConstNonlinBedFrictionCoeff;

	public bool calcFaceWettedPerimeter;

	public bool calcFaceConveyance;

	public bool calcFaceHydrRadius;

	public bool calcFaceHydrDepth;

	public bool calcFaceManning;

	public bool calcFaceBedFrictCoeff;

	public bool calcFaceShearFluxCoeff;

	public bool calcConstBedFrictCoeff;

	private MeshPropertyTables(Mesh m)
	{
		Mesh = m;
	}

	public static OperationResult ComputeFrom(Mesh mesh, IResample<float> elevations, IResample<float> nValues, PropertyTableOptions opts, out MeshPropertyTables tables, ProgressReporter pr = null)
	{
		return ComputeSimple(mesh, elevations, nValues, opts, out tables, pr);
	}

	public static MeshPropertyTables CreateEmpty(Mesh mesh)
	{
		int cellCount = mesh.CellCount;
		int faceCount = mesh.FaceCount;
		return new MeshPropertyTables(mesh)
		{
			CellTableStart = new int[cellCount],
			CellTableCount = new short[cellCount],
			FaceTableStart = new int[faceCount],
			FaceTableCount = new short[faceCount]
		};
	}

	public static MeshPropertyTables DebugAssignMinElevations(Mesh mesh, float elev)
	{
		MeshPropertyTables meshPropertyTables = CreateEmpty(mesh);
		meshPropertyTables.CellMinimumElevations = Enumerable.Repeat(elev, mesh.CellCount).ToArray();
		meshPropertyTables.FaceMinimumElevations = Enumerable.Repeat(elev, mesh.FaceCount).ToArray();
		return meshPropertyTables;
	}

	private static OperationResult ComputeSimple(Mesh mesh, IResample<float> elevations, IResample<float> nValues, PropertyTableOptions opts, out MeshPropertyTables tables, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		tables = CreateEmpty(mesh);
		HydraulicProfile[] allFaceProps = new HydraulicProfile[mesh.FaceCount];
		float[] fMann = new float[mesh.FaceCount];
		pr.ReportMessage("Sampling Face Profiles...");
		SpatialIndex<int> spIdx = mesh.ComputeUniqueFaceSpatialIndexBySimpleMidpoint();
		ProgressReporter subPr = pr.WithProgressModifier(0, 0.33f);
		int netTerrainFaceErrors = 0;
		List<int> terrainFaceErrors = new List<int>();
		int binsSolved = 0;
		int totalFaceBinCt = spIdx.Bins.Length;
		System.Threading.Tasks.Parallel.For(0, totalFaceBinCt, (int binIdx) =>
		{
			List<int> bin = spIdx.Bins[binIdx];
			if (bin != null && bin.Count != 0 && !subPr.CancelRequested && netTerrainFaceErrors <= 100)
			{
				HydraulicProfileBuilderHelper helper = new HydraulicProfileBuilderHelper();
				int count = (int)((float)opts.SampledPointsPerFace * 1.5f);
				Memory<Point> points = new Point[count];
				Span<Point> pointsSpan = points.Span;
				List<Point> pointsList = new List<Point>((int)((float)(bin.Count * count) * 1.5f));
				Point[] pointsArray = new Point[bin.Count];
				int[] intValues = new int[bin.Count];
				Polyline polyline = new Polyline();
				List<int> intList = new List<int>();
				for (int i = 0; i < bin.Count; i++)
				{
					int bin2 = bin[i];
					if (mesh.FaceInternalPoints[bin2].IsEmpty)
					{
						Segment segment = mesh.FaceSegment(bin2);
						pointsArray[i] = segment.PointAtFraction(0.5);
						double maxLength = segment.Length / (double)opts.SampledPointsPerFace;
						Span<Point> outPoints = pointsSpan.Slice(1, points.Length - 1);
						int internalDensificationPoints = segment.GetInternalDensificationPoints(maxLength, outPoints);
						int internalDensificationPointsOffset = internalDensificationPoints + 2;
						if (internalDensificationPoints > outPoints.Length)
						{
							points = new Point[(int)((float)internalDensificationPointsOffset * 1.5f)];
							pointsSpan = points.Span;
							segment.GetInternalDensificationPoints(maxLength, pointsSpan.Slice(1));
						}
						pointsSpan[0] = segment.A;
						pointsSpan[internalDensificationPoints + 1] = segment.B;
						pointsList.AddRange(points.Slice(0, internalDensificationPointsOffset).Span);
						intValues[i] = internalDensificationPointsOffset;
					}
					else
					{
						polyline.Clear();
						mesh.FacePolylineInto(bin2, polyline);
						pointsArray[i] = polyline.PointAtFraction(0.5).Point;
						double length = polyline.Length;
						polyline.Densify(length / (double)opts.SampledPointsPerFace);
						Memory<Point> pointBufferUnsafe = polyline.GetPointBufferUnsafe();
						pointsList.AddRange(pointBufferUnsafe.Span);
						intValues[i] = pointBufferUnsafe.Length;
					}
				}
				Memory<Point> listBackingBuffer = UnsafeHelpers.GetListBackingBuffer(pointsList);
				Span<Point> listBackingBufferSpan = listBackingBuffer.Span;
				float[] samplePoints = elevations.SamplePoints(listBackingBuffer);
				float[] samplePointsArray = nValues.SamplePoints(pointsArray);
				int ordinal = 0;
				ProfileBuilder profileBuilder2 = new ProfileBuilder();
				for (int j = 0; j < bin.Count; j++)
				{
					int bin3 = bin[j];
					profileBuilder2.Clear();
					int ordinal2 = intValues[j];
					double distanceToSum = 0.0;
					for (int k = ordinal; k < ordinal + ordinal2; k++)
					{
						float samplePoint = samplePoints[k];
						if (samplePoint != -9999f)
						{
							Point point = listBackingBufferSpan[k];
							if (k > ordinal)
							{
								distanceToSum += point.DistanceTo(listBackingBufferSpan[k - 1]);
							}
							profileBuilder2.Add(distanceToSum, samplePoint);
						}
					}
					ordinal += ordinal2;
					if (profileBuilder2.PointCount == 0)
					{
						intList.Add(bin3);
					}
					else
					{
						profileBuilder2.FilterSelf(opts.FaceProfileFilterTolerance);
						fMann[bin3] = samplePointsArray[j];
						HydraulicProfile hydraulicProfile = HydraulicProfile.Build(profileBuilder2, opts.FaceAreaElevFilterTolerance, opts.FaceAreaConveyanceRatio, opts.FaceLaminarDepthTolerance, helper);
						allFaceProps[bin3] = hydraulicProfile;
					}
				}
				if (intList.Count > 0)
				{
					lock (terrainFaceErrors)
					{
						terrainFaceErrors.AddRange(intList);
						netTerrainFaceErrors = terrainFaceErrors.Count;
					}
				}
				int numerator = Interlocked.Increment(ref binsSolved);
				subPr.ReportProgressFraction(numerator, totalFaceBinCt);
			}
		});
		if (subPr.CancelRequested)
		{
			return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Computing Property Tables"));
		}
		if (terrainFaceErrors.Count > 0)
		{
			OperationResult operationResult = new OperationResult();
			for (int intervalIndex = 0; intervalIndex < Math.Min(terrainFaceErrors.Count, 10); intervalIndex++)
			{
				operationResult.AddFailure(PropertyTableErrors.GetFaceMissingTerrain(terrainFaceErrors[intervalIndex]));
			}
			if (terrainFaceErrors.Count > 10 && terrainFaceErrors.Count < 100)
			{
				operationResult.AddFailure("(and " + (terrainFaceErrors.Count - 10) + " more)");
			}
			else if (terrainFaceErrors.Count > 100)
			{
				operationResult.AddFailure("(and " + 90 + "+ more)");
			}
			return operationResult;
		}
		subPr.ReportProgress(100);
		pr.ReportMessage("Face profile cut: " + stopwatch.Elapsed);
		OperationResult operationResult2 = CheckReportBadNValues(fMann, pr);
		if (!operationResult2)
		{
			return operationResult2;
		}
		stopwatch.Restart();
		pr.ReportMessage("Building Face Tables...");
		int totalFacePropCount = allFaceProps.Sum((HydraulicProfile fp) => fp.HydraulicallyCorrectedProperties.Length);
		tables.InitFaceTables(totalFacePropCount);
		int hydraulicallyCorrectedPropertyLengthSum = 0;
		for (int intervalIndex2 = 0; intervalIndex2 < mesh.FaceCount; intervalIndex2++)
		{
			if (intervalIndex2 % 128 == 0 && pr.CancelRequested)
			{
				return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Computing Property Tables"));
			}
			Span<HydraulicProperties> hydraulicallyCorrectedProperties = allFaceProps[intervalIndex2].HydraulicallyCorrectedProperties.Span;
			short hydraulicallyCorrectedPropertyLength = (short)hydraulicallyCorrectedProperties.Length;
			tables.FaceTableStart[intervalIndex2] = hydraulicallyCorrectedPropertyLengthSum;
			tables.FaceTableCount[intervalIndex2] = hydraulicallyCorrectedPropertyLength;
			for (int intervalIndex3 = 0; intervalIndex3 < hydraulicallyCorrectedPropertyLength; intervalIndex3++)
			{
				HydraulicProperties hydraulicProperties = hydraulicallyCorrectedProperties[intervalIndex3];
				tables.FaceAreaTable[hydraulicallyCorrectedPropertyLengthSum + intervalIndex3] = (float)hydraulicProperties.Area;
				tables.FaceElevationTable[hydraulicallyCorrectedPropertyLengthSum + intervalIndex3] = (float)hydraulicProperties.Elevation;
				tables.FaceWettedPerimeterTable[hydraulicallyCorrectedPropertyLengthSum + intervalIndex3] = (float)hydraulicProperties.WettedPerimeter;
				tables.FaceManningsTable[hydraulicallyCorrectedPropertyLengthSum + intervalIndex3] = fMann[intervalIndex2];
			}
			hydraulicallyCorrectedPropertyLengthSum += hydraulicallyCorrectedPropertyLength;
		}
		tables.RebuildFaceMinElevationsFromFullTable();
		pr.ReportMessage("Face profile computed props: " + stopwatch.Elapsed);
		stopwatch.Restart();
		pr.ReportMessage("Sampling Cell Elevations...");
		subPr = pr.WithProgressModifier(33, 0.67f);
		int pointCountSum = 0;
		Profile[] profiles = new Profile[mesh.CellCount];
		float[] floatValues = new float[opts.SampledPointsPerCell];
		ProfileBuilder profileBuilder = new ProfileBuilder();
		for (int intervalIndex4 = 0; intervalIndex4 < mesh.CellCount; intervalIndex4++)
		{
			Cell cell = mesh.Cells[intervalIndex4];
			RasterDefinition rasterDefinition = RasterDefinition.ComputeBoundingRaster(mesh.CellExtent(intervalIndex4), opts.SampledPointsPerCell);
			Polygon polygon = mesh.CellPolygon(intervalIndex4);
			double area = polygon.Area;
			if (area <= 0.0)
			{
				return OperationResult.Fail(Mesh.MessageCodes.GetBadCellPolygon(intervalIndex4, mesh.CellCenters[intervalIndex4]));
			}
			if (rasterDefinition.CellCount > floatValues.Length)
			{
				floatValues = new float[rasterDefinition.CellCount];
			}
			Memory<float> slice = floatValues.AsMemory().Slice(0, rasterDefinition.CellCount);
			elevations.Resample(new ResampleArgs(rasterDefinition), slice);
			Histogram histogram = Histogram.ComputeFrom(slice, rasterDefinition, polygon, opts.BinsPerCell);
			if (histogram.IsNull())
			{
				return OperationResult.Fail(PropertyTableErrors.GetTerrainMissing(mesh.CellCenters[intervalIndex4]));
			}
			profileBuilder.Clear();
			if (histogram.IsSingleValue())
			{
				double binCenter = histogram.BinCenter(0);
				profileBuilder.Add(0.0, binCenter);
				profileBuilder.Add(0.1 * area, binCenter + 0.1);
			}
			else
			{
				double minimum = histogram.Minimum;
				PointCollection pointCollection = histogram.ComputeCumulativeValues();
				double x = area / pointCollection.Last().X;
				pointCollection.RatioX(x);
				profileBuilder.Add(0.0, minimum);
				double computed = 0.0;
				for (int unity = 1; unity < pointCollection.Count; unity++)
				{
					double pointCollectionTerm = 0.5 * (pointCollection[unity - 1].X + pointCollection[unity].X);
					double pointCollectionTerm2 = pointCollection[unity].Y - pointCollection[unity - 1].Y;
					computed += pointCollectionTerm2 * pointCollectionTerm;
					profileBuilder.Add(computed, pointCollection[unity].Y);
				}
				if (opts.CellVolumeFilterTolerance > 0.0)
				{
					profileBuilder.FilterSelf(opts.CellVolumeFilterTolerance);
				}
				if (opts.CellMinAreaFraction > 0.0)
				{
					double areaTimesCellMinAreaFraction = area * opts.CellMinAreaFraction;
					for (int unity2 = 1; unity2 < profileBuilder.PointCount; unity2++)
					{
						_ = profileBuilder.X[unity2] / (profileBuilder.Y[unity2] - profileBuilder.Y[0]);
					}
				}
				if (opts.UseSimpleTables)
				{
					throw new NotImplementedException("UseSimpleTables");
				}
			}
			double maxValue = double.MaxValue;
			Span<int> faces = cell.Faces.Span;
			for (int intervalIndex5 = 0; intervalIndex5 < faces.Length; intervalIndex5++)
			{
				int fIdx = faces[intervalIndex5];
				maxValue = Math.Min(maxValue, tables.FaceMinElevation(fIdx));
			}
			if (maxValue < profileBuilder.Y[0])
			{
				profileBuilder.Y[0] = maxValue;
			}
			profiles[intervalIndex4] = profileBuilder.Build();
			pointCountSum += profileBuilder.PointCount;
			if (intervalIndex4 % 100 == 0)
			{
				subPr.ReportProgressFraction(intervalIndex4, mesh.CellCount);
			}
			if (subPr.CancelRequested)
			{
				return null;
			}
		}
		subPr.ReportProgress(100);
		tables.InitCellTables(pointCountSum);
		pr.ReportMessage("Building Cell Tables...");
		int pointCountSum2 = 0;
		for (int intervalIndex6 = 0; intervalIndex6 < mesh.CellCount; intervalIndex6++)
		{
			Profile profile = profiles[intervalIndex6];
			int pointCount = profile.PointCount;
			tables.CellTableStart[intervalIndex6] = pointCountSum2;
			tables.CellTableCount[intervalIndex6] = (short)pointCount;
			for (int intervalIndex7 = 0; intervalIndex7 < pointCount; intervalIndex7++)
			{
				float cellVolumeTable = (float)profile.X[intervalIndex7];
				float cellElevationTable = (float)profile.Y[intervalIndex7];
				_ = 0;
				tables.CellVolumeTable[pointCountSum2 + intervalIndex7] = cellVolumeTable;
				tables.CellElevationTable[pointCountSum2 + intervalIndex7] = cellElevationTable;
			}
			pointCountSum2 += pointCount;
		}
		pr.ReportMessage("Cell Volume/Elevation tables: " + stopwatch.Elapsed);
		stopwatch.Restart();
		pr.ReportMessage("Computing other Cell Properties...");
		tables.ComputeCellWettedAreaTables();
		pr.ReportMessage("Cell wetted area tables: " + stopwatch.Elapsed);
		pr.ReportProgress(100);
		pr.ReportMessage("Computed Property Tables");
		tables.RebuildMinElevationsFromFullTables();
		return true;
	}

	private static OperationResult CheckReportBadNValues(float[] fMann, ProgressReporter pr)
	{
		int ordinal = 0;
		int ordinal2 = 0;
		List<int> intList = null;
		List<int> intListList = null;
		for (int i = 0; i < fMann.Length; i++)
		{
			float fMann2 = fMann[i];
			if (fMann2 < 0f)
			{
				if (intList == null)
				{
					intList = new List<int>();
				}
				if (intList.Count < 5)
				{
					intList.Add(i);
				}
				ordinal++;
			}
			else if (fMann2 == 0f)
			{
				if (intListList == null)
				{
					intListList = new List<int>();
				}
				if (intListList.Count < 5)
				{
					intListList.Add(i);
				}
				ordinal2++;
			}
		}
		if (ordinal2 > 0)
		{
			pr.ReportMessage($"Warning: n-value of 0 detected on {ordinal2} faces in your mesh. If this is unintended, check your geometry's n-value association. FaceIDs: (" + string.Join(',', intListList) + "...)");
		}
		if (ordinal > 0)
		{
			pr.ReportMessage($"Error: Bad/Missing n-values detected on {ordinal} faces in your mesh. FaceIDs: (" + string.Join(',', intList) + "...)");
			return OperationResult.Fail("Negative n-values in mesh.");
		}
		return true;
	}

	public static MeshPropertyTables Reduce(MeshPropertyTables src)
	{
		MeshPropertyTables meshPropertyTables = CreateEmpty(src.Mesh);
		meshPropertyTables.InitCellTables(meshPropertyTables.Mesh.CellCount);
		for (int i = 0; i < meshPropertyTables.Mesh.CellCount; i++)
		{
			int cellTableStart = src.CellTableStart[i];
			short cellTableCount = src.CellTableCount[i];
			int cellTableStartCellTableCountTerm = cellTableStart + cellTableCount - 1;
			meshPropertyTables.CellTableStart[i] = i;
			if (cellTableCount == 0)
			{
				meshPropertyTables.CellTableCount[i] = 0;
				continue;
			}
			meshPropertyTables.CellTableCount[i] = 1;
			meshPropertyTables.CellElevationTable[i] = src.CellElevationTable[cellTableStart];
			meshPropertyTables.CellVolumeTable[i] = 0f;
			meshPropertyTables.CellWettedAreaTable[i] = src.CellWettedAreaTable[cellTableStartCellTableCountTerm];
		}
		meshPropertyTables.InitFaceTables(meshPropertyTables.Mesh.FaceCount);
		for (int j = 0; j < meshPropertyTables.Mesh.FaceCount; j++)
		{
			meshPropertyTables.FaceTableStart[j] = j;
			meshPropertyTables.FaceTableCount[j] = 1;
			int faceTableStart = src.FaceTableStart[j];
			short faceTableCount = src.FaceTableCount[j];
			int faceTableStartFaceTableCountTerm = faceTableStart + faceTableCount - 1;
			meshPropertyTables.FaceElevationTable[j] = src.FaceElevationTable[faceTableStart];
			meshPropertyTables.FaceAreaTable[j] = 0f;
			meshPropertyTables.FaceHydraulicRadiusTable[j] = 0f;
			meshPropertyTables.FaceWettedPerimeterTable[j] = src.FaceWettedPerimeterTable[faceTableStartFaceTableCountTerm];
			meshPropertyTables.FaceManningsTable[j] = src.FaceManningsTable[faceTableStartFaceTableCountTerm];
		}
		for (int k = 0; k < meshPropertyTables.Mesh.FaceCount; k++)
		{
			Face face = meshPropertyTables.Mesh.Faces[k];
			int faceTableStart2 = meshPropertyTables.FaceTableStart[k];
			float faceElevationTable = meshPropertyTables.FaceElevationTable[faceTableStart2];
			faceElevationTable = Math.Max(faceElevationTable, meshPropertyTables.CellElevationTable[meshPropertyTables.CellTableStart[face.cellA]]);
			if (face.cellB >= 0)
			{
				faceElevationTable = Math.Max(faceElevationTable, meshPropertyTables.CellElevationTable[meshPropertyTables.CellTableStart[face.cellB]]);
			}
			meshPropertyTables.FaceElevationTable[faceTableStart2] = faceElevationTable;
		}
		return meshPropertyTables;
	}

	public MeshPropertyTables Subset(Mesh subsetMesh, int[] cellMapping, (int, int)[] faceMapping)
	{
		MeshPropertyTables meshPropertyTables = ShallowCopy(subsetMesh);
		for (int i = 0; i < cellMapping.Length; i++)
		{
			int cellMapping2 = cellMapping[i];
			meshPropertyTables.CellTableStart[i] = CellTableStart[cellMapping2];
			meshPropertyTables.CellTableCount[i] = CellTableCount[cellMapping2];
		}
		for (int j = 0; j < faceMapping.Length; j++)
		{
			int faceMappingTerm = faceMapping[j].Item1;
			meshPropertyTables.FaceTableStart[j] = FaceTableStart[faceMappingTerm];
			meshPropertyTables.FaceTableCount[j] = FaceTableCount[faceMappingTerm];
		}
		return meshPropertyTables;
	}

	public void InitFaceTables(int totalFacePropCount)
	{
		FaceAreaTable = new float[totalFacePropCount];
		FaceElevationTable = new float[totalFacePropCount];
		FaceManningsTable = new float[totalFacePropCount];
		FaceWettedPerimeterTable = new float[totalFacePropCount];
		FaceHydraulicRadiusTable = new float[totalFacePropCount];
	}

	public void InitCellTables(int totalCellPropCount)
	{
		CellVolumeTable = new float[totalCellPropCount];
		CellElevationTable = new float[totalCellPropCount];
		CellWettedAreaTable = new float[totalCellPropCount];
	}

	public void Stream(BinaryWriter bw)
	{
		Pipe.WriteArray(bw, CellTableStart);
		Pipe.WriteArray(bw, CellTableCount);
		Pipe.WriteArray(bw, CellVolumeTable);
		Pipe.WriteArray(bw, CellElevationTable);
		Pipe.WriteArray(bw, CellMinimumElevations);
		Pipe.WriteArray(bw, FaceMinimumElevations);
		Pipe.WriteArray(bw, CellWettedAreaTable);
		Pipe.WriteArray(bw, FaceTableStart);
		Pipe.WriteArray(bw, FaceTableCount);
		Pipe.WriteArray(bw, FaceAreaTable);
		Pipe.WriteArray(bw, FaceElevationTable);
		Pipe.WriteArray(bw, FaceWettedPerimeterTable);
		Pipe.WriteArray(bw, FaceHydraulicRadiusTable);
		Pipe.WriteArray(bw, FaceMeanElevation);
		Pipe.WriteArray(bw, FaceManningsTable);
	}

	public static MeshPropertyTables Stream(BinaryReader br, Mesh m)
	{
		return new MeshPropertyTables(m)
		{
			CellTableStart = Pipe.ReadArray<int>(br),
			CellTableCount = Pipe.ReadArray<short>(br),
			CellVolumeTable = Pipe.ReadArray<float>(br),
			CellElevationTable = Pipe.ReadArray<float>(br),
			CellMinimumElevations = Pipe.ReadArray<float>(br),
			FaceMinimumElevations = Pipe.ReadArray<float>(br),
			CellWettedAreaTable = Pipe.ReadArray<float>(br),
			FaceTableStart = Pipe.ReadArray<int>(br),
			FaceTableCount = Pipe.ReadArray<short>(br),
			FaceAreaTable = Pipe.ReadArray<float>(br),
			FaceElevationTable = Pipe.ReadArray<float>(br),
			FaceWettedPerimeterTable = Pipe.ReadArray<float>(br),
			FaceHydraulicRadiusTable = Pipe.ReadArray<float>(br),
			FaceMeanElevation = Pipe.ReadArray<float>(br),
			FaceManningsTable = Pipe.ReadArray<float>(br)
		};
	}

	public MeshPropertyTables Clone(Mesh withMesh, int[] cellTableStart = null, short[] cellTableCount = null, int[] faceTableStart = null, short[] faceTableCount = null)
	{
		return new MeshPropertyTables(withMesh)
		{
			CellTableStart = (cellTableStart ?? CellTableStart)?.ToArray(),
			CellTableCount = (cellTableCount ?? CellTableCount)?.ToArray(),
			CellVolumeTable = CellVolumeTable?.ToArray(),
			CellElevationTable = CellElevationTable?.ToArray(),
			CellMinimumElevations = CellMinimumElevations?.ToArray(),
			FaceMinimumElevations = FaceMinimumElevations?.ToArray(),
			CellWettedAreaTable = CellWettedAreaTable?.ToArray(),
			FaceTableStart = (faceTableStart ?? FaceTableStart)?.ToArray(),
			FaceTableCount = (faceTableCount ?? FaceTableCount)?.ToArray(),
			FaceAreaTable = FaceAreaTable?.ToArray(),
			FaceElevationTable = FaceElevationTable?.ToArray(),
			FaceWettedPerimeterTable = FaceWettedPerimeterTable?.ToArray(),
			FaceHydraulicRadiusTable = FaceHydraulicRadiusTable?.ToArray(),
			FaceMeanElevation = FaceMeanElevation?.ToArray(),
			FaceManningsTable = FaceManningsTable?.ToArray()
		};
	}

	public MeshPropertyTables ShallowCopy(Mesh withMesh)
	{
		int cellCount = withMesh.CellCount;
		int faceCount = withMesh.FaceCount;
		return new MeshPropertyTables(withMesh)
		{
			CellTableStart = new int[cellCount],
			CellTableCount = new short[cellCount],
			CellVolumeTable = CellVolumeTable,
			CellElevationTable = CellElevationTable,
			CellMinimumElevations = CellMinimumElevations,
			FaceMinimumElevations = FaceMinimumElevations,
			CellWettedAreaTable = CellWettedAreaTable,
			FaceTableStart = new int[faceCount],
			FaceTableCount = new short[faceCount],
			FaceAreaTable = FaceAreaTable,
			FaceElevationTable = FaceElevationTable,
			FaceWettedPerimeterTable = FaceWettedPerimeterTable,
			FaceHydraulicRadiusTable = FaceHydraulicRadiusTable,
			FaceMeanElevation = FaceMeanElevation,
			FaceManningsTable = FaceManningsTable
		};
	}

	public void RebuildFaceMinElevationsFromFullTable()
	{
		if (FaceMinimumElevations == null || FaceMinimumElevations.Length != Mesh.FaceCount)
		{
			FaceMinimumElevations = new float[Mesh.FaceCount];
		}
		for (int i = 0; i < Mesh.FaceCount; i++)
		{
			FaceMinimumElevations[i] = FaceElevationTable[FaceTableStart[i]];
		}
	}

	public Memory<float> EnsureCellMinElevationTable()
	{
		if (CellMinimumElevations == null || CellMinimumElevations.Length != Mesh.CellCount)
		{
			RebuildCellMinElevationsFromFullTable();
		}
		return CellMinimumElevations;
	}

	public void RebuildCellMinElevationsFromFullTable()
	{
		if (CellMinimumElevations == null || CellMinimumElevations.Length != Mesh.CellCount)
		{
			CellMinimumElevations = new float[Mesh.CellCount];
		}
		for (int i = 0; i < Mesh.CellCount; i++)
		{
			CellMinimumElevations[i] = CellElevationTable[CellTableStart[i]];
		}
	}

	public void RebuildMinElevationsFromFullTables()
	{
		RebuildCellMinElevationsFromFullTable();
		RebuildFaceMinElevationsFromFullTable();
	}

	public void RebuildTableCountsFromStarts()
	{
		if (CellTableStart == null || FaceTableStart == null)
		{
			throw new Exception("TableStarts not initialized.");
		}
		if (CellTableCount == null || CellTableCount.Length != CellTableStart.Length)
		{
			CellTableCount = new short[CellTableStart.Length];
		}
		if (FaceTableCount == null || FaceTableCount.Length != FaceTableStart.Length)
		{
			FaceTableCount = new short[FaceTableStart.Length];
		}
		for (int i = 0; i < CellTableStart.Length - 1; i++)
		{
			CellTableCount[i] = (short)(CellTableStart[i + 1] - CellTableStart[i]);
		}
		CellTableCount[^1] = (short)(CellVolumeTable.Length - CellTableStart[^1]);
		for (int j = 0; j < FaceTableStart.Length - 1; j++)
		{
			FaceTableCount[j] = (short)(FaceTableStart[j + 1] - FaceTableStart[j]);
		}
		FaceTableCount[^1] = (short)(FaceAreaTable.Length - FaceTableStart[^1]);
	}

	public float FaceMinElevation(int fIdx)
	{
		if (FaceMinimumElevations == null)
		{
			if (FaceElevationTable == null)
			{
				throw new ArgumentException("Face tables not computed.");
			}
			RebuildFaceMinElevationsFromFullTable();
		}
		return FaceMinimumElevations[fIdx];
	}

	public float CellMinElevation(int cIdx)
	{
		if (CellMinimumElevations == null)
		{
			if (CellElevationTable == null)
			{
				throw new ArgumentException("Cell tables not computed.");
			}
			RebuildCellMinElevationsFromFullTable();
		}
		return CellMinimumElevations[cIdx];
	}

	public float[] AllCellMinimumElevations()
	{
		float[] floatValues = new float[CellTableStart.Length];
		for (int i = 0; i < floatValues.Length; i++)
		{
			floatValues[i] = CellMinElevation(i);
		}
		return floatValues;
	}

	public void ComputeCellWettedAreaTables()
	{
		if (CellWettedAreaTable == null || CellWettedAreaTable.Length != CellVolumeTable.Length)
		{
			CellWettedAreaTable = new float[CellVolumeTable.Length];
		}
		for (int i = 0; i < Mesh.CellCount; i++)
		{
			int cellTableStart = CellTableStart[i];
			int cellTableCount = CellTableCount[i];
			if (cellTableCount != 0)
			{
				double cellWettedAreaTable = 0.0;
				for (int j = cellTableStart + 1; j < cellTableStart + cellTableCount; j++)
				{
					double cellVolumeTableStep = CellVolumeTable[j] - CellVolumeTable[j - 1];
					double cellElevationTableStep = CellElevationTable[j] - CellElevationTable[j - 1];
					cellWettedAreaTable = cellVolumeTableStep / cellElevationTableStep;
					CellWettedAreaTable[j - 1] = (float)cellWettedAreaTable;
				}
				CellWettedAreaTable[cellTableStart + cellTableCount - 1] = (float)Math.Max(cellWettedAreaTable, Mesh.CellPolygonArea(i));
			}
		}
	}

	public void ComputeFaceHydraulicDepthTables()
	{
		calcFaceHydrDepth = true;
		FaceHydraulicDepthTable = new float[FaceAreaTable.Length];
		for (int i = 0; i < Mesh.FaceCount; i++)
		{
			int faceTableStart = FaceTableStart[i];
			int count = faceTableStart + FaceTableCount[i];
			FaceHydraulicDepthTable[faceTableStart] = 0f;
			for (int j = faceTableStart + 1; j < count; j++)
			{
				float faceWettedTopWidthTableOne = ComputeFaceWettedTopWidthTableOne(i, j);
				FaceHydraulicDepthTable[j] = FaceAreaTable[j] / faceWettedTopWidthTableOne;
			}
		}
	}

	public void ComputeFaceHydraulicRadiusTables()
	{
		calcFaceHydrRadius = true;
		FaceHydraulicRadiusTable = new float[FaceAreaTable.Length];
		for (int i = 0; i < Mesh.FaceCount; i++)
		{
			int faceTableStart = FaceTableStart[i];
			int faceTableCount = FaceTableCount[i];
			FaceHydraulicRadiusTable[faceTableStart] = 0f;
			for (int j = faceTableStart + 1; j < faceTableStart + faceTableCount; j++)
			{
				FaceHydraulicRadiusTable[j] = FaceAreaTable[j] / FaceWettedPerimeterTable[j];
			}
		}
	}

	public void ComputeFaceBedFrictVariableTable(Mesh mesh, double gravity, double nSItoUnits, double dzMin)
	{
		calcFaceBedFrictCoeff = true;
		calcConstBedFrictCoeff = false;
		FaceBedFrictVarTable = new float[FaceAreaTable.Length];
		FaceBedFrictVarA = new float[mesh.FaceCount];
		FaceBedFrictVarB = new float[mesh.FaceCount];
		float gravity2 = (float)gravity;
		float nSItoUnits2 = (float)nSItoUnits;
		float y = (float)dzMin;
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			int faceTableStart = FaceTableStart[i];
			int count = faceTableStart + FaceTableCount[i];
			float nSItoUnits2FaceManningsTableTerm;
			for (int j = faceTableStart; j < count; j++)
			{
				nSItoUnits2FaceManningsTableTerm = nSItoUnits2 * FaceManningsTable[j];
				if ((double)nSItoUnits2FaceManningsTableTerm > 0.0)
				{
					float x = ComputeFaceHydraulicRadiusTableOne(i, j);
					x = MathF.Max(x, y);
					FaceBedFrictVarTable[j] = MathF.Pow(gravity2 * nSItoUnits2FaceManningsTableTerm * nSItoUnits2FaceManningsTableTerm / MathF.Pow(x, 1.3333334f), -0.75f);
				}
				else
				{
					FaceBedFrictVarTable[j] = 1E+20f;
				}
			}
			int countOffset = count - 1;
			nSItoUnits2FaceManningsTableTerm = nSItoUnits2 * FaceManningsTable[countOffset];
			if ((double)nSItoUnits2FaceManningsTableTerm > 0.0)
			{
				float faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[i];
				float faceWettedPerimeterTableGravity2Term = MathF.Pow(MathF.Pow(FaceWettedPerimeterTable[countOffset], 1.3333334f) * gravity2 * nSItoUnits2FaceManningsTableTerm * nSItoUnits2FaceManningsTableTerm, 0.75f);
				float faceBedFrictVarA = (FaceAreaTable[countOffset] - faceSimpleLength * FaceElevationTable[countOffset]) / faceWettedPerimeterTableGravity2Term;
				float faceBedFrictVarB = faceSimpleLength / faceWettedPerimeterTableGravity2Term;
				FaceBedFrictVarA[i] = faceBedFrictVarA;
				FaceBedFrictVarB[i] = faceBedFrictVarB;
			}
			else
			{
				FaceBedFrictVarA[i] = 1E+20f;
				FaceBedFrictVarB[i] = 0f;
			}
		}
	}

	public void SetConstBedFrictCoeff(double bedFrictionCoeff)
	{
		calcConstBedFrictCoeff = true;
		calcFaceBedFrictCoeff = false;
		ConstNonlinBedFrictionCoeff = bedFrictionCoeff;
	}

	public void ComputeFaceShearFluxVarTable(Mesh mesh, double gravity, double nSItoUnits)
	{
		calcFaceShearFluxCoeff = true;
		FaceShearFluxVarTable = new float[FaceAreaTable.Length];
		FaceShearFluxVarA = new float[mesh.FaceCount];
		FaceShearFluxVarB = new float[mesh.FaceCount];
		float sqrtGravity = (float)Math.Sqrt(gravity);
		float nSItoUnits2 = (float)nSItoUnits;
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			int faceTableStart = FaceTableStart[i];
			int count = faceTableStart + FaceTableCount[i];
			float nSItoUnits2FaceManningsTableTerm;
			for (int j = faceTableStart; j < count; j++)
			{
				float faceHydraulicRadiusTableOne = ComputeFaceHydraulicRadiusTableOne(i, j);
				nSItoUnits2FaceManningsTableTerm = nSItoUnits2 * FaceManningsTable[j];
				FaceShearFluxVarTable[j] = MathF.Pow(nSItoUnits2FaceManningsTableTerm * sqrtGravity, 1.2f) * faceHydraulicRadiusTableOne;
			}
			int countOffset = count - 1;
			nSItoUnits2FaceManningsTableTerm = nSItoUnits2 * FaceManningsTable[countOffset];
			float faceWettedPerimeterTableTerm = FaceWettedPerimeterTable[countOffset] * MathF.Pow(nSItoUnits2FaceManningsTableTerm * sqrtGravity, -1.2f);
			float faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[i];
			float faceShearFluxVarA = (FaceAreaTable[countOffset] - faceSimpleLength * FaceElevationTable[countOffset]) / faceWettedPerimeterTableTerm;
			float faceShearFluxVarB = faceSimpleLength / faceWettedPerimeterTableTerm;
			FaceShearFluxVarA[i] = faceShearFluxVarA;
			FaceShearFluxVarB[i] = faceShearFluxVarB;
		}
	}

	public void ComputeFaceConveyanceTable(Mesh mesh, double nSItoUnits)
	{
		calcFaceConveyance = true;
		FaceConveyanceTable = new float[FaceAreaTable.Length];
		FaceConveyanceA = new float[mesh.FaceCount];
		FaceConveyanceB = new float[mesh.FaceCount];
		float nSItoUnits2 = (float)nSItoUnits;
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			int faceTableStart = FaceTableStart[i];
			int count = faceTableStart + FaceTableCount[i];
			float nSItoUnits2FaceManningsTableTerm;
			float faceConveyanceA;
			for (int j = faceTableStart; j < count; j++)
			{
				float x = ComputeFaceHydraulicRadiusTableOne(i, j);
				nSItoUnits2FaceManningsTableTerm = nSItoUnits2 * FaceManningsTable[j];
				faceConveyanceA = FaceAreaTable[j];
				FaceConveyanceTable[j] = MathF.Pow(faceConveyanceA * MathF.Pow(x, 2f / 3f) / nSItoUnits2FaceManningsTableTerm, 0.6f);
			}
			int countOffset = count - 1;
			nSItoUnits2FaceManningsTableTerm = nSItoUnits2 * FaceManningsTable[countOffset];
			float faceWettedPerimeterTableTerm = MathF.Pow(MathF.Pow(FaceWettedPerimeterTable[countOffset], 2f / 3f) * nSItoUnits2FaceManningsTableTerm, 0.6f);
			float faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[i];
			faceConveyanceA = (FaceAreaTable[countOffset] - faceSimpleLength * FaceElevationTable[countOffset]) / faceWettedPerimeterTableTerm;
			float faceConveyanceB = faceSimpleLength / faceWettedPerimeterTableTerm;
			FaceConveyanceA[i] = faceConveyanceA;
			FaceConveyanceB[i] = faceConveyanceB;
		}
	}

	private float ComputeFaceHydraulicRadiusTableOne(int fIdx, int tblIdx)
	{
		int faceTableStart = FaceTableStart[fIdx];
		if (tblIdx > faceTableStart)
		{
			return FaceAreaTable[tblIdx] / FaceWettedPerimeterTable[tblIdx];
		}
		return 0f;
	}

	private float ComputeFaceHydraulicDepthTableOne(int fIdx, int tblIdx)
	{
		int faceTableStart = FaceTableStart[fIdx];
		if (tblIdx > faceTableStart)
		{
			return FaceAreaTable[tblIdx] / ComputeFaceWettedTopWidthTableOne(fIdx, tblIdx);
		}
		return 0f;
	}

	private float ComputeFaceWettedTopWidthTableOne(int fIdx, int tblIdx)
	{
		int faceTableStartFIdxTerm = FaceTableStart[fIdx] + FaceTableCount[fIdx];
		if (tblIdx < faceTableStartFIdxTerm - 1)
		{
			return (FaceAreaTable[tblIdx + 1] - FaceAreaTable[tblIdx]) / (FaceElevationTable[tblIdx + 1] - FaceElevationTable[tblIdx]);
		}
		return Mesh.MetadataCache.FaceSimpleLengths[fIdx];
	}

	public void UpdateFaceMeanElevation()
	{
		FaceMeanElevation = new float[Mesh.FaceCount];
		for (int i = 0; i < Mesh.FaceCount; i++)
		{
			int faceTableStartFaceTableCountTerm = FaceTableStart[i] + FaceTableCount[i] - 1;
			FaceMeanElevation[i] = (float)((double)FaceElevationTable[faceTableStartFaceTableCountTerm] - (double)FaceAreaTable[faceTableStartFaceTableCountTerm] * Mesh.MetadataCache.FaceInvLengths[i]);
		}
	}

	public void GetCellTables(int cIdx, out Memory<float> vols, out Memory<float> elevs, out Memory<float> wettedArea)
	{
		int start = CellTableStart[cIdx];
		int length = CellTableCount[cIdx];
		vols = CellVolumeTable.AsMemory(start, length);
		elevs = CellElevationTable.AsMemory(start, length);
		wettedArea = CellWettedAreaTable.AsMemory(start, length);
	}

	public void GetFaceTables(int fIdx, out Memory<float> areas, out Memory<float> elevs, out Memory<float> wettedPerim, out Memory<float> hydrRadius)
	{
		int start = FaceTableStart[fIdx];
		int length = FaceTableCount[fIdx];
		areas = FaceAreaTable.AsMemory(start, length);
		elevs = FaceElevationTable.AsMemory(start, length);
		wettedPerim = FaceWettedPerimeterTable.AsMemory(start, length);
		hydrRadius = FaceHydraulicRadiusTable.AsMemory(start, length);
	}

	public void GetCellPropertiesAtWaterSurface(int cIdx, double wsel, out double volume, out double wetArea, out int idx)
	{
		int cellTableStart = CellTableStart[cIdx];
		int cellTableCount = CellTableCount[cIdx];
		for (int i = cellTableStart; i < cellTableStart + cellTableCount; i++)
		{
			double cellElevationTable = CellElevationTable[i];
			if (wsel <= cellElevationTable)
			{
				idx = i - cellTableStart;
				wetArea = CellWettedAreaTable[i];
				if (i > cellTableStart)
				{
					volume = Interp(wsel, CellElevationTable[i - 1], CellElevationTable[i], CellVolumeTable[i - 1], CellVolumeTable[i]);
				}
				else
				{
					volume = 0.0;
				}
				return;
			}
		}
		int cellTableStartCellTableCountTerm = cellTableStart + cellTableCount - 1;
		wetArea = CellWettedAreaTable[cellTableStartCellTableCountTerm];
		idx = -1;
		volume = (double)CellVolumeTable[cellTableStartCellTableCountTerm] + wetArea * (wsel - (double)CellElevationTable[cellTableStartCellTableCountTerm]);
	}

	public void GetCellPropertiesAtWaterSurface(int cIdx, double wsel, int[] cellPriorIndexes, out double volume, out double wetArea)
	{
		int cellTableStart = CellTableStart[cIdx];
		int cellTableCount = CellTableCount[cIdx];
		int count = cellTableStart + cellTableCount - 1;
		if (wsel > (double)CellElevationTable[count])
		{
			wetArea = CellWettedAreaTable[count];
			volume = (double)CellVolumeTable[count] + wetArea * (wsel - (double)CellElevationTable[count]);
			cellPriorIndexes[cIdx] = cellTableCount - 1;
			return;
		}
		if (wsel <= (double)CellElevationTable[cellTableStart])
		{
			wetArea = CellWettedAreaTable[cellTableStart];
			volume = 0.0;
			cellPriorIndexes[cIdx] = 0;
			return;
		}
		int cellTableStartCellPriorIndexesTerm = cellTableStart + cellPriorIndexes[cIdx];
		if (cellTableStartCellPriorIndexesTerm <= cellTableStart)
		{
			cellTableStartCellPriorIndexesTerm = cellTableStart + 1;
		}
		else if (cellTableStartCellPriorIndexesTerm > count)
		{
			cellTableStartCellPriorIndexesTerm = count;
		}
		if (wsel > (double)CellElevationTable[cellTableStartCellPriorIndexesTerm])
		{
			for (int i = cellTableStartCellPriorIndexesTerm + 1; i <= count; i++)
			{
				if (wsel <= (double)CellElevationTable[i])
				{
					wetArea = CellWettedAreaTable[i];
					volume = Interp(wsel, CellElevationTable[i - 1], CellElevationTable[i], CellVolumeTable[i - 1], CellVolumeTable[i]);
					cellPriorIndexes[cIdx] = i - cellTableStart;
					return;
				}
			}
		}
		else
		{
			for (int ordinal = cellTableStartCellPriorIndexesTerm; ordinal > cellTableStart; ordinal--)
			{
				if (wsel > (double)CellElevationTable[ordinal - 1])
				{
					wetArea = CellWettedAreaTable[ordinal];
					volume = Interp(wsel, CellElevationTable[ordinal - 1], CellElevationTable[ordinal], CellVolumeTable[ordinal - 1], CellVolumeTable[ordinal]);
					cellPriorIndexes[cIdx] = ordinal - cellTableStart;
					return;
				}
			}
		}
		throw new InvalidOperationException("Water surface lookup failed to bracket a cell table segment.");
	}

	public void GetCellHydraulicDepth(int cIdx, double wsel, int[] cellPriorIndexes, in double volume, out double hydraulicDepth)
	{
		if (cellPriorIndexes[cIdx] == 0)
		{
			hydraulicDepth = 0.0;
			return;
		}
		int cellTableStart = CellTableStart[cIdx];
		int cellTableCount = CellTableCount[cIdx];
		int cellTableStartCellTableCountTerm = cellTableStart + cellTableCount - 1;
		if (wsel > (double)CellElevationTable[cellTableStartCellTableCountTerm])
		{
			double cellWettedAreaTable = CellWettedAreaTable[cellTableStartCellTableCountTerm];
			hydraulicDepth = volume / cellWettedAreaTable;
		}
		else
		{
			int cellTableStartCellPriorIndexesTerm = cellTableStart + cellPriorIndexes[cIdx];
			double interp = Interp(wsel, CellElevationTable[cellTableStartCellPriorIndexesTerm - 1], CellElevationTable[cellTableStartCellPriorIndexesTerm], CellWettedAreaTable[cellTableStartCellPriorIndexesTerm - 1], CellWettedAreaTable[cellTableStartCellPriorIndexesTerm]);
			hydraulicDepth = volume / interp;
		}
	}

	public void GetFaceHydraulicRadiusAtWaterSurface(int fIdx, double wsel, out double hydRad, out int idx)
	{
		int faceTableStart = FaceTableStart[fIdx];
		int faceTableCount = FaceTableCount[fIdx];
		for (int i = faceTableStart; i < faceTableStart + faceTableCount; i++)
		{
			double faceElevationTable = FaceElevationTable[i];
			if (wsel <= faceElevationTable)
			{
				idx = i - faceTableStart;
				if (i > faceTableStart)
				{
					double ratio = InterpRatio(wsel, FaceElevationTable[i - 1], FaceElevationTable[i]);
					double fromBound = ComputeFaceHydraulicRadiusTableOne(fIdx, i - 1);
					double toBound = ComputeFaceHydraulicRadiusTableOne(fIdx, i);
					hydRad = InterpLinear(ratio, fromBound, toBound);
				}
				else
				{
					hydRad = 0.0;
				}
				return;
			}
		}
		int faceTableStartFaceTableCountTerm = faceTableStart + faceTableCount - 1;
		double faceAreaTable = FaceAreaTable[faceTableStartFaceTableCountTerm];
		double faceWettedPerimeterTable = FaceWettedPerimeterTable[faceTableStartFaceTableCountTerm];
		idx = -1;
		hydRad = faceAreaTable / faceWettedPerimeterTable;
	}

	internal bool TryGetFaceSectionPropertiesAtWaterSurface(int fIdx, double wsel, out double area, out double wettedPerimeter, out double manning)
	{
		area = 0.0;
		wettedPerimeter = 0.0;
		manning = 0.0;
		if (!double.IsFinite(wsel) || Mesh == null || fIdx < 0 || fIdx >= Mesh.FaceCount || FaceTableStart == null || FaceTableCount == null || FaceAreaTable == null || FaceElevationTable == null || FaceWettedPerimeterTable == null || FaceManningsTable == null || fIdx >= FaceTableStart.Length || fIdx >= FaceTableCount.Length)
		{
			return false;
		}
		int faceTableStart = FaceTableStart[fIdx];
		int faceTableCount = FaceTableCount[fIdx];
		if (faceTableCount < 1)
		{
			return false;
		}
		int faceTableStartFaceTableCountTerm = faceTableStart + faceTableCount - 1;
		if (faceTableStart < 0 || faceTableStartFaceTableCountTerm >= FaceAreaTable.Length || faceTableStartFaceTableCountTerm >= FaceElevationTable.Length || faceTableStartFaceTableCountTerm >= FaceWettedPerimeterTable.Length || faceTableStartFaceTableCountTerm >= FaceManningsTable.Length)
		{
			return false;
		}
		if (wsel <= (double)FaceElevationTable[faceTableStart])
		{
			area = FaceAreaTable[faceTableStart];
			wettedPerimeter = FaceWettedPerimeterTable[faceTableStart];
			manning = FaceManningsTable[faceTableStart];
			return FaceSectionPropertiesAreValid(area, wettedPerimeter, manning);
		}
		if (wsel > (double)FaceElevationTable[faceTableStartFaceTableCountTerm])
		{
			double faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[fIdx];
			if (!double.IsFinite(faceSimpleLength) || faceSimpleLength <= 0.0)
			{
				return false;
			}
			area = (double)FaceAreaTable[faceTableStartFaceTableCountTerm] + faceSimpleLength * (wsel - (double)FaceElevationTable[faceTableStartFaceTableCountTerm]);
			wettedPerimeter = FaceWettedPerimeterTable[faceTableStartFaceTableCountTerm];
			manning = FaceManningsTable[faceTableStartFaceTableCountTerm];
			return FaceSectionPropertiesAreValid(area, wettedPerimeter, manning);
		}
		int i;
		for (i = faceTableStart + 1; i < faceTableStartFaceTableCountTerm && wsel > (double)FaceElevationTable[i]; i++)
		{
		}
		double faceElevationTableStep = FaceElevationTable[i] - FaceElevationTable[i - 1];
		if (!(faceElevationTableStep > 0.0))
		{
			area = FaceAreaTable[i];
			wettedPerimeter = FaceWettedPerimeterTable[i];
			manning = FaceManningsTable[i];
		}
		else
		{
			double ratio = (wsel - (double)FaceElevationTable[i - 1]) / faceElevationTableStep;
			area = InterpLinear(ratio, FaceAreaTable[i - 1], FaceAreaTable[i]);
			wettedPerimeter = InterpLinear(ratio, FaceWettedPerimeterTable[i - 1], FaceWettedPerimeterTable[i]);
			manning = InterpLinear(ratio, FaceManningsTable[i - 1], FaceManningsTable[i]);
		}
		return FaceSectionPropertiesAreValid(area, wettedPerimeter, manning);
	}

	private static bool FaceSectionPropertiesAreValid(double area, double wettedPerimeter, double manning)
	{
		if (double.IsFinite(area) && area >= 0.0 && double.IsFinite(wettedPerimeter) && wettedPerimeter >= 0.0 && double.IsFinite(manning) && manning > 0.0)
		{
			if (!(area <= 1E-06))
			{
				return wettedPerimeter > 0.0;
			}
			return true;
		}
		return false;
	}

	public void GetCellAreaWet(int cIdx, double wsel, out double wetArea)
	{
		int cellTableStart = CellTableStart[cIdx];
		int cellTableCount = CellTableCount[cIdx];
		for (int i = cellTableStart; i < cellTableStart + cellTableCount; i++)
		{
			double cellElevationTable = CellElevationTable[i];
			if (wsel <= cellElevationTable)
			{
				wetArea = CellWettedAreaTable[i];
				return;
			}
		}
		int cellTableStartCellTableCountTerm = cellTableStart + cellTableCount - 1;
		wetArea = CellWettedAreaTable[cellTableStartCellTableCountTerm];
	}

	public bool UpdateCells(double[] cellElevation, double[] cellArea, double[] cellVolume, float[] cellHydraulicDepth, int[] priorIndexes, double[] newCellVolumes, List<int> cellIndexes, ThreadFor threadFor)
	{
		EnsurePartialTables();
		if (threadFor == null)
		{
			threadFor = ThreadFor.DefaultSingleThread;
		}
		bool anyNegative = false;
		threadFor.Run(cellIndexes.Count, (int start, int stop) =>
		{
			List<int> cellIndexesList = cellIndexes;
			double[] newCellVolume = newCellVolumes;
			bool errorCellVolumeNegative = false;
			for (int i = start; i <= stop; i++)
			{
				int cIdx = cellIndexesList[i];
				UpdateCellsONE(in cIdx, cellElevation, cellArea, cellVolume, cellHydraulicDepth, priorIndexes, in newCellVolume, ref errorCellVolumeNegative);
			}
		});
		return !anyNegative;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UpdateCellsONE(in int cIdx, double[] cellElevation, double[] cellArea, double[] cellVolume, float[] cellHydraulicDepth, int[] cellPriorIndexes, in double[] newCellVolume, ref bool errorCellVolumeNegative)
	{
		double newCellVolume2 = newCellVolume[cIdx];
		cellVolume[cIdx] = newCellVolume2;
		if (newCellVolume2 < 0.0)
		{
			lock (this)
			{
				errorCellVolumeNegative = true;
			}
		}
		int cellTableStart = CellTableStart[cIdx];
		int cellTableStartCellPriorIndexesTerm = cellTableStart + cellPriorIndexes[cIdx];
		if (cellTableStartCellPriorIndexesTerm > cellTableStart)
		{
			double cellVolumeTable = CellVolumeTable[cellTableStartCellPriorIndexesTerm];
			double cellVolumeTable2 = CellVolumeTable[cellTableStartCellPriorIndexesTerm - 1];
			if (newCellVolume2 <= cellVolumeTable && newCellVolume2 >= cellVolumeTable2)
			{
				double ratio = InterpRatio(newCellVolume2, cellVolumeTable2, cellVolumeTable);
				cellElevation[cIdx] = InterpLinear(ratio, CellElevationTable[cellTableStartCellPriorIndexesTerm - 1], CellElevationTable[cellTableStartCellPriorIndexesTerm]);
				if (cellArea != null || cellHydraulicDepth != null)
				{
					double cellWettedAreaTable = CellWettedAreaTable[cellTableStartCellPriorIndexesTerm - 1];
					if (cellArea != null)
					{
						cellArea[cIdx] = cellWettedAreaTable;
					}
					if (cellHydraulicDepth != null)
					{
						cellHydraulicDepth[cIdx] = (float)(newCellVolume2 / cellWettedAreaTable);
					}
				}
				return;
			}
		}
		if (newCellVolume2 <= 0.0)
		{
			if (cellArea != null)
			{
				cellArea[cIdx] = CellWettedAreaTable[cellTableStart];
			}
			if (cellHydraulicDepth != null)
			{
				cellHydraulicDepth[cIdx] = 0f;
			}
			cellPriorIndexes[cIdx] = 0;
			cellElevation[cIdx] = CellElevationTable[cellTableStart];
			return;
		}
		int cellTableCount = CellTableCount[cIdx];
		int count = cellTableStart + cellTableCount - 1;
		double cellVolumeTable3 = CellVolumeTable[count];
		if (newCellVolume2 > cellVolumeTable3)
		{
			double cellWettedAreaTable2 = CellWettedAreaTable[count];
			if (cellArea != null)
			{
				cellArea[cIdx] = cellWettedAreaTable2;
			}
			if (cellHydraulicDepth != null)
			{
				cellHydraulicDepth[cIdx] = (float)(newCellVolume2 / cellWettedAreaTable2);
			}
			cellPriorIndexes[cIdx] = cellTableCount - 1;
			cellElevation[cIdx] = (double)CellElevationTable[count] + (newCellVolume2 - cellVolumeTable3) / cellWettedAreaTable2;
			return;
		}
		double cellVolumeTable4 = CellVolumeTable[cellTableStartCellPriorIndexesTerm];
		if (newCellVolume2 > cellVolumeTable4)
		{
			for (int i = cellTableStartCellPriorIndexesTerm; i <= count; i++)
			{
				cellVolumeTable4 = CellVolumeTable[i];
				if (!(newCellVolume2 <= cellVolumeTable4))
				{
					continue;
				}
				cellPriorIndexes[cIdx] = i - cellTableStart;
				double ratio2 = InterpRatio(newCellVolume2, CellVolumeTable[i - 1], cellVolumeTable4);
				cellElevation[cIdx] = InterpLinear(ratio2, CellElevationTable[i - 1], CellElevationTable[i]);
				if (cellArea != null || cellHydraulicDepth != null)
				{
					double cellWettedAreaTable3 = CellWettedAreaTable[i - 1];
					if (cellArea != null)
					{
						cellArea[cIdx] = cellWettedAreaTable3;
					}
					if (cellHydraulicDepth != null)
					{
						cellHydraulicDepth[cIdx] = (float)(newCellVolume2 / cellWettedAreaTable3);
					}
				}
				break;
			}
			return;
		}
		for (int ordinal = cellTableStartCellPriorIndexesTerm - 1; ordinal > cellTableStart; ordinal--)
		{
			cellVolumeTable4 = CellVolumeTable[ordinal - 1];
			if (newCellVolume2 > cellVolumeTable4)
			{
				cellPriorIndexes[cIdx] = ordinal - cellTableStart;
				double ratio3 = InterpRatio(newCellVolume2, cellVolumeTable4, CellVolumeTable[ordinal]);
				cellElevation[cIdx] = InterpLinear(ratio3, CellElevationTable[ordinal - 1], CellElevationTable[ordinal]);
				if (cellArea != null || cellHydraulicDepth != null)
				{
					double cellWettedAreaTable4 = CellWettedAreaTable[ordinal - 1];
					if (cellArea != null)
					{
						cellArea[cIdx] = cellWettedAreaTable4;
					}
					if (cellHydraulicDepth != null)
					{
						cellHydraulicDepth[cIdx] = (float)(newCellVolume2 / cellWettedAreaTable4);
					}
				}
				break;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UpdateCellPartial(in int cIdx, ref double cellElevation, ref double cellArea, in double newCellVolume)
	{
		if (_partialCellProps == null || _partialCellProps.Length != Mesh.CellCount)
		{
			EnsurePartialTables();
		}
		UpdateCellPartial(cIdx, ref _partialCellProps[cIdx], ref cellElevation, ref cellArea, newCellVolume);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void UpdateCellPartial(int cIdx, ref PartialCellProps cellProps, ref double cellElevation, ref double cellArea, double newCellVolume)
	{
		if (newCellVolume == 0.0)
		{
			if (!cellProps.IsBottomOfTable)
			{
				UpdateCellWindow(cIdx, ref cellProps, 0.0);
			}
			cellElevation = cellProps.Elev1;
			cellArea = 0.0;
			return;
		}
		if (newCellVolume < (double)cellProps.Vol1 || (newCellVolume > (double)cellProps.VolTOP && !cellProps.IsTopOfTable))
		{
			UpdateCellWindow(cIdx, ref cellProps, newCellVolume);
			if (newCellVolume > (double)cellProps.VolTOP)
			{
				_ = cellProps.IsTopOfTable;
			}
		}
		if (newCellVolume < (double)cellProps.Vol2)
		{
			cellElevation = Interp(newCellVolume, cellProps.Vol1, cellProps.Vol2, cellProps.Elev1, cellProps.Elev2);
			cellArea = cellProps.Area12;
		}
		else if (newCellVolume <= (double)cellProps.Vol3)
		{
			cellElevation = Interp(newCellVolume, cellProps.Vol2, cellProps.Vol3, cellProps.Elev2, cellProps.Elev3);
			cellArea = cellProps.Area23;
		}
		else
		{
			cellArea = cellProps.FinalArea;
			cellElevation = (double)cellProps.Elev3 + (newCellVolume - (double)cellProps.Vol3) / cellArea;
		}
	}

	private void UpdateCellWindow(int cIdx, ref PartialCellProps cellProps, double volume)
	{
		int cellTableStart = CellTableStart[cIdx];
		int cellTableCount = CellTableCount[cIdx];
		int count = cellTableStart + cellTableCount - 1;
		if (cellTableCount == 3)
		{
			cellProps.IsTopOfTable = true;
			cellProps.Index = 0;
			cellProps.Elev1 = CellElevationTable[cellTableStart];
			cellProps.Vol1 = CellVolumeTable[cellTableStart];
			cellProps.Elev2 = CellElevationTable[cellTableStart + 1];
			cellProps.Vol2 = CellVolumeTable[cellTableStart + 1];
			cellProps.Elev3 = CellElevationTable[cellTableStart + 2];
			cellProps.Vol3 = CellVolumeTable[cellTableStart + 2];
		}
		if (cellTableCount <= 3)
		{
			throw new NotImplementedException();
		}
		int cellTableStartPlusIndex = cellTableStart + cellProps.Index;
		if (volume > (double)cellProps.Vol3)
		{
			if (cellProps.IsTopOfTable)
			{
				return;
			}
			for (int i = cellTableStartPlusIndex + 3; i <= count; i++)
			{
				if ((double)CellVolumeTable[i] > volume)
				{
					if (i == count)
					{
						break;
					}
					cellProps.IsTopOfTable = i + 1 == count;
					cellProps.Index = (ushort)(i - 1 - cellTableStart);
					cellProps.Elev1 = CellElevationTable[i - 1];
					cellProps.Vol1 = CellVolumeTable[i - 1];
					cellProps.Elev2 = CellElevationTable[i];
					cellProps.Vol2 = CellVolumeTable[i];
					cellProps.Elev3 = CellElevationTable[i + 1];
					cellProps.Vol3 = CellVolumeTable[i + 1];
					return;
				}
			}
			cellProps.IsTopOfTable = true;
			cellProps.Index = (ushort)(count - 2 - cellTableStart);
			cellProps.Elev1 = CellElevationTable[count - 2];
			cellProps.Vol1 = CellVolumeTable[count - 2];
			cellProps.Elev2 = CellElevationTable[count - 1];
			cellProps.Vol2 = CellVolumeTable[count - 1];
			cellProps.Elev3 = CellElevationTable[count];
			cellProps.Vol3 = CellVolumeTable[count];
			return;
		}
		if (volume < (double)cellProps.Vol1)
		{
			cellProps.IsTopOfTable = false;
			for (int ordinal = cellTableStartPlusIndex - 1; ordinal > cellTableStart; ordinal--)
			{
				if ((double)CellVolumeTable[ordinal] < volume)
				{
					if (ordinal < cellTableStart + 3 - 1)
					{
						break;
					}
					cellProps.Index = (ushort)(ordinal - 1 - cellTableStart);
					cellProps.Elev1 = CellElevationTable[ordinal - 1];
					cellProps.Vol1 = CellVolumeTable[ordinal - 1];
					cellProps.Elev2 = CellElevationTable[ordinal];
					cellProps.Vol2 = CellVolumeTable[ordinal];
					cellProps.Elev3 = CellElevationTable[ordinal + 1];
					cellProps.Vol3 = CellVolumeTable[ordinal + 1];
					return;
				}
			}
			cellProps.Index = 0;
			cellProps.Elev1 = CellElevationTable[cellTableStart];
			cellProps.Vol1 = CellVolumeTable[cellTableStart];
			cellProps.Elev2 = CellElevationTable[cellTableStart + 1];
			cellProps.Vol2 = CellVolumeTable[cellTableStart + 1];
			cellProps.Elev3 = CellElevationTable[cellTableStart + 2];
			cellProps.Vol3 = CellVolumeTable[cellTableStart + 2];
			return;
		}
		throw new NotImplementedException();
	}

	public void EnsurePartialTables()
	{
		if (_partialCellProps != null && _partialCellProps.Length == Mesh.CellCount)
		{
			return;
		}
		PartialCellProps[] properties = new PartialCellProps[Mesh.CellCount];
		for (int i = 0; i < properties.Length; i++)
		{
			properties[i] = SeedPartialProp(i);
		}
		_partialCellProps = properties;
	}

	private PartialCellProps SeedPartialProp(int cIdx)
	{
		int cellTableStart = CellTableStart[cIdx];
		int cellTableCount = CellTableCount[cIdx];
		PartialCellProps result = new PartialCellProps
		{
			FinalArea = CellWettedAreaTable[cellTableStart + cellTableCount - 1]
		};
		if (cellTableCount >= 3)
		{
			result.IsTopOfTable = cellTableCount == 3;
			result.Index = 0;
			result.Vol1 = CellVolumeTable[cellTableStart];
			result.Vol2 = CellVolumeTable[cellTableStart + 1];
			result.Vol3 = CellVolumeTable[cellTableStart + 2];
			result.Elev1 = CellElevationTable[cellTableStart];
			result.Elev2 = CellElevationTable[cellTableStart + 1];
			result.Elev3 = CellElevationTable[cellTableStart + 2];
		}
		else
		{
			result.IsTopOfTable = true;
			result.Index = 0;
			result.Elev1 = CellElevationTable[cellTableStart];
			result.Vol1 = CellVolumeTable[cellTableStart];
			switch (cellTableCount)
			{
			case 1:
				result.Elev2 = result.Elev1 + 1f;
				result.Vol2 = result.Vol1 + result.FinalArea;
				result.Elev3 = result.Elev2 + 1f;
				result.Vol3 = result.Vol2 + 2f * result.FinalArea;
				break;
			case 2:
				result.Elev2 = CellElevationTable[cellTableStart + 1];
				result.Vol2 = CellVolumeTable[cellTableStart + 1];
				result.Elev3 = result.Elev2 + 1f;
				result.Vol3 = result.Vol2 + CellWettedAreaTable[cellTableStart + 1];
				break;
			default:
				throw new NotImplementedException();
			}
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UpdateFaceONE(in int fIdx, int[] prevFoundIndexes, double[] faceWsel, double[] faceArea, float[] faceHydrRadius, float[] faceHydrDepth, float[] faceManning, float[] faceNonlinFrictCoeff, float[] faceShearFluxCoeff, double[] faceConveyance, in double wsel)
	{
		_ = ref Mesh.Faces[fIdx];
		int prevFoundIndex = prevFoundIndexes[fIdx];
		if (prevFoundIndex != 0)
		{
			if (faceWsel[fIdx] == wsel)
			{
				return;
			}
		}
		else
		{
			prevFoundIndex = 1;
		}
		int faceTableStart = FaceTableStart[fIdx];
		int faceTableStartFaceTableCountTerm = faceTableStart + FaceTableCount[fIdx];
		int i;
		for (i = faceTableStart + prevFoundIndex; i < faceTableStartFaceTableCountTerm && wsel > (double)FaceElevationTable[i]; i++)
		{
		}
		while (i - 1 > faceTableStart && wsel < (double)FaceElevationTable[i - 1])
		{
			i--;
		}
		if (i - 1 == faceTableStart && wsel <= (double)FaceElevationTable[i - 1])
		{
			i--;
			faceWsel[fIdx] = FaceElevationTable[i];
			faceArea[fIdx] = FaceAreaTable[i];
			if (calcFaceManning)
			{
				faceManning[fIdx] = FaceManningsTable[i];
			}
			if (calcFaceHydrRadius)
			{
				faceHydrRadius[fIdx] = FaceHydraulicRadiusTable[i];
			}
			if (calcFaceHydrDepth)
			{
				faceHydrDepth[fIdx] = FaceHydraulicDepthTable[i];
			}
			if (calcFaceBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)Math.Pow(FaceBedFrictVarTable[i], -1.3333333333333333);
			}
			else if (calcConstBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)ConstNonlinBedFrictionCoeff;
			}
			if (calcFaceShearFluxCoeff)
			{
				faceShearFluxCoeff[fIdx] = (float)Math.Pow(FaceShearFluxVarTable[i], 5.0 / 6.0);
			}
			if (calcFaceConveyance)
			{
				faceConveyance[fIdx] = Math.Pow(FaceConveyanceTable[i], 1.6666666666666667);
			}
			return;
		}
		faceWsel[fIdx] = wsel;
		prevFoundIndexes[fIdx] = i - faceTableStart;
		if (i == faceTableStartFaceTableCountTerm)
		{
			int ordinal = i - 1;
			double faceElevationTable = FaceElevationTable[ordinal];
			double faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[fIdx];
			double faceArea2 = (double)FaceAreaTable[ordinal] + faceSimpleLength * (wsel - faceElevationTable);
			faceArea[fIdx] = faceArea2;
			if (calcFaceManning)
			{
				faceManning[fIdx] = FaceManningsTable[ordinal];
			}
			if (calcFaceHydrRadius)
			{
				faceHydrRadius[fIdx] = (float)(faceArea2 / (double)FaceWettedPerimeterTable[ordinal]);
			}
			if (calcFaceHydrDepth)
			{
				faceHydrDepth[fIdx] = (float)(faceArea2 / faceSimpleLength);
			}
			if (calcFaceBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)Math.Pow((double)FaceBedFrictVarA[fIdx] + (double)FaceBedFrictVarB[fIdx] * wsel, -1.3333333333333333);
			}
			else if (calcConstBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)ConstNonlinBedFrictionCoeff;
			}
			if (calcFaceShearFluxCoeff)
			{
				faceShearFluxCoeff[fIdx] = (float)Math.Pow((double)FaceShearFluxVarA[fIdx] + (double)FaceShearFluxVarB[fIdx] * wsel, 5.0 / 6.0);
			}
			if (calcFaceConveyance)
			{
				faceConveyance[fIdx] = Math.Pow((double)FaceConveyanceA[fIdx] + (double)FaceConveyanceB[fIdx] * wsel, 1.6666666666666667);
			}
		}
		else
		{
			double ratio = InterpRatio(wsel, FaceElevationTable[i - 1], FaceElevationTable[i]);
			faceArea[fIdx] = InterpLinear(ratio, FaceAreaTable[i - 1], FaceAreaTable[i]);
			if (calcFaceManning)
			{
				faceManning[fIdx] = (float)InterpLinear(ratio, FaceManningsTable[i - 1], FaceManningsTable[i]);
			}
			if (calcFaceHydrRadius)
			{
				faceHydrRadius[fIdx] = (float)InterpLinear(ratio, FaceHydraulicRadiusTable[i - 1], FaceHydraulicRadiusTable[i]);
			}
			if (calcFaceHydrDepth)
			{
				faceHydrDepth[fIdx] = (float)InterpLinear(ratio, FaceHydraulicDepthTable[i - 1], FaceHydraulicDepthTable[i]);
			}
			if (calcFaceBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)Math.Pow(InterpLinear(ratio, FaceBedFrictVarTable[i - 1], FaceBedFrictVarTable[i]), -1.3333333333333333);
			}
			else if (calcConstBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)ConstNonlinBedFrictionCoeff;
			}
			if (calcFaceShearFluxCoeff)
			{
				faceShearFluxCoeff[fIdx] = (float)Math.Pow(InterpLinear(ratio, FaceShearFluxVarTable[i - 1], FaceShearFluxVarTable[i]), 5.0 / 6.0);
			}
			if (calcFaceConveyance)
			{
				faceConveyance[fIdx] = Math.Pow(InterpLinear(ratio, FaceConveyanceTable[i - 1], FaceConveyanceTable[i]), 1.6666666666666667);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void UpdateFaceONE<T>(in int fIdx, int[] prevFoundIndexes, double[] faceWsel, double[] faceArea, float[] faceHydrRadius, float[] faceHydrDepth, float[] faceManning, float[] faceNonlinFrictCoeff, float[] faceShearFluxCoeff, double[] faceConveyance, in double wsel) where T : IPropertyConfig
	{
		_ = ref Mesh.Faces[fIdx];
		int prevFoundIndex = prevFoundIndexes[fIdx];
		if (prevFoundIndex != 0)
		{
			if (faceWsel[fIdx] == wsel)
			{
				return;
			}
		}
		else
		{
			prevFoundIndex = 1;
		}
		int faceTableStart = FaceTableStart[fIdx];
		int faceTableStartFaceTableCountTerm = faceTableStart + FaceTableCount[fIdx];
		int i;
		for (i = faceTableStart + prevFoundIndex; i < faceTableStartFaceTableCountTerm && wsel > (double)FaceElevationTable[i]; i++)
		{
		}
		while (i - 1 > faceTableStart && wsel < (double)FaceElevationTable[i - 1])
		{
			i--;
		}
		if (i - 1 == faceTableStart && wsel <= (double)FaceElevationTable[i - 1])
		{
			i--;
			faceWsel[fIdx] = FaceElevationTable[i];
			faceArea[fIdx] = FaceAreaTable[i];
			if (calcFaceBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)Math.Pow(FaceBedFrictVarTable[i], -1.3333333333333333);
			}
			else if (calcConstBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)ConstNonlinBedFrictionCoeff;
			}
			if (T.StateHasFaceMannings())
			{
				faceManning[fIdx] = FaceManningsTable[i];
			}
			if (T.StateHasFaceHydraulicRadius())
			{
				faceHydrRadius[fIdx] = FaceHydraulicRadiusTable[i];
			}
			if (T.StateHasFaceHydraulicDepth())
			{
				faceHydrDepth[fIdx] = FaceHydraulicDepthTable[i];
			}
			if (T.StateHasFaceShearFluxCoeff() && FaceShearFluxVarTable != null)
			{
				faceShearFluxCoeff[fIdx] = (float)Math.Pow(FaceShearFluxVarTable[i], 5.0 / 6.0);
			}
			if (T.StateHasFaceConveyance())
			{
				faceConveyance[fIdx] = Math.Pow(FaceConveyanceTable[i], 1.6666666666666667);
			}
			return;
		}
		faceWsel[fIdx] = wsel;
		prevFoundIndexes[fIdx] = i - faceTableStart;
		if (i == faceTableStartFaceTableCountTerm)
		{
			int ordinal = i - 1;
			double faceElevationTable = FaceElevationTable[ordinal];
			double faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[fIdx];
			double faceArea2 = (double)FaceAreaTable[ordinal] + faceSimpleLength * (wsel - faceElevationTable);
			faceArea[fIdx] = faceArea2;
			if (T.StateHasFaceMannings())
			{
				faceManning[fIdx] = FaceManningsTable[ordinal];
			}
			if (T.StateHasFaceHydraulicRadius())
			{
				faceHydrRadius[fIdx] = (float)(faceArea2 / (double)FaceWettedPerimeterTable[ordinal]);
			}
			if (T.StateHasFaceHydraulicDepth())
			{
				faceHydrDepth[fIdx] = (float)(faceArea2 / faceSimpleLength);
			}
			if (calcFaceBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)Math.Pow((double)FaceBedFrictVarA[fIdx] + (double)FaceBedFrictVarB[fIdx] * wsel, -1.3333333333333333);
			}
			else if (calcConstBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)ConstNonlinBedFrictionCoeff;
			}
			if (T.StateHasFaceShearFluxCoeff() && FaceShearFluxVarTable != null)
			{
				faceShearFluxCoeff[fIdx] = (float)Math.Pow((double)FaceShearFluxVarA[fIdx] + (double)FaceShearFluxVarB[fIdx] * wsel, 5.0 / 6.0);
			}
			if (T.StateHasFaceConveyance())
			{
				faceConveyance[fIdx] = Math.Pow((double)FaceConveyanceA[fIdx] + (double)FaceConveyanceB[fIdx] * wsel, 1.6666666666666667);
			}
		}
		else
		{
			double ratio = InterpRatio(wsel, FaceElevationTable[i - 1], FaceElevationTable[i]);
			faceArea[fIdx] = InterpLinear(ratio, FaceAreaTable[i - 1], FaceAreaTable[i]);
			if (calcFaceBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)Math.Pow(InterpLinear(ratio, FaceBedFrictVarTable[i - 1], FaceBedFrictVarTable[i]), -1.3333333333333333);
			}
			else if (calcConstBedFrictCoeff)
			{
				faceNonlinFrictCoeff[fIdx] = (float)ConstNonlinBedFrictionCoeff;
			}
			if (T.StateHasFaceMannings())
			{
				faceManning[fIdx] = (float)InterpLinear(ratio, FaceManningsTable[i - 1], FaceManningsTable[i]);
			}
			if (T.StateHasFaceHydraulicRadius())
			{
				faceHydrRadius[fIdx] = (float)InterpLinear(ratio, FaceHydraulicRadiusTable[i - 1], FaceHydraulicRadiusTable[i]);
			}
			if (T.StateHasFaceHydraulicDepth())
			{
				faceHydrDepth[fIdx] = (float)InterpLinear(ratio, FaceHydraulicDepthTable[i - 1], FaceHydraulicDepthTable[i]);
			}
			if (T.StateHasFaceShearFluxCoeff() && FaceShearFluxVarTable != null)
			{
				faceShearFluxCoeff[fIdx] = (float)Math.Pow(InterpLinear(ratio, FaceShearFluxVarTable[i - 1], FaceShearFluxVarTable[i]), 5.0 / 6.0);
			}
			if (T.StateHasFaceConveyance())
			{
				faceConveyance[fIdx] = Math.Pow(InterpLinear(ratio, FaceConveyanceTable[i - 1], FaceConveyanceTable[i]), 1.6666666666666667);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double ComputeFaceHydraulicDepth(in int fIdx, int[] prevFoundIndexes, in double wsel)
	{
		int prevFoundIndex = prevFoundIndexes[fIdx];
		if (prevFoundIndex == 0)
		{
			return 0.0;
		}
		int faceTableStart = FaceTableStart[fIdx];
		int faceTableStartPlusPrevFoundIndex = faceTableStart + prevFoundIndex;
		if (faceTableStartPlusPrevFoundIndex - 1 == faceTableStart && wsel <= (double)FaceElevationTable[faceTableStartPlusPrevFoundIndex - 1])
		{
			return 0.0;
		}
		int faceTableStartFaceTableCountTerm = faceTableStart + FaceTableCount[fIdx];
		if (faceTableStartPlusPrevFoundIndex == faceTableStartFaceTableCountTerm)
		{
			int ordinal = faceTableStartPlusPrevFoundIndex - 1;
			double faceElevationTable = FaceElevationTable[ordinal];
			double faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[fIdx];
			return (float)((double)FaceAreaTable[ordinal] / faceSimpleLength + wsel - faceElevationTable);
		}
		double ratio = InterpRatio(wsel, FaceElevationTable[faceTableStartPlusPrevFoundIndex - 1], FaceElevationTable[faceTableStartPlusPrevFoundIndex]);
		float faceHydraulicDepthTableOne = ComputeFaceHydraulicDepthTableOne(fIdx, faceTableStartPlusPrevFoundIndex - 1);
		float faceHydraulicDepthTableOne2 = ComputeFaceHydraulicDepthTableOne(fIdx, faceTableStartPlusPrevFoundIndex);
		return InterpLinear(ratio, faceHydraulicDepthTableOne, faceHydraulicDepthTableOne2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double ComputeFaceConveyanceByArea(in int fIdx, int[] prevFoundIndexes, in double wsel, double nSItoUnits)
	{
		int prevFoundIndex = prevFoundIndexes[fIdx];
		if (prevFoundIndex == 0)
		{
			return 0.0;
		}
		int faceTableStart = FaceTableStart[fIdx];
		int faceTableStartPlusPrevFoundIndex = faceTableStart + prevFoundIndex;
		if (faceTableStartPlusPrevFoundIndex - 1 == faceTableStart && wsel <= (double)FaceElevationTable[faceTableStartPlusPrevFoundIndex - 1])
		{
			return 0.0;
		}
		int faceTableStartFaceTableCountTerm = faceTableStart + FaceTableCount[fIdx];
		if (faceTableStartPlusPrevFoundIndex == faceTableStartFaceTableCountTerm)
		{
			int ordinal = faceTableStartPlusPrevFoundIndex - 1;
			double faceElevationTable = FaceElevationTable[ordinal];
			double faceAreaTableFaceSimpleLengthsTerm = (double)FaceAreaTable[ordinal] + (double)Mesh.MetadataCache.FaceSimpleLengths[fIdx] * (wsel - faceElevationTable);
			double faceManningsTable = FaceManningsTable[ordinal];
			double faceWettedPerimeterTable = FaceWettedPerimeterTable[ordinal];
			return Math.Pow(faceAreaTableFaceSimpleLengthsTerm / faceWettedPerimeterTable, 2.0 / 3.0) / (nSItoUnits * faceManningsTable);
		}
		double ratio = InterpRatio(wsel, FaceElevationTable[faceTableStartPlusPrevFoundIndex - 1], FaceElevationTable[faceTableStartPlusPrevFoundIndex]);
		double interpLinear = InterpLinear(ratio, FaceManningsTable[faceTableStartPlusPrevFoundIndex - 1], FaceManningsTable[faceTableStartPlusPrevFoundIndex]);
		double interpLinear2 = InterpLinear(ratio, FaceAreaTable[faceTableStartPlusPrevFoundIndex - 1], FaceAreaTable[faceTableStartPlusPrevFoundIndex]);
		double interpLinear3 = InterpLinear(ratio, FaceWettedPerimeterTable[faceTableStartPlusPrevFoundIndex - 1], FaceWettedPerimeterTable[faceTableStartPlusPrevFoundIndex]);
		return Math.Pow(interpLinear2 / interpLinear3, 2.0 / 3.0) / (nSItoUnits * interpLinear);
	}

	public void ConvertDepthToStage(Memory<int> cellIndexes, Memory<float>[] depths)
	{
		Span<float> ensureCellMinElevationTable = EnsureCellMinElevationTable().Span;
		Span<int> cellIndexesSpan = cellIndexes.Span;
		for (int i = 0; i < cellIndexesSpan.Length; i++)
		{
			int index = cellIndexesSpan[i];
			float addValue = ensureCellMinElevationTable[index];
			Buffers.AddConstant(depths[i].Span, addValue);
		}
	}

	public void ConvertDepthToStage(Memory<int> cellIndexes, Memory<float> depths)
	{
		Span<float> ensureCellMinElevationTable = EnsureCellMinElevationTable().Span;
		Span<int> cellIndexesSpan = cellIndexes.Span;
		Span<float> depthsSpan = depths.Span;
		for (int i = 0; i < cellIndexesSpan.Length; i++)
		{
			int index = cellIndexesSpan[i];
			float ensureCellMinElevationTable2 = ensureCellMinElevationTable[index];
			depthsSpan[i] += ensureCellMinElevationTable2;
		}
	}

	public void ConvertDepthToStage(Memory<float> depths)
	{
		if (Mesh.CellCount != depths.Length)
		{
			throw new ArgumentException("Given depth buffer does not match expected cell count.");
		}
		Span<float> ensureCellMinElevationTable = EnsureCellMinElevationTable().Span;
		Span<float> depthsSpan = depths.Span;
		Buffers.AddVector(depthsSpan, ensureCellMinElevationTable, depthsSpan);
	}

	public void ConvertStageToDepth(Memory<int> cellIndexes, Memory<float>[] stages, float truncateTolerance = 0f)
	{
		Span<float> ensureCellMinElevationTable = EnsureCellMinElevationTable().Span;
		Span<int> cellIndexesSpan = cellIndexes.Span;
		for (int i = 0; i < cellIndexesSpan.Length; i++)
		{
			int index = cellIndexesSpan[i];
			float ensureCellMinElevationTable2 = ensureCellMinElevationTable[index];
			Span<float> stage = stages[i].Span;
			Buffers.AddConstant(stage, 0f - ensureCellMinElevationTable2);
			if (truncateTolerance != 0f)
			{
				Buffers.ClipToFloor(stage, truncateTolerance, 0f);
			}
		}
	}

	public void ConvertStageToDepth(Memory<float> stages)
	{
		if (Mesh.CellCount != stages.Length)
		{
			throw new ArgumentException("Given depth buffer does not match expected cell count.");
		}
		Span<float> ensureCellMinElevationTable = EnsureCellMinElevationTable().Span;
		Span<float> stagesSpan = stages.Span;
		Buffers.SubtractVector(stagesSpan, ensureCellMinElevationTable, stagesSpan);
	}

	public double InterpolateCellElevation(int cIdx, double volume)
	{
		GetCellTables(cIdx, out var vols, out var elevs, out var wettedArea);
		Span<float> volsSpan = vols.Span;
		Span<float> elevsSpan = elevs.Span;
		Span<float> wettedAreaSpan = wettedArea.Span;
		float vol = volsSpan[volsSpan.Length - 1];
		if (volume >= (double)vol)
		{
			float elev = elevsSpan[elevsSpan.Length - 1];
			float wettedArea2 = wettedAreaSpan[wettedAreaSpan.Length - 1];
			return (double)elev + (volume - (double)vol) / (double)wettedArea2;
		}
		if (volume == 0.0)
		{
			return elevsSpan[0];
		}
		for (int i = 1; i < volsSpan.Length; i++)
		{
			double vol2 = volsSpan[i];
			double vol3 = volsSpan[i - 1];
			if (volume >= vol3 && volume < vol2)
			{
				return Interp(volume, vol3, vol2, elevsSpan[i - 1], elevsSpan[i]);
			}
		}
		throw new Exception("Error");
	}

	public double InterpolateCellVolume(int cIdx, double elev)
	{
		GetCellTables(cIdx, out var vols, out var elevs, out var wettedArea);
		Span<float> volsSpan = vols.Span;
		Span<float> elevsSpan = elevs.Span;
		Span<float> wettedAreaSpan = wettedArea.Span;
		float elev2 = elevsSpan[elevsSpan.Length - 1];
		if (elev <= (double)elevsSpan[0])
		{
			return 0.0;
		}
		if (elev >= (double)elev2)
		{
			float vol = volsSpan[volsSpan.Length - 1];
			float wettedArea2 = wettedAreaSpan[wettedAreaSpan.Length - 1];
			return (double)vol + (elev - (double)elev2) * (double)wettedArea2;
		}
		for (int i = 1; i < volsSpan.Length; i++)
		{
			float elev3 = elevsSpan[i];
			float elev4 = elevsSpan[i - 1];
			if (elev >= (double)elev4 && elev < (double)elev3)
			{
				return Interp(elev, elev4, elev3, volsSpan[i - 1], volsSpan[i]);
			}
		}
		throw new Exception("Error");
	}

	public double InterpolateFaceArea(int fIdx, double elev)
	{
		GetFaceTables(fIdx, out var areas, out var elevs, out var wettedPerim, out var _);
		Span<float> areasSpan = areas.Span;
		Span<float> elevsSpan = elevs.Span;
		Span<float> wettedPerimSpan = wettedPerim.Span;
		float elev2 = elevsSpan[elevsSpan.Length - 1];
		if (elev <= (double)elevsSpan[0])
		{
			return 0.0;
		}
		if (elev >= (double)elev2)
		{
			float area = areasSpan[areasSpan.Length - 1];
			float wettedPerim2 = wettedPerimSpan[wettedPerimSpan.Length - 1];
			return (double)area + (elev - (double)elev2) * (double)wettedPerim2;
		}
		for (int i = 1; i < elevsSpan.Length; i++)
		{
			float elev3 = elevsSpan[i];
			float elev4 = elevsSpan[i - 1];
			if (elev >= (double)elev4 && elev < (double)elev3)
			{
				return Interp(elev, elev4, elev3, areasSpan[i - 1], areasSpan[i]);
			}
		}
		throw new Exception("Error");
	}

	public float InterpolateFaceArea(int fIdx, float elev)
	{
		GetFaceTables(fIdx, out var areas, out var elevs, out var wettedPerim, out var _);
		Span<float> areasSpan = areas.Span;
		Span<float> elevsSpan = elevs.Span;
		_ = wettedPerim.Span;
		float elev2 = elevsSpan[elevsSpan.Length - 1];
		if (elev <= elevsSpan[0])
		{
			return 0f;
		}
		if (elev >= elev2)
		{
			float area = areasSpan[areasSpan.Length - 1];
			float faceSimpleLength = Mesh.MetadataCache.FaceSimpleLengths[fIdx];
			return area + (elev - elev2) * faceSimpleLength;
		}
		for (int i = 1; i < elevsSpan.Length; i++)
		{
			float elev3 = elevsSpan[i];
			float elev4 = elevsSpan[i - 1];
			if (elev >= elev4 && elev < elev3)
			{
				return Interp(elev, elev4, elev3, areasSpan[i - 1], areasSpan[i]);
			}
		}
		throw new Exception("Error");
	}

	private T Interp<T>(T a, T a1, T a2, T b1, T b2) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		return InterpLinear(InterpRatio(a, a1, a2), b1, b2);
	}

	private static T InterpRatio<T>(T localValue, T fromBound, T toBound) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		return (localValue - fromBound) / (toBound - fromBound);
	}

	private static T InterpLinear<T>(T ratio, T fromBound, T toBound) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		return ratio * (toBound - fromBound) + fromBound;
	}

	public void Floodfill(int seedCell, double stage, Action<int> visitCell, bool includeSeedCell = false)
	{
		Mesh.Floodfill(seedCell, (int fIdx) => (double)FaceMinimumElevations[fIdx] < stage, (int cIdx) =>
		{
			visitCell(cIdx);
		}, includeSeedCell);
	}

	public string CellPropertySummary(int cIdx)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine($"cell {cIdx}");
		stringBuilder.AppendLine($"count: {CellTableCount[cIdx]}  start: {CellTableStart[cIdx]}");
		stringBuilder.AppendLine(string.Format("   i table    elev        vol       area", default(ReadOnlySpan<object>)));
		for (int i = 0; i < CellTableCount[cIdx]; i++)
		{
			int cellTableStartCIdxTerm = i + CellTableStart[cIdx];
			_003C_003Ey__InlineArray5<object> buffer = default;
			buffer[0] = i;
			buffer[1] = cellTableStartCIdxTerm;
			buffer[2] = CellElevationTable[cellTableStartCIdxTerm];
			buffer[3] = CellVolumeTable[cellTableStartCIdxTerm];
			buffer[4] = CellWettedAreaTable[cellTableStartCIdxTerm];
			stringBuilder.AppendLine(string.Format("{0,4}  {1,4} {2,7:G5} {3,8:E3} {4,8:E3}", (ReadOnlySpan<object?>)buffer));
		}
		return stringBuilder.ToString();
	}

	public string FacePropertySummary(int fIdx)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine($"face {fIdx}");
		stringBuilder.AppendLine($"count: {FaceTableCount[fIdx]}  start: {FaceTableStart[fIdx]}");
		stringBuilder.AppendLine(string.Format("   i   table      elev       area      length    R", default(ReadOnlySpan<object>)));
		for (int i = 0; i < FaceTableCount[fIdx]; i++)
		{
			int faceTableStartFIdxTerm = i + FaceTableStart[fIdx];
			_003C_003Ey__InlineArray6<object> buffer = default;
			buffer[0] = i;
			buffer[1] = faceTableStartFIdxTerm;
			buffer[2] = FaceElevationTable[faceTableStartFIdxTerm];
			buffer[3] = FaceAreaTable[faceTableStartFIdxTerm];
			buffer[4] = FaceWettedPerimeterTable[faceTableStartFIdxTerm];
			buffer[5] = FaceHydraulicRadiusTable[faceTableStartFIdxTerm];
			stringBuilder.AppendLine(string.Format("{0,4}  {1,6} {2,7:G8} {3,8:E3} {4,8:E3} {5,8:E3}", (ReadOnlySpan<object?>)buffer));
		}
		return stringBuilder.ToString();
	}

	public void Stats(out int smallestCellCount, out int largestCellCount, out double avgCellCount, out int smallestFaceCount, out int largestFaceCount, out double avgFaceCount)
	{
		smallestCellCount = CellTableCount.Min();
		largestCellCount = CellTableCount.Max();
		avgCellCount = ((IEnumerable<short>)CellTableCount).Select((Func<short, double>)((short s) => s)).Average();
		smallestFaceCount = FaceTableCount.Min();
		largestFaceCount = FaceTableCount.Max();
		avgFaceCount = ((IEnumerable<short>)FaceTableCount).Select((Func<short, double>)((short s) => s)).Average();
	}

	public string GetStatsStatusString()
	{
		Stats(out var smallestCellCount, out var largestCellCount, out var avgCellCount, out var smallestFaceCount, out var largestFaceCount, out var avgFaceCount);
		string newLine = Environment.NewLine;
		return $"Smallest Cell {smallestCellCount}{newLine}LargestCell {largestCellCount}{newLine}AverageCell {avgCellCount}{newLine}SmallestFace {smallestFaceCount}{newLine}LargestFace {largestFaceCount}{newLine}AverageFace {avgFaceCount}{newLine}";
	}
}
