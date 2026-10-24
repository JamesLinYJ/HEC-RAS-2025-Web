using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.HighPerformance;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.IO.Vectors;

public static class MeshReader
{
	private const string sVersion = "Version";

	public const string VERSION = "1.0";

	private const string sDataDate = "Data Date";

	public const string sCellVolumeElevation = "Cells Volume Elevation";

	public const string sFacesAreaElevation = "Faces Area Elevation";

	private static readonly ILogger _logger = LogHelper.GetLogger("MeshReader");

	public static bool ExistsAndIsCurrent(H5Reader hr, string meshGroup, string meshDate)
	{
		if (hr.ExistsAsGroup(meshGroup))
		{
			if (hr.TryReadStringAttributeScalar(meshGroup, "Version") != "1.0")
			{
				return false;
			}
			if (!string.Equals(hr.TryReadStringAttributeScalar(meshGroup, "Data Date", "No Date"), meshDate, StringComparison.CurrentCultureIgnoreCase))
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static OperationResult Read(string filename, string meshGroup, out Mesh mesh, ProgressReporter reporter = null)
	{
		using H5Reader hr = new H5Reader(filename);
		return Read(hr, meshGroup, out mesh, reporter);
	}

	public static OperationResult Read(H5Reader hr, string meshGroup, out Mesh mesh, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		OperationResult result = IsLegacyMesh(hr, meshGroup)
			? ReadLegacy(hr, meshGroup, out mesh, pr)
			: ReadNew(hr, meshGroup, out mesh, pr);
		string centroidDataset = meshGroup + "/Faces Low Elevation Centroid";
		if (result && hr.ExistsAsDataset(centroidDataset))
		{
			hr.ReadDataset(centroidDataset, out float[] stations);
			if (stations.Length != mesh.FaceCount)
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Face centroid dataset does not match the mesh face count.");
			}
			mesh.SetFaceCentroidStations(stations);
		}
		return result;
	}

	public static bool IsLegacyMesh(H5Reader hr, string meshGroup)
	{
		if (hr.ExistsAsDataset(meshGroup + "/Cells Face and Orientation Info"))
		{
			return true;
		}
		return false;
	}

	private static OperationResult ReadNew(H5Reader hr, string meshGroup, out Mesh mesh, ProgressReporter reporter)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (!hr.ExistsAsGroup(meshGroup))
		{
			mesh = Mesh.NullMesh();
			return OperationResult.Fail(H5Reader.MessageCodes.GetGroupNotFound(hr.Filename, meshGroup));
		}
		mesh = new Mesh();
		mesh.Perimeter = HDF5VectorReader.TryReadPolygon(hr, meshGroup + "/Perimeter");
		if (mesh.Perimeter == null)
		{
			mesh = Mesh.NullMesh();
			return OperationResult.Fail("Could not read Perimeter data.");
		}
		Point[] cellCenters = HDF5VectorReader.TryReadPoints(hr, meshGroup + "/Cell Coordinates", out var _, out var _);
		if (cellCenters == null)
		{
			mesh = Mesh.NullMesh();
			return OperationResult.Fail("Could not read cell coordinates.");
		}
		mesh.Cells = new Cell[cellCenters.Length];
		int[] elements = null;
		hr.ReadJagged1D<int>(meshGroup + "/Cell Data", out elements, out var starts, out var counts);
		int startLength = starts.Length;
		if (cellCenters.Length != startLength)
		{
			mesh = Mesh.NullMesh();
			return OperationResult.Fail("Cell dataset doesn't match expected size.");
		}
		Memory<int> elementsSpan = elements.AsMemory();
		mesh.CellCenters = cellCenters;
		for (int i = 0; i < cellCenters.Length; i++)
		{
			int start = starts[i];
			int length = counts[i];
			mesh.Cells[i] = new Cell
			{
				Faces = elementsSpan.Slice(start, length)
			};
		}
		hr.ReadDataset(meshGroup + "/Face Data", out int[,] values);
		string meshGroupTerm = meshGroup + "/Face Internal Points";
		double[,] values2 = null;
		if (hr.ExistsAsDataset(meshGroupTerm))
		{
			hr.ReadDataset(meshGroupTerm, out values2);
			hr.ReadJagged2D<double>(meshGroupTerm, out values2, out starts, out counts);
		}
		Memory<Point> points = values2?.AsMemory().Cast<double, Point>() ?? Memory<Point>.Empty;
		int length2 = values.GetLength(0);
		mesh.Faces = new Face[length2];
		mesh.FaceInternalPoints = new Memory<Point>[length2];
		for (int j = 0; j < mesh.FaceCount; j++)
		{
			Face face = new Face();
			face.cellA = values[j, 0];
			face.cellB = values[j, 1];
			face.fpA = values[j, 2];
			face.fpB = values[j, 3];
			Face face2 = face;
			if (values2 != null)
			{
				_ = starts[j];
				int count = counts[j];
				if (count != 0)
				{
					mesh.FaceInternalPoints[j] = points.Slice(0, count);
					points = points.Slice(count);
				}
			}
			mesh.Faces[j] = face2;
		}
		string meshGroupTerm2 = meshGroup + "/Node Coordinates";
		string meshGroupTerm3 = meshGroup + "/Node Data";
		if (!hr.ExistsAsDataset(meshGroupTerm2) || !hr.ExistsAsDataset(meshGroupTerm3))
		{
			mesh = Mesh.NullMesh();
			return OperationResult.Fail("Could not read facepoint/node data.");
		}
		hr.ReadDataset(meshGroupTerm2, out double[,] values3);
		hr.ReadJagged1D<int>(meshGroupTerm3, out var elements2, out starts, out counts);
		mesh.FacePoints = new FacePoint[values3.GetLength(0)];
		for (int k = 0; k < mesh.FacePointCount; k++)
		{
			FacePoint facePoint = new FacePoint(new Point(values3[k, 0], values3[k, 1]));
			facePoint.Faces = elements2.AsSpan().Slice(starts[k], counts[k]).ToArray();
			mesh.FacePoints[k] = facePoint;
		}
		string meshGroupTerm4 = meshGroup + "/Data/CellFace Streamwise";
		if (hr.ExistsAsDataset(meshGroupTerm4))
		{
			hr.ReadJagged2D<double>(meshGroupTerm4, out var elements3, out var starts2, out var counts2);
			hr.ReadAttribute(meshGroupTerm4, "Index", out int[] values4);
			mesh.StreamwiseData = StreamwiseData.FromJagged(elements3, values4, starts2, counts2);
		}
		else
		{
			mesh.StreamwiseData = null;
		}
		reporter.ReportProgress(100);
		stopwatch.Stop();
		_logger.LogDebug("Mesh read from hdf file in {Duration} ms.", stopwatch.Elapsed.TotalMilliseconds);
		return true;
	}

	private static OperationResult ReadLegacy(H5Reader hr, string meshGroup, out Mesh mesh, ProgressReporter reporter)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		mesh = new Mesh();
		if (!hr.ExistsAsGroup(meshGroup))
		{
			mesh = Mesh.NullMesh();
			return OperationResult.Fail(H5Reader.MessageCodes.GetGroupNotFound(hr.Filename, meshGroup));
		}
		try
		{
			hr.GetAttributeNames(meshGroup);
			mesh.Perimeter = HDF5VectorReader.TryReadPolygon(hr, meshGroup + "/Perimeter");
			if (mesh.Perimeter == null)
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Could not read Perimeter data.");
			}
			Point[] cellCenters = HDF5VectorReader.TryReadPoints(hr, meshGroup + "/Cells Center Coordinate", out var _, out var _);
			if (cellCenters == null)
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Could not read cell coordinates.");
			}
			reporter.ReportProgress(5);
			string meshGroupTerm = meshGroup + "/Cells Face and Orientation Info";
			string meshGroupTerm2 = meshGroup + "/Cells Face and Orientation Values";
			CellMinimumDatset(meshGroup);
			if (!hr.ExistsAsDataset(meshGroupTerm) || !hr.ExistsAsDataset(meshGroupTerm2))
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Could not read cell/face data.");
			}
			hr.ReadDataset(meshGroupTerm, out int[,] values);
			hr.ReadDataset(meshGroupTerm2, out int[,] values2);
			if (values.GetLength(0) != cellCenters.Length)
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Mismatched cell/face data.");
			}
			mesh.Cells = new Cell[cellCenters.Length];
			mesh.CellCenters = cellCenters;
			for (int i = 0; i < cellCenters.Length; i++)
			{
				Cell cell = mesh.Cells[i];
				int ordinal = values[i, 0];
				int count = values[i, 1];
				int[] faces = new int[count];
				for (int j = 0; j < count; j++)
				{
					int faces2 = values2[ordinal + j, 0];
					faces[j] = faces2;
				}
				cell.Faces = faces;
				mesh.Cells[i] = cell;
			}
			reporter.ReportProgress(30);
			string meshGroupTerm3 = meshGroup + "/Faces FacePoint Indexes";
			string meshGroupTerm4 = meshGroup + "/Faces Cell Indexes";
			if (!hr.ExistsAsDataset(meshGroupTerm3) || !hr.ExistsAsDataset(meshGroupTerm4))
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Could not read face/facepoint data.");
			}
			hr.ReadDataset(meshGroupTerm3, out int[,] values3);
			hr.ReadDataset(meshGroupTerm4, out int[,] values4);
			if (values3.GetLength(0) != values4.GetLength(0))
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Mismatched face indexing data.");
			}
			int length = values3.GetLength(0);
			mesh.Faces = new Face[length];
			mesh.FaceInternalPoints = new Memory<Point>[length];
			for (int k = 0; k < length; k++)
			{
				Face face = new Face();
				face.fpA = values3[k, 0];
				face.fpB = values3[k, 1];
				face.cellA = values4[k, 0];
				face.cellB = values4[k, 1];
				Face face2 = face;
				mesh.Faces[k] = face2;
			}
			reporter.ReportProgress(55);
			string meshGroupTerm5 = meshGroup + "/Faces Perimeter Info";
			string meshGroupTerm6 = meshGroup + "/Faces Perimeter Values";
			if (!hr.ExistsAsDataset(meshGroupTerm5) || !hr.ExistsAsDataset(meshGroupTerm6))
			{
				_logger.LogWarning("Continuing for now - Faces Perimeter Info/Values MIA. Should fix this reader.");
			}
			else
			{
				hr.ReadDataset(meshGroupTerm5, out int[,] values5);
				hr.ReadDataset(meshGroupTerm6, out double[,] values6);
				if (mesh.Faces.Length != values5.GetLength(0))
				{
					mesh = Mesh.NullMesh();
					return OperationResult.Fail("Mismatched data for face internal points.");
				}
				for (int l = 0; l < mesh.Faces.Length; l++)
				{
					_ = ref mesh.Faces[l];
					int values5Term = values5[l, 0];
					int count2 = values5[l, 1];
					if (count2 > 0)
					{
						Point[] faceInternalPoints = new Point[count2];
						for (int n = 0; n < count2; n++)
						{
							double x = values6[values5Term + n, 0];
							double y = values6[values5Term + n, 1];
							faceInternalPoints[n] = new Point(x, y);
						}
						mesh.FaceInternalPoints[l] = faceInternalPoints;
					}
				}
			}
			reporter.ReportProgress(63);
			string meshGroupTerm7 = meshGroup + "/FacePoints Coordinate";
			if (!hr.ExistsAsDataset(meshGroupTerm7))
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Could not read facepoint coordinates.");
			}
			hr.ReadDataset(meshGroupTerm7, out double[,] values7);
			if (values7.GetLength(1) != 2)
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Malformed facepoint coordinate data.");
			}
			int length2 = values7.GetLength(0);
			string datasetName = meshGroup + "/FacePoints Face and Orientation Info";
			string datasetName2 = meshGroup + "/FacePoints Face and Orientation Values";
			hr.ReadDataset(datasetName, out int[,] values8);
			hr.ReadDataset(datasetName2, out int[,] values9);
			if (values8.GetLength(0) != length2)
			{
				mesh = Mesh.NullMesh();
				return OperationResult.Fail("Malformed FacePoint Face/Orientation Info data.");
			}
			mesh.FacePoints = new FacePoint[length2];
			for (int intervalIndex = 0; intervalIndex < length2; intervalIndex++)
			{
				double x2 = values7[intervalIndex, 0];
				double y2 = values7[intervalIndex, 1];
				FacePoint facePoint = new FacePoint(new Point(x2, y2));
				int values8Term = values8[intervalIndex, 0];
				int count3 = values8[intervalIndex, 1];
				facePoint.Faces = new int[count3];
				for (int intervalIndex2 = 0; intervalIndex2 < count3; intervalIndex2++)
				{
					facePoint.Faces[intervalIndex2] = values9[values8Term + intervalIndex2, 0];
				}
				mesh.FacePoints[intervalIndex] = facePoint;
			}
			reporter.ReportProgress(100);
			mesh.CoalesceCellArrays();
			stopwatch.Stop();
			_logger.LogDebug("Mesh read from hdf file in {Duration} ms.", stopwatch.Elapsed.TotalMilliseconds);
		}
		catch (Exception ex)
		{
			string message = "Unknown error reading mesh from '" + hr.Filename + "'. Recompute this geometry.";
			mesh = Mesh.NullMesh();
			_logger.LogWarning(ex, message);
			return OperationResult.Fail("Reading Mesh", ex);
		}
		return true;
	}

	public static bool Write(Mesh mesh, H5Writer hw, string meshGroup, ProgressReporter reporter = null)
	{
		return WriteNew(mesh, hw, meshGroup, reporter);
	}

	public static bool Clear(H5Writer hw, string meshGroup)
	{
		if (hw.ExistsAsGroup(meshGroup))
		{
			hw.Delete(meshGroup);
			return true;
		}
		return false;
	}

	public static bool WriteNew(Mesh mesh, H5Writer hw, string meshGroup, ProgressReporter reporter = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (hw.ExistsAsGroup(meshGroup))
		{
			hw.Delete(meshGroup);
		}
		if (!mesh.IsValid())
		{
			reporter.ReportMessage("Failed to save mesh to " + meshGroup + ": Incomplete or has errors.");
			return false;
		}
		hw.CreateGroup(meshGroup);
		hw.WriteAttribute(meshGroup, "Version", "2.0");
		HDF5VectorReader.Write(hw, mesh.Perimeter, meshGroup + "/Perimeter");
		HDF5VectorReader.Write(hw, mesh.CellCenters, meshGroup + "/Cell Coordinates");
		reporter.ReportProgress(5);
		int[] intValues = new int[mesh.Cells.Sum((Cell c) => c.Faces.Length)];
		int[] intValuesArray = new int[mesh.CellCount];
		int[] intValues2 = new int[mesh.CellCount];
		Memory<int> intValuesSpan = intValues.AsMemory();
		int intervalIndex = 0;
		for (int intervalIndex2 = 0; intervalIndex2 < mesh.CellCount; intervalIndex2++)
		{
			Cell cell = mesh.Cells[intervalIndex2];
			int length = cell.Faces.Length;
			intValuesArray[intervalIndex2] = intervalIndex;
			intValues2[intervalIndex2] = length;
			cell.Faces.CopyTo(intValuesSpan.Slice(intervalIndex));
			intervalIndex += length;
		}
		hw.WriteDataset(meshGroup + "/Cell Data", intValues);
		hw.WriteAttribute(meshGroup + "/Cell Data", "Start", intValuesArray);
		hw.WriteAttribute(meshGroup + "/Cell Data", "Count", intValues2);
		int[,] intValues3 = new int[mesh.FaceCount, 4];
		for (int intervalIndex3 = 0; intervalIndex3 < mesh.FaceCount; intervalIndex3++)
		{
			Face face = mesh.Faces[intervalIndex3];
			intValues3[intervalIndex3, 0] = face.cellA;
			intValues3[intervalIndex3, 1] = face.cellB;
			intValues3[intervalIndex3, 2] = face.fpA;
			intValues3[intervalIndex3, 3] = face.fpB;
		}
		hw.WriteDataset(meshGroup + "/Face Data", intValues3);
		hw.WriteAttribute(meshGroup + "/Face Data", "Description", "[CellA, CellB, FpA, FpB]");
		int sum = mesh.FaceInternalPoints.Sum((Memory<Point> ff) => ff.Length);
		if (sum > 0)
		{
			string meshGroupTerm = meshGroup + "/Face Internal Points";
			intValuesArray = new int[mesh.FaceCount];
			intValues2 = new int[mesh.FaceCount];
			double[,] doubleValues = new double[sum, 2];
			intervalIndex = 0;
			for (int intervalIndex4 = 0; intervalIndex4 < mesh.FaceCount; intervalIndex4++)
			{
				_ = ref mesh.Faces[intervalIndex4];
				Span<Point> faceInternalPoint = mesh.FaceInternalPoints[intervalIndex4].Span;
				int length2 = faceInternalPoint.Length;
				intValuesArray[intervalIndex4] = intervalIndex;
				intValues2[intervalIndex4] = length2;
				for (int intervalIndex5 = 0; intervalIndex5 < length2; intervalIndex5++)
				{
					Point point = faceInternalPoint[intervalIndex5];
					doubleValues[intervalIndex + intervalIndex5, 0] = point.X;
					doubleValues[intervalIndex + intervalIndex5, 1] = point.Y;
				}
				intervalIndex += length2;
			}
			hw.WriteDataset(meshGroupTerm, doubleValues);
			hw.WriteAttribute(meshGroupTerm, "Start", intValuesArray);
			hw.WriteAttribute(meshGroupTerm, "Count", intValues2);
		}
		string dataset = meshGroup + "/Node Coordinates";
		string meshGroupTerm2 = meshGroup + "/Node Data";
		HDF5VectorReader.Write(hw, mesh.FacePoints.Select((FacePoint c) => c.Point).ToArray(), dataset);
		int[] faces = new int[mesh.FacePoints.Sum((FacePoint c) => c.Faces.Count)];
		intValuesArray = new int[mesh.FacePointCount];
		intValues2 = new int[mesh.FacePointCount];
		intervalIndex = 0;
		for (int intervalIndex6 = 0; intervalIndex6 < mesh.FacePointCount; intervalIndex6++)
		{
			FacePoint facePoint = mesh.FacePoints[intervalIndex6];
			int count = facePoint.Faces.Count;
			intValuesArray[intervalIndex6] = intervalIndex;
			intValues2[intervalIndex6] = count;
			facePoint.Faces.CopyTo(faces, intervalIndex);
			intervalIndex += count;
		}
		hw.WriteDataset(meshGroupTerm2, faces);
		hw.WriteAttribute(meshGroupTerm2, "Start", intValuesArray);
		hw.WriteAttribute(meshGroupTerm2, "Count", intValues2);
		if (mesh.StreamwiseData != null)
		{
			string meshGroupTerm3 = meshGroup + "/Data/CellFace Streamwise";
			double[,] cellFaceNormalsAsJaggedArray = mesh.StreamwiseData.CellFaceNormalsAsJaggedArray();
			if (cellFaceNormalsAsJaggedArray != null)
			{
				hw.WriteDataset(meshGroupTerm3, cellFaceNormalsAsJaggedArray);
				hw.WriteAttribute(meshGroupTerm3, "Start", mesh.StreamwiseData.JaggedStarts());
				hw.WriteAttribute(meshGroupTerm3, "Count", mesh.StreamwiseData.JaggedCounts());
				hw.WriteAttribute(meshGroupTerm3, "Index", mesh.StreamwiseData.Cells.ToArray());
			}
		}
		reporter.ReportProgress(100);
		stopwatch.Stop();
		WriteFaceCentroidStations(mesh, hw, meshGroup);
		_logger.LogDebug("Mesh saved to hdf file in {Duration} ms.", stopwatch.Elapsed.TotalMilliseconds);
		return true;
	}

	public static bool WriteLegacy(Mesh mesh, H5Writer hw, string meshGroup, string meshDate, ProgressReporter prog = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!mesh.IsValid())
		{
			prog.ReportMessage("Failed to save mesh to " + meshGroup + ": Incomplete or has errors.");
			return false;
		}
		if (hw.ExistsAsGroup(meshGroup))
		{
			hw.Delete(meshGroup);
		}
		hw.CreateGroup(meshGroup);
		hw.WriteAttribute(meshGroup, "Version", "1.0");
		hw.WriteAttribute(meshGroup, "Data Date", meshDate);
		MeshStatistics meshStatistics = mesh.GetMeshStatistics();
		hw.WriteAttribute(meshGroup, "Cell Maximum Index", meshStatistics.CellMaximumAreaIndex);
		hw.WriteAttribute(meshGroup, "Cell Maximum Size", meshStatistics.CellMaximumArea);
		hw.WriteAttribute(meshGroup, "Cell Minimum Size", meshStatistics.CellMinimumArea);
		hw.WriteAttribute(meshGroup, "Cell Average Size", meshStatistics.CellAverageArea);
		HDF5VectorReader.Write(hw, mesh.Perimeter, meshGroup + "/Perimeter");
		HDF5VectorReader.Write(hw, mesh.CellCenters, meshGroup + "/Cells Center Coordinate");
		prog.ReportProgress(5);
		int[,] intValues = new int[mesh.CellCount, 2];
		List<Tuple<int, int>> valueTupleList = new List<Tuple<int, int>>();
		for (int i = 0; i < mesh.CellCount; i++)
		{
			Cell cell = mesh.Cells[i];
			Span<int> facesSpan = cell.Faces.Span;
			intValues[i, 0] = valueTupleList.Count;
			intValues[i, 1] = facesSpan.Length;
			if (facesSpan.IsEmpty)
			{
				continue;
			}
			for (int j = 0; j < facesSpan.Length; j++)
			{
				int faceIndex = facesSpan[j];
				Face face = mesh.Faces[faceIndex];
				if (facesSpan.Length == 1)
				{
					valueTupleList.Add(new Tuple<int, int>(faceIndex, -1));
				}
				else if (face.cellA == i)
				{
					valueTupleList.Add(new Tuple<int, int>(faceIndex, 1));
				}
				else
				{
					valueTupleList.Add(new Tuple<int, int>(faceIndex, -1));
				}
			}
		}
		hw.WriteDataset(meshGroup + "/Cells Face and Orientation Info", intValues);
		int[,] intValues2 = new int[valueTupleList.Count, 2];
		for (int k = 0; k < valueTupleList.Count; k++)
		{
			Tuple<int, int> tuple = valueTupleList[k];
			intValues2[k, 0] = tuple.Item1;
			intValues2[k, 1] = tuple.Item2;
		}
		hw.WriteDataset(meshGroup + "/Cells Face and Orientation Values", intValues2);
		prog.ReportProgress(30);
		int count2 = mesh.Cells.Max((Cell c) => c.Faces.Length);
		int[,] intValues3 = new int[mesh.CellCount, count2];
		for (int intervalIndex = 0; intervalIndex < mesh.CellCount; intervalIndex++)
		{
			for (int intervalIndex2 = 0; intervalIndex2 < count2; intervalIndex2++)
			{
				intValues3[intervalIndex, intervalIndex2] = -1;
			}
			Cell cell2 = mesh.Cells[intervalIndex];
			Span<int> faces2 = cell2.Faces.Span;
			if (faces2.IsEmpty)
			{
				continue;
			}
			if (faces2.Length == 1)
			{
				int faceIndex2 = faces2[0];
				intValues3[intervalIndex, 0] = mesh.Faces[faceIndex2].fpA;
				intValues3[intervalIndex, 1] = mesh.Faces[faceIndex2].fpB;
				continue;
			}
			for (int intervalIndex3 = 0; intervalIndex3 < faces2.Length; intervalIndex3++)
			{
				Face face2 = mesh.Faces[faces2[intervalIndex3]];
				intValues3[intervalIndex, intervalIndex3] = face2.GetFPPrev(intervalIndex);
			}
		}
		hw.WriteDataset(meshGroup + "/Cells FacePoint Indexes", intValues3);
		prog.ReportProgress(55);
		int[,] intValues4 = new int[mesh.FaceCount, 2];
		int[,] intValues5 = new int[mesh.FaceCount, 2];
		float[,] floatValues = new float[mesh.FaceCount, 3];
		for (int intervalIndex4 = 0; intervalIndex4 < mesh.FaceCount; intervalIndex4++)
		{
			Face face3 = mesh.Faces[intervalIndex4];
			intValues4[intervalIndex4, 0] = face3.fpA;
			intValues4[intervalIndex4, 1] = face3.fpB;
			intValues5[intervalIndex4, 0] = face3.cellA;
			intValues5[intervalIndex4, 1] = face3.cellB;
			Segment segment = mesh.FaceSegment(intervalIndex4);
			Vector vector = segment.PerpendicularUnitVector();
			floatValues[intervalIndex4, 0] = (float)(0.0 - vector.X);
			floatValues[intervalIndex4, 1] = (float)(0.0 - vector.Y);
			floatValues[intervalIndex4, 2] = (float)segment.Length;
		}
		hw.WriteDataset(meshGroup + "/Faces FacePoint Indexes", intValues4);
		hw.WriteDataset(meshGroup + "/Faces Cell Indexes", intValues5);
		hw.WriteDataset(meshGroup + "/Faces NormalUnitVector and Length", floatValues);
		prog.ReportProgress(63);
		int[,] intValues6 = new int[mesh.FaceCount, 2];
		List<Point> points = new List<Point>();
		for (int intervalIndex5 = 0; intervalIndex5 < mesh.FaceCount; intervalIndex5++)
		{
			_ = ref mesh.Faces[intervalIndex5];
			Memory<Point> faceInternalPoint = mesh.FaceInternalPoints[intervalIndex5];
			intValues6[intervalIndex5, 0] = points.Count;
			intValues6[intervalIndex5, 1] = faceInternalPoint.Length;
			if (!faceInternalPoint.IsEmpty)
			{
				points.AddRange(faceInternalPoint.Span);
			}
		}
		if (points.Count > 0)
		{
			hw.WriteDataset(meshGroup + "/Faces Perimeter Info", intValues6);
			HDF5VectorReader.Write(hw, points, meshGroup + "/Faces Perimeter Values");
		}
		HDF5VectorReader.Write(hw, mesh.FacePoints.Select((FacePoint fp) => fp.Point).ToList(), meshGroup + "/FacePoints Coordinate");
		prog.ReportProgress(80);
		int[,] intValues7 = new int[mesh.FacePointCount, 2];
		List<int> scaledFacePointCount = new List<int>(mesh.FacePointCount * 4);
		for (int intervalIndex6 = 0; intervalIndex6 < mesh.FacePointCount; intervalIndex6++)
		{
			_ = ref mesh.FacePoints[intervalIndex6];
			List<int> cellsAroundFacepoint = mesh.GetCellsAroundFacepoint(intervalIndex6);
			intValues7[intervalIndex6, 0] = scaledFacePointCount.Count;
			intValues7[intervalIndex6, 1] = cellsAroundFacepoint.Count;
			scaledFacePointCount.AddRange(cellsAroundFacepoint);
		}
		hw.WriteDataset(meshGroup + "/FacePoints Cell Info", intValues7);
		hw.WriteDataset(meshGroup + "/FacePoints Cell Index Values", scaledFacePointCount.ToArray());
		hw.WriteDataset(meshGroup + "/FacePoints Is Perimeter", (from fpIdx in Enumerable.Range(0, mesh.FacePointCount)
			select mesh.FacePointIsPerimeter(fpIdx) ? (-1) : 0).ToArray());
		List<FaceAroundFacePoint>[] faceAroundFacePointListArray = new List<FaceAroundFacePoint>[mesh.FacePointCount];
		for (int intervalIndex7 = 0; intervalIndex7 < mesh.FaceCount; intervalIndex7++)
		{
			Face face4 = mesh.Faces[intervalIndex7];
			int fpA = face4.fpA;
			int fpB = face4.fpB;
			if (faceAroundFacePointListArray[fpA] == null)
			{
				faceAroundFacePointListArray[fpA] = new List<FaceAroundFacePoint>();
			}
			if (faceAroundFacePointListArray[fpB] == null)
			{
				faceAroundFacePointListArray[fpB] = new List<FaceAroundFacePoint>();
			}
			faceAroundFacePointListArray[fpA].Add(new FaceAroundFacePoint(intervalIndex7, -1, mesh.FaceSegment(intervalIndex7).Bearing, mesh.FaceIsPerimeter(intervalIndex7)));
			faceAroundFacePointListArray[fpB].Add(new FaceAroundFacePoint(intervalIndex7, 1, mesh.FaceSegment(intervalIndex7).Reverse().Bearing, mesh.FaceIsPerimeter(intervalIndex7)));
		}
		for (int intervalIndex8 = 0; intervalIndex8 < mesh.FacePointCount; intervalIndex8++)
		{
			if (faceAroundFacePointListArray[intervalIndex8] == null)
			{
				faceAroundFacePointListArray[intervalIndex8] = new List<FaceAroundFacePoint>();
			}
			faceAroundFacePointListArray[intervalIndex8].Sort(FaceAroundFacePoint.CompareBearings);
			int ordinal = 0;
			foreach (FaceAroundFacePoint faceAroundFacePoint in faceAroundFacePointListArray[intervalIndex8])
			{
				if (faceAroundFacePoint.isPerimeter)
				{
					ordinal++;
				}
			}
			if (ordinal != 2)
			{
				continue;
			}
			if (MeshGenerationParams.DefaultSplitExternalFaces && faceAroundFacePointListArray[intervalIndex8].Count == 2)
			{
				int faceIndex3 = mesh.FacePoints[intervalIndex8].Faces[0];
				if (faceAroundFacePointListArray[intervalIndex8][0].faceIndex != faceIndex3)
				{
					FaceAroundFacePoint faceAroundFacePoint2 = faceAroundFacePointListArray[intervalIndex8][0];
					faceAroundFacePointListArray[intervalIndex8][0] = faceAroundFacePointListArray[intervalIndex8][1];
					faceAroundFacePointListArray[intervalIndex8][1] = faceAroundFacePoint2;
				}
			}
			else
			{
				int ordinal2 = 0;
				for (int count = faceAroundFacePointListArray[intervalIndex8].Count; !faceAroundFacePointListArray[intervalIndex8].First().isPerimeter || !faceAroundFacePointListArray[intervalIndex8].Last().isPerimeter || ordinal2 == count; ordinal2++)
				{
					faceAroundFacePointListArray[intervalIndex8].Add(faceAroundFacePointListArray[intervalIndex8].First());
					faceAroundFacePointListArray[intervalIndex8].RemoveAt(0);
				}
			}
		}
		int[,] intValues8 = new int[mesh.FacePointCount, 2];
		List<Tuple<int, int>> valueTupleListList = new List<Tuple<int, int>>();
		for (int intervalIndex9 = 0; intervalIndex9 < mesh.FacePointCount; intervalIndex9++)
		{
			List<FaceAroundFacePoint> faceAroundFacePoints = faceAroundFacePointListArray[intervalIndex9];
			intValues8[intervalIndex9, 0] = valueTupleListList.Count;
			intValues8[intervalIndex9, 1] = faceAroundFacePoints.Count;
			foreach (int cellsAroundFacepoint2 in mesh.GetCellsAroundFacepoint(intervalIndex9))
			{
				int ccwFaceIdx = mesh.FaceCCWOfCell(intervalIndex9, cellsAroundFacepoint2);
				if (ccwFaceIdx == -1)
				{
					break;
				}
				int index = faceAroundFacePointListArray[intervalIndex9].FirstIndexOf((FaceAroundFacePoint f) => f.faceIndex == ccwFaceIdx);
				valueTupleListList.Add(new Tuple<int, int>(ccwFaceIdx, faceAroundFacePoints[index].faceOrientation));
			}
			FacePoint facePoint = mesh.FacePoints[intervalIndex9];
			int[] faces = new int[facePoint.Faces.Count];
			facePoint.Faces = faces;
			for (int intervalIndex10 = 0; intervalIndex10 < faceAroundFacePoints.Count; intervalIndex10++)
			{
				facePoint.Faces[intervalIndex10] = faceAroundFacePoints[intervalIndex10].faceIndex;
			}
			mesh.FacePoints[intervalIndex9] = facePoint;
		}
		hw.WriteDataset(meshGroup + "/FacePoints Face and Orientation Info", intValues8);
		int[,] intValues9 = new int[valueTupleListList.Count, 2];
		for (int intervalIndex11 = 0; intervalIndex11 < valueTupleListList.Count; intervalIndex11++)
		{
			Tuple<int, int> tuple2 = valueTupleListList[intervalIndex11];
			intValues9[intervalIndex11, 0] = tuple2.Item1;
			intValues9[intervalIndex11, 1] = tuple2.Item2;
		}
		hw.WriteDataset(meshGroup + "/FacePoints Face and Orientation Values", intValues9);
		prog.ReportProgress(100);
		stopwatch.Stop();
		WriteFaceCentroidStations(mesh, hw, meshGroup);
		_logger.LogDebug("[LEGACY] Mesh saved to hdf file in {Duration} ms.", stopwatch.Elapsed.TotalMilliseconds);
		return true;
	}

	private static void WriteFaceCentroidStations(Mesh mesh, H5Writer writer, string meshGroup)
	{
		ReadOnlyMemory<float> stations = mesh.FaceCentroidStations;
		if (stations.IsEmpty) return;
		if (stations.Length != mesh.FaceCount) throw new InvalidOperationException("Face centroid station count no longer matches this mesh.");
		writer.WriteDataset(meshGroup + "/Faces Low Elevation Centroid", stations.ToArray());
	}

	public static string CellMinimumDatset(string meshgroup)
	{
		return meshgroup + "/Cells Minimum Elevation";
	}

	public static bool GetCellMinimum(H5Reader hr, string meshgroup, out float[] cellMinimum)
	{
		string cellMinimumDatset = CellMinimumDatset(meshgroup);
		if (hr.ExistsAsDataset(cellMinimumDatset))
		{
			hr.ReadDataset(cellMinimumDatset, out cellMinimum);
			return true;
		}
		cellMinimum = null;
		return false;
	}

	public static string CellTableName(string meshGroup)
	{
		return TableGroupName(meshGroup) + "/Cell Tables";
	}

	public static string FaceTableName(string meshGroup)
	{
		return TableGroupName(meshGroup) + "/Face Tables";
	}

	public static bool ContainsTables(H5Reader hr, string meshGroup, out bool isLegacy)
	{
		if (hr.ExistsAsDataset(meshGroup + "/Cells Volume Elevation Info"))
		{
			isLegacy = true;
			return true;
		}
		string absolutePath = TableGroupName(meshGroup);
		if (hr.ExistsAsGroup(absolutePath))
		{
			isLegacy = false;
			string absolutePath2 = CellTableName(meshGroup);
			string absolutePath3 = FaceTableName(meshGroup);
			if (!hr.ExistsAsDataset(absolutePath2) || !hr.ExistsAsDataset(absolutePath3))
			{
				return false;
			}
			return true;
		}
		isLegacy = false;
		return false;
	}

	public static MeshPropertyTables ReadTables(Mesh mesh, H5Reader hr, string meshGroup, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (!ContainsTables(hr, meshGroup, out var isLegacy))
		{
			return null;
		}
		if (isLegacy)
		{
			return ReadLegacyTables(mesh, hr, meshGroup, reporter);
		}
		return ReadNewTables(mesh, hr, meshGroup, reporter);
	}

	private static MeshPropertyTables ReadLegacyTables(Mesh mesh, H5Reader hr, string meshGroup, ProgressReporter reporter)
	{
		MeshPropertyTables meshPropertyTables = MeshPropertyTables.CreateEmpty(mesh);
		hr.ReadDataset(meshGroup + "/Cells Volume Elevation Info", out int[,] values);
		hr.ReadDataset(meshGroup + "/Cells Volume Elevation Values", out float[,] values2);
		int length = values2.GetLength(0);
		meshPropertyTables.InitCellTables(values2.GetLength(0));
		for (int i = 0; i < mesh.CellCount; i++)
		{
			meshPropertyTables.CellTableStart[i] = values[i, 0];
			meshPropertyTables.CellTableCount[i] = (short)values[i, 1];
		}
		for (int j = 0; j < length; j++)
		{
			meshPropertyTables.CellElevationTable[j] = values2[j, 0];
			meshPropertyTables.CellVolumeTable[j] = values2[j, 1];
		}
		hr.ReadDataset(meshGroup + "/Faces Area Elevation Info", out int[,] values3);
		hr.ReadDataset(meshGroup + "/Faces Area Elevation Values", out float[,] values4);
		int length2 = values4.GetLength(0);
		meshPropertyTables.InitFaceTables(length2);
		for (int k = 0; k < mesh.FaceCount; k++)
		{
			meshPropertyTables.FaceTableStart[k] = values3[k, 0];
			meshPropertyTables.FaceTableCount[k] = (short)values3[k, 1];
		}
		for (int l = 0; l < length2; l++)
		{
			meshPropertyTables.FaceElevationTable[l] = values4[l, 0];
			meshPropertyTables.FaceAreaTable[l] = values4[l, 1];
			meshPropertyTables.FaceWettedPerimeterTable[l] = values4[l, 2];
			meshPropertyTables.FaceManningsTable[l] = values4[l, 3];
		}
		meshPropertyTables.ComputeCellWettedAreaTables();
		meshPropertyTables.RebuildMinElevationsFromFullTables();
		return meshPropertyTables;
	}

	public static string TableGroupName(string meshGroup)
	{
		return meshGroup + "/Property Tables";
	}

	public static string TableSignatureGroupName(string meshGroup)
	{
		return TableGroupName(meshGroup) + "/Signature";
	}

	private static MeshPropertyTables ReadNewTables(Mesh mesh, H5Reader hr, string meshGroup, ProgressReporter reporter)
	{
		MeshPropertyTables meshPropertyTables = MeshPropertyTables.CreateEmpty(mesh);
		string absolutePath = TableGroupName(meshGroup);
		if (!hr.ExistsAsGroup(absolutePath))
		{
			return meshPropertyTables;
		}
		string cellTableName = CellTableName(meshGroup);
		hr.ReadDataset(cellTableName, out float[,] values);
		hr.ReadAttribute(cellTableName, "Start", out meshPropertyTables.CellTableStart);
		int length = values.GetLength(0);
		meshPropertyTables.InitCellTables(length);
		for (int i = 0; i < length; i++)
		{
			meshPropertyTables.CellElevationTable[i] = values[i, 0];
			meshPropertyTables.CellVolumeTable[i] = values[i, 1];
		}
		string faceTableName = FaceTableName(meshGroup);
		hr.ReadDataset(faceTableName, out float[,] values2);
		hr.ReadAttribute(faceTableName, "Start", out meshPropertyTables.FaceTableStart);
		int length2 = values2.GetLength(0);
		meshPropertyTables.InitFaceTables(length2);
		for (int j = 0; j < length2; j++)
		{
			meshPropertyTables.FaceElevationTable[j] = values2[j, 0];
			meshPropertyTables.FaceAreaTable[j] = values2[j, 1];
			meshPropertyTables.FaceWettedPerimeterTable[j] = values2[j, 2];
			meshPropertyTables.FaceManningsTable[j] = values2[j, 3];
		}
		meshPropertyTables.RebuildMinElevationsFromFullTables();
		meshPropertyTables.RebuildTableCountsFromStarts();
		meshPropertyTables.ComputeCellWettedAreaTables();
		return meshPropertyTables;
	}

	public static void WriteTables(MeshPropertyTables tables, H5Writer hw, string meshGroup, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		string tableGroupName = TableGroupName(meshGroup);
		if (hw.ExistsAsGroup(tableGroupName))
		{
			hw.Delete(tableGroupName);
		}
		hw.CreateGroup(tableGroupName);
		string tableGroupNameTerm = tableGroupName + "/Cell Tables";
		float[,] floatValues = new float[tables.CellVolumeTable.Length, 2];
		for (int i = 0; i < tables.CellVolumeTable.Length; i++)
		{
			floatValues[i, 0] = tables.CellElevationTable[i];
			floatValues[i, 1] = tables.CellVolumeTable[i];
		}
		hw.WriteDataset(tableGroupNameTerm, floatValues);
		string tableGroupNameTerm2 = tableGroupName + "/Face Tables";
		float[,] floatValues2 = new float[tables.FaceAreaTable.Length, 4];
		for (int j = 0; j < tables.FaceAreaTable.Length; j++)
		{
			floatValues2[j, 0] = tables.FaceElevationTable[j];
			floatValues2[j, 1] = tables.FaceAreaTable[j];
			floatValues2[j, 2] = tables.FaceWettedPerimeterTable[j];
			floatValues2[j, 3] = tables.FaceManningsTable[j];
		}
		hw.WriteDataset(tableGroupNameTerm2, floatValues2);
		hw.WriteAttribute(tableGroupNameTerm, "Start", tables.CellTableStart);
		hw.WriteAttribute(tableGroupNameTerm2, "Start", tables.FaceTableStart);
		string datasetName = tableGroupName + "/Cell Minimum Elevation";
		string datasetName2 = tableGroupName + "/Face Minimum Elevation";
		if (tables.CellMinimumElevations == null || tables.FaceMinimumElevations == null)
		{
			_logger.LogWarning("NO MIN ELEVATIONS WHEN SAVING PROPERTY TABLES!");
			tables.RebuildMinElevationsFromFullTables();
		}
		hw.WriteDataset(datasetName, tables.CellMinimumElevations);
		hw.WriteDataset(datasetName2, tables.FaceMinimumElevations);
	}
}
