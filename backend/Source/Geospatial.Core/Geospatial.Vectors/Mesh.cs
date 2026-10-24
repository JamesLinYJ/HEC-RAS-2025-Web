using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors.Meshes;
using Utility;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.Matrices;
using Utility.Memory;
using Utility.Systems;

namespace Geospatial.Vectors;

[DebuggerDisplay("{CellCount} Cells")]
public class Mesh : IDataHash, IMutableSpatialVersion, ISpatialVersion, IExtent
{
	public static class MessageCodes
	{
		public static readonly MessageClass BadCellPolygon = new MessageClass("Mesh_BadCellPolygon", MessageSeverity.Error, "Mesh has a bad cell polygon. Index={CellIndex}, near ({Coordinate}).");

		public static readonly MessageClass CellHasNoFaces = new MessageClass("Mesh_CellHasNoFaces", MessageSeverity.Error, "Cell {CellIndex} has no faces.");

		public static readonly MessageClass CellFaceIndexMismatch = new MessageClass("Mesh_CellFaceIndexMismatch", MessageSeverity.Error, "Cell {CellIndex} and face {FaceIndex} have incorrect bi-directional pointers.");

		public static readonly MessageClass FaceNodeIndexMismatch = new MessageClass("Mesh_FaceNodeIndexMismatch", MessageSeverity.Error, "Face {FaceIndex} and node {FacePointIndex} have incorrect bi-directional pointers.");

		public static readonly MessageClass FaceHasNoCellA = new MessageClass("Mesh_FaceHasNoCellA", MessageSeverity.Error, "Face {FaceIndex} has no CellA. This is not allowed with our mesh generation. (Perimeter faces must set CellB = -1)");

		public static readonly MessageClass FaceHasBadNodes = new MessageClass("Mesh_FaceHasBadNodes", MessageSeverity.Error, "Face {FaceIndex} has bad node indexes (one or both set to -1).");

		public static readonly MessageClass FaceOrientationError = new MessageClass("Mesh_FaceOrientationError", MessageSeverity.Error, "Face {FaceIndex} appears to be backwards (fpA/fpB orientation mismatch).");

		public static readonly MessageClass NodeHasDuplicatedFaceEntries = new MessageClass("Mesh_NodeHasDuplicatedFaceEntries", MessageSeverity.Error, "Node {FacePointIndex} has duplicated face entries.");

		public static readonly MessageClass GeneralMeshError = new MessageClass("Mesh_GeneralError", MessageSeverity.Error, "General mesh error: {ErrorMessage}");

		public static Message GetBadCellPolygon(int cIdx, Point pt)
		{
			return BadCellPolygon.Create(cIdx, pt);
		}

		public static Message GetCellHasNoFaces(int cIdx)
		{
			return CellHasNoFaces.Create(cIdx);
		}

		public static Message GetCellFaceIndexMismatch(int cIdx, int fIdx)
		{
			return CellFaceIndexMismatch.Create(cIdx, fIdx);
		}

		public static Message GetFaceNodeIndexMismatch(int fIdx, int nIdx)
		{
			return FaceNodeIndexMismatch.Create(fIdx, nIdx);
		}

		public static Message GetFaceHasNoCellA(int fIdx)
		{
			return FaceHasNoCellA.Create(fIdx);
		}

		public static Message GetFaceHasBadNodes(int fIdx)
		{
			return FaceHasBadNodes.Create(fIdx);
		}

		public static Message GetFaceOrientationError(int fIdx)
		{
			return FaceOrientationError.Create(fIdx);
		}

		public static Message GetNodeHasDuplicatedFaceEntries(int nIdx)
		{
			return NodeHasDuplicatedFaceEntries.Create(nIdx);
		}

		public static Message GetGeneralMeshError(string innerMessage)
		{
			return GeneralMeshError.Create(innerMessage);
		}
	}

	public FacePoint[] FacePoints;

	public Face[] Faces;

	public Cell[] Cells;

	public Point[] CellCenters;

	public Memory<Point>[] FaceInternalPoints;

	private VersionTracker _perimeterTracker;

	private Polygon _perimeter;

	private long _versionXy = 1L;

	private Freezer _freezer;

	private readonly Versioned<Extent> _extVersion = new Versioned<Extent>();

	private readonly object _jitLock = new object();

	private BufferPool<float> _cellPool;

	private BufferPool<float> _facePool;

	private BufferPool<float> _facePointPool;

	private HydraulicMeshComputer _hmesh;

	private MeshStatistics _meshStatistics;

	private float[] _faceCentroidStation;

	private readonly object _squareCellLock = new object();

	private BitArray _squareCells;

	public Polygon Perimeter
	{
		get
		{
			return _perimeter;
		}
		set
		{
			_perimeterTracker.Assign(ref _perimeter, value);
		}
	}

	public VirtualMeshCellPolygonCollection CellPolygons { get; private set; }

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _versionXy + _perimeterTracker.GetVersion(Perimeter);
		}
	}

	public Extent Extent => _extVersion.Get(VersionXY, this, ComputeExtent);

	public int CellCount => Cells?.Length ?? 0;

	public int FacePointCount => FacePoints?.Length ?? 0;

	public int NodeCount => FacePoints?.Length ?? 0;

	public int FaceCount => Faces?.Length ?? 0;

	public BufferPool<float> CellPool => LazyThreadsafe<BufferPool<float>>.AllocateOnce(ref _cellPool, _jitLock, () => new BufferPool<float>(CellCount));

	public BufferPool<float> FacePool => LazyThreadsafe<BufferPool<float>>.AllocateOnce(ref _facePool, _jitLock, () => new BufferPool<float>(FaceCount));

	public BufferPool<float> FacePointPool => LazyThreadsafe<BufferPool<float>>.AllocateOnce(ref _facePointPool, _jitLock, () => new BufferPool<float>(FacePointCount));

	public HydraulicMeshComputer MetadataCache => _hmesh;

	public StreamwiseData StreamwiseData { get; set; }

	public double AreaTolerance => GetMeshStatistics().CellMinimumArea / 10000.0;

	public double LengthTolerance => Math.Sqrt(GetMeshStatistics().CellMinimumArea) * 0.01;

	public bool IsValid()
	{
		if (Cells.IsNullOrEmpty() || Faces.IsNullOrEmpty() || FacePoints.IsNullOrEmpty() || Extent.IsNull())
		{
			return false;
		}
		return true;
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public void ResetMetadataCache()
	{
		_hmesh = new HydraulicMeshComputer(this);
	}

	public MeshStatistics GetMeshStatistics()
	{
		if (_meshStatistics == null)
		{
			_meshStatistics = MeshStatistics.ComputeCellStatistics(this);
		}
		return _meshStatistics;
	}

	public static Mesh NullMesh()
	{
		return new Mesh
		{
			Cells = Array.Empty<Cell>(),
			Faces = Array.Empty<Face>(),
			FacePoints = Array.Empty<FacePoint>(),
			Perimeter = Polygon.NullPolygon(),
			FaceInternalPoints = Array.Empty<Memory<Point>>()
		};
	}

	public Mesh()
	{
		CellPolygons = new VirtualMeshCellPolygonCollection(this);
		_hmesh = new HydraulicMeshComputer(this);
	}

	public Mesh(Polygon perim, Cell[] cells, Face[] faces, FacePoint[] facePoints, Point[] cellCenters, Memory<Point>[] faceInternalPoints)
		: this()
	{
		Cells = cells;
		Faces = faces;
		FacePoints = facePoints;
		perim.EnsureCounterClockwise();
		Perimeter = perim;
		CellCenters = cellCenters;
		if (faceInternalPoints == null)
		{
			FaceInternalPoints = new Memory<Point>[faces.Length];
		}
		else
		{
			FaceInternalPoints = faceInternalPoints;
		}
	}

	public Mesh(Tin tin)
		: this()
	{
		int triangleCount = tin.TriangleCount;
		Cells = new Cell[triangleCount];
		for (int i = 0; i < triangleCount; i++)
		{
		}
		throw new NotImplementedException();
	}

	public static Mesh FromRasterDefinition(RasterDefinition rdef)
	{
		Polygon.FromExtent(rdef.Extent);
		Cell[] cells = new Cell[rdef.CellCount];
		Point[] points = new Point[rdef.CellCount];
		int rows = rdef.Rows;
		int cols = rdef.Cols;
		Face[] faces = new Face[2 * (rows - 1) * cols + (rows - 1) + (cols - 1)];
		_ = new FacePoint[cols * rows + 2 * rows + 1];
		for (int i = 0; i < rdef.Rows; i++)
		{
			for (int j = 0; j < rdef.Cols; j++)
			{
				int colsRdefTerm = i * rdef.Cols + j;
				int colsTerm = 2 * i * cols + i + j;
				int colsTerm2 = colsTerm + cols;
				int ordinal = colsTerm2 + 1;
				int colsTerm3 = colsTerm + 2 * cols + 1;
				int fpA = colsRdefTerm + i;
				int fpAOffset = fpA + 1;
				int fpATerm = fpA + i + 1;
				int fpB = fpATerm + 1;
				Cell cell = new Cell();
				cell.Faces = new int[4] { colsTerm, colsTerm2, colsTerm3, ordinal };
				Cell cell2 = cell;
				cells[colsRdefTerm] = cell2;
				points[colsRdefTerm] = rdef.CellCenter(colsRdefTerm);
				int cellA = colsRdefTerm - cols;
				if (i == 0)
				{
					cellA = -1;
				}
				int cellB = colsRdefTerm + cols;
				if (i == rdef.Rows - 1)
				{
					cellB = -1;
				}
				int cellB2 = colsRdefTerm - 1;
				if (j == 0)
				{
					cellB2 = -1;
				}
				int cellA2 = colsRdefTerm + 1;
				if (j == rdef.Cols - 1)
				{
					cellA2 = -1;
				}
				faces[colsTerm].fpA = fpA;
				faces[colsTerm].fpB = fpAOffset;
				faces[colsTerm].cellA = cellA;
				faces[colsTerm].cellB = colsRdefTerm;
				faces[colsTerm3].fpA = fpATerm;
				faces[colsTerm3].fpB = fpB;
				faces[colsTerm3].cellA = colsRdefTerm;
				faces[colsTerm3].cellB = cellB;
				faces[colsTerm2].fpA = fpA;
				faces[colsTerm2].fpB = fpATerm;
				faces[colsTerm2].cellA = colsRdefTerm;
				faces[colsTerm2].cellB = cellB2;
				faces[ordinal].fpA = fpAOffset;
				faces[ordinal].fpB = fpB;
				faces[ordinal].cellA = cellA2;
				faces[ordinal].cellB = colsRdefTerm;
			}
		}
		throw new NotImplementedException();
	}

	public void ForceSetVersion(long versionXy)
	{
		_versionXy = versionXy;
	}

	public void Stream(BinaryWriter bw)
	{
		Perimeter.Stream(bw);
		Cell.Stream(bw, Cells);
		double[] doubleValues = new double[CellCenters.Length * 2];
		for (int i = 0; i < CellCenters.Length; i++)
		{
			doubleValues[i * 2] = CellCenters[i].X;
			doubleValues[i * 2 + 1] = CellCenters[i].Y;
		}
		Pipe.WriteArray(bw, doubleValues);
		Face.Stream(bw, Faces);
		FacePoint.Stream(bw, FacePoints);
		bw.Write(FaceInternalPoints.Length);
		for (int j = 0; j < FaceInternalPoints.Length; j++)
		{
			Memory<Point> arr = FaceInternalPoints[j];
			WriteMeshPointArray(bw, arr);
		}
	}

	private static void WriteMeshPointArray(BinaryWriter writer, Memory<Point> points)
	{
		writer.Write(points.Length);
		if (!points.IsEmpty)
		{
			writer.Write(MemoryMarshal.AsBytes(points.Span).ToArray());
		}
	}

	public static Mesh Stream(BinaryReader br)
	{
		Polygon perimeter = ReadMeshPerimeter(br);
		Cell[] cells = ReadMeshCells(br);
		double[] centerValues = ReadMeshArray<double>(br, "cell centers");
		if ((centerValues.Length & 1) != 0)
		{
			throw new InvalidDataException("Mesh stream contains an odd number of cell-center coordinates.");
		}
		Point[] cellCenters = new Point[centerValues.Length / 2];
		for (int i = 0; i < cellCenters.Length; i++)
		{
			cellCenters[i] = new Point(centerValues[i * 2], centerValues[i * 2 + 1]);
		}
		if (cellCenters.Length != cells.Length)
		{
			throw new InvalidDataException("Mesh stream cell-center and cell counts do not match.");
		}
		Face[] faces = ReadMeshFaces(br);
		FacePoint[] facePoints = ReadMeshFacePoints(br);
		int internalPointCount = ReadMeshCount(br, "face-internal point arrays");
		EnsureRemainingBytes(br, (long)internalPointCount * sizeof(int), "face-internal point arrays");
		if (internalPointCount != faces.Length)
		{
			throw new InvalidDataException("Mesh stream face-internal point and face counts do not match.");
		}
		Memory<Point>[] faceInternalPoints = new Memory<Point>[internalPointCount];
		for (int i = 0; i < internalPointCount; i++)
		{
			faceInternalPoints[i] = ReadMeshArray<Point>(br, "face-internal points");
		}

		Mesh mesh = new Mesh
		{
			Perimeter = perimeter,
			Cells = cells,
			CellCenters = cellCenters,
			Faces = faces,
			FacePoints = facePoints,
			FaceInternalPoints = faceInternalPoints
		};
		return mesh;
	}

	private static Polygon ReadMeshPerimeter(BinaryReader reader)
	{
		double[] coordinates = ReadMeshArray<double>(reader, "perimeter coordinates");
		if ((coordinates.Length & 1) != 0)
		{
			throw new InvalidDataException("Mesh stream perimeter has an odd number of coordinates.");
		}
		Polygon perimeter = new Polygon(coordinates.Length / 2);
		for (int i = 0; i < coordinates.Length; i += 2)
		{
			perimeter.Add(new Point(coordinates[i], coordinates[i + 1]));
		}
		return perimeter;
	}

	private static Cell[] ReadMeshCells(BinaryReader reader)
	{
		int count = ReadMeshCount(reader, "cells");
		EnsureRemainingBytes(reader, (long)count * sizeof(int), "cells");
		Cell[] cells = new Cell[count];
		for (int i = 0; i < count; i++)
		{
			Cell cell = new Cell();
			cell.Faces = ReadMeshArray<int>(reader, "cell faces");
			cells[i] = cell;
		}
		return cells;
	}

	private static Face[] ReadMeshFaces(BinaryReader reader)
	{
		int count = ReadMeshCount(reader, "faces");
		EnsureRemainingBytes(reader, (long)count * sizeof(int) * 4, "faces");
		Face[] faces = new Face[count];
		for (int i = 0; i < count; i++)
		{
			Face face = new Face
			{
				cellA = reader.ReadInt32(),
				cellB = reader.ReadInt32(),
				fpA = reader.ReadInt32(),
				fpB = reader.ReadInt32()
			};
			faces[i] = face;
		}
		return faces;
	}

	private static FacePoint[] ReadMeshFacePoints(BinaryReader reader)
	{
		int count = ReadMeshCount(reader, "face points");
		EnsureRemainingBytes(reader, (long)count * (sizeof(double) * 2 + sizeof(int)), "face points");
		FacePoint[] facePoints = new FacePoint[count];
		for (int i = 0; i < count; i++)
		{
			Point point = new Point(reader.ReadDouble(), reader.ReadDouble());
			facePoints[i] = new FacePoint(point)
			{
				Faces = ReadMeshArray<int>(reader, "face-point faces")
			};
		}
		return facePoints;
	}

	private static int ReadMeshCount(BinaryReader reader, string section)
	{
		int count = reader.ReadInt32();
		if (count < 0)
		{
			throw new InvalidDataException("Mesh stream contains a negative count for " + section + ".");
		}
		return count;
	}

	private static T[] ReadMeshArray<T>(BinaryReader reader, string section) where T : struct
	{
		int count = ReadMeshCount(reader, section);
		if (count == 0)
		{
			return Array.Empty<T>();
		}
		int stride = Marshal.SizeOf<T>();
		long byteCount = (long)count * stride;
		if (byteCount > int.MaxValue)
		{
			throw new InvalidDataException("Mesh stream array is too large for " + section + ".");
		}
		EnsureRemainingBytes(reader, byteCount, section);
		byte[] bytes = reader.ReadBytes((int)byteCount);
		if (bytes.Length != byteCount)
		{
			throw new EndOfStreamException("Mesh stream ended while reading " + section + ".");
		}
		T[] values = new T[count];
		bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(values.AsSpan()));
		return values;
	}

	private static void EnsureRemainingBytes(BinaryReader reader, long byteCount, string section)
	{
		Stream stream = reader.BaseStream;
		if (!stream.CanSeek)
		{
			return;
		}
		long remaining = stream.Length - stream.Position;
		if (remaining < byteCount)
		{
			throw new EndOfStreamException("Mesh stream ended while reading " + section + ".");
		}
	}

	public OperationResult SanityCheck(bool xpDirectionalChecks = true)
	{
		if (CellCenters.Length != Cells.Length)
		{
			return OperationResult.Fail(MessageCodes.GetGeneralMeshError("CellCenters/Cells size disagreement"));
		}
		OperationResult operationResult = new OperationResult();
		for (int i = 0; i < Cells.Length; i++)
		{
			Cell cell = Cells[i];
			Span<int> faces = cell.Faces.Span;
			if (faces.IsEmpty)
			{
				operationResult.AddFailure(MessageCodes.GetCellHasNoFaces(i));
				continue;
			}
			bool condition = true;
			Span<int> facesSpan = faces;
			for (int j = 0; j < facesSpan.Length; j++)
			{
				int faceIndex = facesSpan[j];
				Face face = Faces[faceIndex];
				if (face.cellA != i && face.cellB != i)
				{
					condition = false;
					operationResult.AddFailure(MessageCodes.GetCellFaceIndexMismatch(i, faceIndex));
					break;
				}
			}
			if (condition && CellPolygon(i).Area <= 0.0)
			{
				operationResult.AddFailure(MessageCodes.GetBadCellPolygon(i, CellCenters[i]));
			}
		}
		for (int k = 0; k < Faces.Length; k++)
		{
			Face face2 = Faces[k];
			if (face2.fpA < 0 || face2.fpB < 0)
			{
				operationResult.AddFailure(MessageCodes.GetFaceHasBadNodes(k));
				continue;
			}
			FacePoint facePoint = FacePoints[face2.fpA];
			FacePoint facePoint2 = FacePoints[face2.fpB];
			if (face2.cellA == -1)
			{
				operationResult.AddFailure(MessageCodes.GetFaceHasNoCellA(k));
				continue;
			}
			Cell cell2 = Cells[face2.cellA];
			Span<int> faces2 = cell2.Faces.Span;
			bool condition2 = false;
			Span<int> faces3 = faces2;
			for (int j = 0; j < faces3.Length; j++)
			{
				if (faces3[j] == k)
				{
					condition2 = true;
					break;
				}
			}
			if (!condition2)
			{
				operationResult.AddFailure(MessageCodes.GetCellFaceIndexMismatch(face2.cellA, k));
				continue;
			}
			if (xpDirectionalChecks)
			{
				Point c = CellCenters[face2.cellA];
				if (Vector.CrossProduct(in facePoint.Point, in facePoint2.Point, in c) <= 0.0)
				{
					operationResult.AddFailure(MessageCodes.GetFaceOrientationError(k));
					continue;
				}
			}
			if (face2.cellB != -1)
			{
				Cell cell3 = Cells[face2.cellB];
				Span<int> faces4 = cell3.Faces.Span;
				bool condition3 = false;
				Span<int> faces5 = faces4;
				for (int j = 0; j < faces5.Length; j++)
				{
					if (faces5[j] == k)
					{
						condition3 = true;
						break;
					}
				}
				if (!condition3)
				{
					operationResult.AddFailure(MessageCodes.GetCellFaceIndexMismatch(face2.cellB, k));
					continue;
				}
			}
			bool condition4 = false;
			foreach (int face3 in facePoint.Faces)
			{
				if (face3 == k)
				{
					condition4 = true;
					break;
				}
			}
			if (!condition4)
			{
				operationResult.AddFailure(MessageCodes.GetFaceNodeIndexMismatch(k, face2.fpA));
				continue;
			}
			bool condition5 = false;
			foreach (int face4 in facePoint2.Faces)
			{
				if (face4 == k)
				{
					condition5 = true;
					break;
				}
			}
			if (!condition5)
			{
				operationResult.AddFailure(MessageCodes.GetFaceNodeIndexMismatch(k, face2.fpB));
			}
		}
		for (int l = 0; l < FacePointCount; l++)
		{
			FacePoint facePoint3 = FacePoints[l];
			if (facePoint3.Faces.Count != facePoint3.Faces.Distinct().Count())
			{
				operationResult.AddFailure(MessageCodes.GetNodeHasDuplicatedFaceEntries(l));
			}
		}
		return operationResult;
	}

	public void MD5HashAppend(Hash hash)
	{
		hash.Add(CellCenters);
		Perimeter.MD5HashAppend(hash);
	}

	public static void CompareMeshes(Mesh meshA, Mesh meshB, ref Tuple<int, bool>[] faceMatches, ref int[] cellMatches)
	{
		Extent ext = meshA.Extent.Union(meshB.Extent).RatioAroundCenter(1.01);
		double tol = Math.Min(meshA.LengthTolerance, meshB.LengthTolerance);
		faceMatches = new Tuple<int, bool>[meshA.FaceCount];
		cellMatches = new int[meshA.CellCount];
		Tuple<int, bool>[] faceMatchesLocal = faceMatches;
		int[] cellMatchesLocal = cellMatches;
		SpatialIndex<Tuple<bool, int>> faceSpIdx = new SpatialIndex<Tuple<bool, int>>(ext);
		for (int i = 0; i < meshA.Faces.Length; i++)
		{
			faceSpIdx.Add(meshA.FaceSegment(i).Midpoint(), new Tuple<bool, int>(item1: true, i));
		}
		for (int j = 0; j < meshB.Faces.Length; j++)
		{
			faceSpIdx.Add(meshB.FaceSegment(j).Midpoint(), new Tuple<bool, int>(item1: false, j));
		}
		System.Threading.Tasks.Parallel.For(0, faceSpIdx.Bins.Length, (int binIdx) =>
		{
			List<Tuple<bool, int>> bin = faceSpIdx.Bins[binIdx];
			if (!bin.IsNullOrEmpty())
			{
				HashSet<int> intSet = new HashSet<int>();
				for (int k = 0; k < bin.Count; k++)
				{
					Tuple<bool, int> tuple = bin[k];
					if (tuple.Item1)
					{
						int tupleTerm = tuple.Item2;
						Face face = meshA.Faces[tupleTerm];
						Point point = meshA.FacePoints[face.fpA].Point;
						Point point2 = meshA.FacePoints[face.fpB].Point;
						for (int l = k + 1; l < bin.Count; l++)
						{
							Tuple<bool, int> tuple2 = bin[l];
							if (!tuple2.Item1 && !intSet.Contains(l))
							{
								int tuple2Term = tuple2.Item2;
								Face face2 = meshB.Faces[tuple2Term];
								Point other = meshB.FacePoints[face2.fpA].Point;
								Point other2 = meshB.FacePoints[face2.fpB].Point;
								if (point.EqualXY(in other) && point2.EqualXY(in other2))
								{
									intSet.Add(l);
									faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: true);
									break;
								}
								double distanceTo = point.DistanceTo(other);
								double distanceTo2 = point.DistanceTo(other2);
								double distanceTo3 = point2.DistanceTo(other);
								double distanceTo4 = point2.DistanceTo(other2);
								if (distanceTo < tol && distanceTo4 < tol)
								{
									intSet.Add(l);
									faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: true);
									break;
								}
								if (distanceTo2 < tol && distanceTo3 < tol)
								{
									intSet.Add(l);
									faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: false);
									break;
								}
							}
						}
					}
				}
			}
		});
		Utility.Parallel.SmartFor(meshA.CellCount, (int start, int fin) =>
		{
			for (int k = start; k <= fin; k++)
			{
				Cell cell = meshA.Cells[k];
				Span<int> faces = cell.Faces.Span;
				bool condition = true;
				int minValue = int.MinValue;
				Span<int> facesSpan = faces;
				for (int l = 0; l < facesSpan.Length; l++)
				{
					int faceIndex = facesSpan[l];
					Tuple<int, bool> tuple = faceMatchesLocal[faceIndex];
					if (tuple == null)
					{
						condition = false;
						break;
					}
					Face face = meshA.Faces[faceIndex];
					Face face2 = meshB.Faces[tuple.Item1];
					int neighborCell;
					int neighborCell2;
					if (tuple.Item2)
					{
						neighborCell = face2.cellA;
						neighborCell2 = face2.cellB;
					}
					else
					{
						neighborCell = face2.cellB;
						neighborCell2 = face2.cellA;
					}
					if (face.cellA == k)
					{
						if (minValue == int.MinValue)
						{
							minValue = neighborCell;
						}
						else if (minValue != neighborCell)
						{
							condition = false;
							break;
						}
					}
					else if (minValue == int.MinValue)
					{
						minValue = neighborCell2;
					}
					else if (minValue != neighborCell2)
					{
						condition = false;
						break;
					}
				}
				if (condition)
				{
					cellMatchesLocal[k] = minValue;
				}
				else
				{
					cellMatchesLocal[k] = -1;
				}
			}
		});
	}

	public Mesh Transform(ISpatialTransformer transformer)
	{
		Cell[] cells = new Cell[CellCount];
		FacePoint[] facePoints = new FacePoint[FacePointCount];
		Face[] faces = new Face[FaceCount];
		transformer.Transform(CellCenters);
		for (int i = 0; i < CellCount; i++)
		{
			cells[i] = Cells[i].Clone();
		}
		Point[] transform = transformer.Transform(FacePoints.Select((FacePoint c) => c.Point).ToArray());
		for (int intervalIndex = 0; intervalIndex < FacePointCount; intervalIndex++)
		{
			facePoints[intervalIndex] = FacePoints[intervalIndex].Clone();
			facePoints[intervalIndex].Point = transform[intervalIndex];
		}
		for (int intervalIndex2 = 0; intervalIndex2 < FaceCount; intervalIndex2++)
		{
			faces[intervalIndex2] = Faces[intervalIndex2].Clone();
		}
		Polygon polygon = Perimeter.Clone();
		polygon.Transform(transformer);
		Memory<Point>[] pointSpanArray = new Memory<Point>[FaceCount];
		for (int intervalIndex3 = 0; intervalIndex3 < FaceInternalPoints.Length; intervalIndex3++)
		{
			Memory<Point> points = FaceInternalPoints[intervalIndex3];
			if (!points.IsEmpty)
			{
				Point[] pointsArray = new Point[points.Length];
				transformer.Transform(points, pointsArray);
				pointSpanArray[intervalIndex3] = pointsArray;
			}
		}
		return new Mesh(polygon, cells, faces, facePoints, CellCenters, pointSpanArray);
	}

	public bool CellIsVirtual(int cIdx)
	{
		return Cells[cIdx].Faces.Length == 1;
	}

	public bool CellIsPerimeter(int cIdx)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (faces.Length == 1)
		{
			return true;
		}
		Span<int> facesSpan = faces;
		for (int i = 0; i < facesSpan.Length; i++)
		{
			int f = facesSpan[i];
			if (FaceIsPerimeter(f))
			{
				return true;
			}
		}
		return false;
	}

	public Extent CellExtent(int cIdx)
	{
		if (cIdx >= Cells.Length)
		{
			throw new ArgumentOutOfRangeException("cIdx");
		}
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (faces.IsEmpty)
		{
			return Extent.NullExtent;
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < faces.Length; i++)
		{
			Face face = Faces[faces[i]];
			Point point = FacePoints[face.GetFPPrev(cIdx)].Point;
			maxValue = Math.Min(maxValue, point.X);
			minValue = Math.Max(minValue, point.X);
			maxValue2 = Math.Min(maxValue2, point.Y);
			minValue2 = Math.Max(minValue2, point.Y);
			Span<Point> faceInternalPoint = FaceInternalPoints[faces[i]].Span;
			for (int j = 0; j < faceInternalPoint.Length; j++)
			{
				Point point2 = faceInternalPoint[j];
				maxValue = Math.Min(maxValue, point2.X);
				minValue = Math.Max(minValue, point2.X);
				maxValue2 = Math.Min(maxValue2, point2.Y);
				minValue2 = Math.Max(minValue2, point2.Y);
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public Polygon CellPolygon(int c)
	{
		return Polygon.CreateWithBuffer(CellPolygonPoints(c));
	}

	public ValuePolygon CellValuePolygon(int cIdx)
	{
		Point[] points = new Point[GetCellPolygonPointCount(cIdx)];
		TryGetCellPolygonPoints(cIdx, points);
		return new ValuePolygon(points);
	}

	public Polygon CellNodes(int c)
	{
		return Polygon.CreateWithBuffer(CellNodePoints(c));
	}

	public bool CellContains(int cIdx, Point pt)
	{
		return CellPolygon(cIdx).Contains(pt);
	}

	public double CellPolygonArea(int c)
	{
		Cell cell = Cells[c];
		Span<int> faces = cell.Faces.Span;
		double addSegmentSum = 0.0;
		for (int i = 0; i < cell.Faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			int fPPrev = face.GetFPPrev(c);
			Point pA = FacePoints[fPPrev].Point;
			Span<Point> faceInternalPoint = ((FaceInternalPoints != null) ? FaceInternalPoints[faceIndex].Span : default(Span<Point>));
			if (!faceInternalPoint.IsEmpty)
			{
				if (IsFaceInternalPointsReversed(face, c))
				{
					for (int lengthOffset = faceInternalPoint.Length - 1; lengthOffset >= 0; lengthOffset--)
					{
						Point point = faceInternalPoint[lengthOffset];
						addSegmentSum += AddSegment(pA, point);
						pA = point;
					}
				}
				else
				{
					for (int j = 0; j < faceInternalPoint.Length; j++)
					{
						Point point2 = faceInternalPoint[j];
						addSegmentSum += AddSegment(pA, point2);
						pA = point2;
					}
				}
			}
			Point point3 = FacePoints[face.GetFPNext(c)].Point;
			addSegmentSum += AddSegment(pA, point3);
		}
		return Math.Abs(addSegmentSum * 0.5);
		static double AddSegment(Point point4, Point pB)
		{
			double pBPoint4Term = pB.X - point4.X;
			double y = point4.Y;
			double y2 = pB.Y;
			return (y + y2) * pBPoint4Term;
		}
	}

	public double CellSimpleArea(int c)
	{
		return CellNodes(c)?.Area ?? 0.0;
	}

	public double TotalCellArea()
	{
		double cellPolygonAreaSum = 0.0;
		for (int i = 0; i < CellCount; i++)
		{
			cellPolygonAreaSum += CellPolygonArea(i);
		}
		return cellPolygonAreaSum;
	}

	private List<Point> CellPolygonPoints(int cIdx)
	{
		if (cIdx >= Cells.Length)
		{
			throw new ArgumentOutOfRangeException("cIdx");
		}
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		List<Point> points = new List<Point>();
		if (faces.IsEmpty)
		{
			return null;
		}
		if (faces.Length == 1)
		{
			throw new NotSupportedException();
		}
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			if (cIdx == face.cellA)
			{
				if (i == 0)
				{
					points.Add(new Point(FacePoints[face.fpA].Point));
				}
				points.AddRange(FaceInternalPoints[faceIndex].Span);
				points.Add(new Point(FacePoints[face.fpB].Point));
			}
			else
			{
				if (i == 0)
				{
					points.Add(new Point(FacePoints[face.fpB].Point));
				}
				points.AddRangeReverse(FaceInternalPoints[faceIndex].Span);
				points.Add(new Point(FacePoints[face.fpA].Point));
			}
		}
		points.Reverse();
		return points;
	}

	private int GetCellPolygonPointCount(int cIdx)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (faces.IsEmpty)
		{
			return -1;
		}
		int faceInternalPointLengthSum = 0;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			faceInternalPointLengthSum += FaceInternalPoints[faceIndex].Length;
			faceInternalPointLengthSum++;
		}
		return faceInternalPointLengthSum;
	}

	private int TryGetCellPolygonPoints(int cIdx, Span<Point> pointSpan)
	{
		if (cIdx >= Cells.Length)
		{
			throw new ArgumentOutOfRangeException("cIdx");
		}
		int curIdx = 0;
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (faces.IsEmpty)
		{
			return -1;
		}
		for (int lengthOffset = faces.Length - 1; lengthOffset >= 0; lengthOffset--)
		{
			int faceIndex = faces[lengthOffset];
			Face face = Faces[faceIndex];
			int fPNext = face.GetFPNext(cIdx);
			TryAdd(pointSpan, FacePoints[fPNext].Point, ref curIdx);
			Memory<Point> faceInternalPoint = FaceInternalPoints[faceIndex];
			if (!faceInternalPoint.IsEmpty)
			{
				Span<Point> faceInternalPointSpan = faceInternalPoint.Span;
				if (face.cellA == cIdx)
				{
					for (int lengthOffset2 = faceInternalPointSpan.Length - 1; lengthOffset2 >= 0; lengthOffset2--)
					{
						TryAdd(pointSpan, faceInternalPointSpan[lengthOffset2], ref curIdx);
					}
				}
				else
				{
					for (int i = 0; i < faceInternalPointSpan.Length; i++)
					{
						TryAdd(pointSpan, faceInternalPointSpan[i], ref curIdx);
					}
				}
			}
		}
		return curIdx;
		static void TryAdd(Span<Point> buf, Point pt, ref int reference)
		{
			if (reference < buf.Length)
			{
				buf[reference] = pt;
			}
			reference++;
		}
	}

	private bool IsFaceInternalPointsReversed(Face face, int cIdx)
	{
		return face.cellB == cIdx;
	}

	public void FillCellPolygon_CCW(int cIdx, Polygon pg)
	{
		if (cIdx >= Cells.Length)
		{
			throw new ArgumentOutOfRangeException("cIdx");
		}
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (faces.IsEmpty)
		{
			return;
		}
		if (faces.Length == 1)
		{
			int faceIndex = faces[0];
			Face face = Faces[faceIndex];
			pg.Add(new Point(FacePoints[face.fpA].Point));
			pg.AddRange(FaceInternalPoints[faceIndex].Span);
			pg.Add(new Point(FacePoints[face.fpB].Point));
			pg.AddRangeReverse(FaceInternalPoints[faceIndex].Span);
			return;
		}
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex2 = faces[i];
			Face face2 = Faces[faceIndex2];
			if (cIdx == face2.cellA)
			{
				if (i == 0)
				{
					pg.Add(new Point(FacePoints[face2.fpA].Point));
				}
				pg.AddRange(FaceInternalPoints[faceIndex2].Span);
				pg.Add(new Point(FacePoints[face2.fpB].Point));
			}
			else
			{
				if (i == 0)
				{
					pg.Add(new Point(FacePoints[face2.fpB].Point));
				}
				pg.AddRangeReverse(FaceInternalPoints[faceIndex2].Span);
				pg.Add(new Point(FacePoints[face2.fpA].Point));
			}
		}
	}

	public Point[] CellNodePoints(int c)
	{
		Point[] cellCornerPoints = CellCornerPoints(c, circleBackToFirstPoint: false);
		cellCornerPoints.AsSpan().Reverse();
		return cellCornerPoints;
	}

	public Point[] CellCornerPoints(int cIdx, bool circleBackToFirstPoint = true)
	{
		Cell cell = Cells[cIdx];
		int length = cell.Faces.Span.Length;
		Point[] points = ((!circleBackToFirstPoint) ? new Point[length] : new Point[length + 1]);
		CellCornerPoints(cIdx, points, circleBackToFirstPoint);
		return points;
	}

	public void CellCornerPoints(int cIdx, Span<Point> cellPts, bool circleBackToFirstPoint = true)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		for (int i = 0; i < length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			int fPPrev = face.GetFPPrev(cIdx);
			Point point = FacePoints[fPPrev].Point;
			cellPts[i] = point;
		}
		if (circleBackToFirstPoint)
		{
			cellPts[length] = cellPts[0];
		}
	}

	public int FaceCCWOfCell(int fpIdx, int cIdx)
	{
		foreach (int face2 in FacePoints[fpIdx].Faces)
		{
			Face face = Faces[face2];
			if (face.fpA == fpIdx)
			{
				if (face.cellB == cIdx)
				{
					return face2;
				}
			}
			else if (face.fpB == fpIdx && face.cellA == cIdx)
			{
				return face2;
			}
		}
		return -1;
	}

	public List<int> GetCellsAroundFacepoint(int fp)
	{
		List<int> cellsAroundFacepoint = new List<int>();
		GetCellsAroundFacepoint(fp, cellsAroundFacepoint);
		return cellsAroundFacepoint;
	}

	public void GetCellsAroundFacepoint(int fp, List<int> into)
	{
		into.Clear();
		IList<int> faces = FacePoints[fp].Faces;
		foreach (int faceIndex in faces)
		{
			if (Faces[faceIndex].fpB == fp)
			{
				into.Add(Faces[faceIndex].cellA);
			}
			else
			{
				into.Add(Faces[faceIndex].cellB);
			}
		}
		if (FacePointIsPerimeter(fp))
		{
			into.Add(Faces[faces.Last()].cellB);
		}
	}

	public List<int> GetCellsAroundCell(int cIdx)
	{
		Cell cell = Cells[cIdx];
		List<int> cellsAroundCell = new List<int>(cell.Faces.Length);
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			if (face.cellA != cIdx)
			{
				cellsAroundCell.Add(face.cellA);
			}
			else if (face.cellB != -1)
			{
				cellsAroundCell.Add(face.cellB);
			}
		}
		return cellsAroundCell;
	}

	public List<int> GetCellsAroundCellWithBoundaries(int cIdx)
	{
		Cell cell = Cells[cIdx];
		List<int> cellsAroundCellWithBoundaries = new List<int>(cell.Faces.Length);
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			if (face.cellA != cIdx)
			{
				cellsAroundCellWithBoundaries.Add(face.cellA);
			}
			else
			{
				cellsAroundCellWithBoundaries.Add(face.cellB);
			}
		}
		return cellsAroundCellWithBoundaries;
	}

	public void GetCellsAroundCell(int cIdx, Span<int> output)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			if (face.cellA != cIdx)
			{
				output[i] = face.cellA;
			}
			else
			{
				output[i] = face.cellB;
			}
		}
	}

	public List<int> GetFacesAroundFace(int fIdx)
	{
		Face face = Faces[fIdx];
		List<int> facesAroundFace = new List<int>();
		FacePoint facePoint = FacePoints[face.fpA];
		FacePoint facePoint2 = FacePoints[face.fpB];
		foreach (int face2 in facePoint.Faces)
		{
			if (fIdx != face2)
			{
				facesAroundFace.Add(face2);
			}
		}
		foreach (int face3 in facePoint2.Faces)
		{
			if (fIdx != face3)
			{
				facesAroundFace.Add(face3);
			}
		}
		facesAroundFace.Sort();
		return facesAroundFace;
	}

	public int GetFaceBetweenCells(int cA, int cB)
	{
		if (cA == cB)
		{
			return -1;
		}
		Cell cell = Cells[cA];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			if (face.cellA == cB || face.cellB == cB)
			{
				return faceIndex;
			}
		}
		return -2;
	}

	public List<int> GetFacePointsAroundFacePoint(int fpIdx)
	{
		FacePoint facePoint = FacePoints[fpIdx];
		List<int> facePointsAroundFacePoint = new List<int>();
		foreach (int face2 in facePoint.Faces)
		{
			Face face = Faces[face2];
			if (face.fpA != fpIdx)
			{
				facePointsAroundFacePoint.Add(face.fpA);
			}
			if (face.fpB != fpIdx)
			{
				facePointsAroundFacePoint.Add(face.fpB);
			}
		}
		facePointsAroundFacePoint.Sort();
		return facePointsAroundFacePoint;
	}

	public bool FacePointIsPerimeter(int fp)
	{
		FacePoint facePoint = FacePoints[fp];
		if (facePoint.Faces.IsNullOrEmpty())
		{
			return false;
		}
		foreach (int face in facePoint.Faces)
		{
			int cellB = Faces[face].cellB;
			if (cellB == -1)
			{
				return true;
			}
			if (CellIsVirtual(cellB))
			{
				return true;
			}
		}
		return false;
	}

	public List<int> CellFacePoints(int cIdx)
	{
		List<int> intList = new List<int>();
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			Face face = Faces[faces[i]];
			intList.Add(face.GetFPPrev(cIdx));
		}
		return intList;
	}

	public void CellFacePoints(int cIdx, Span<int> fpOut)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			Face face = Faces[faces[i]];
			fpOut[i] = face.GetFPPrev(cIdx);
		}
	}

	public bool FaceIsPerimeter(int f)
	{
		int cellB = Faces[f].cellB;
		return cellB switch
		{
			-1 => true, 
			int.MinValue => false, 
			_ => Cells[cellB].Faces.Length == 1, 
		};
	}

	public Segment FaceSegment(int fIdx)
	{
		Face face = Faces[fIdx];
		return new Segment(FacePoints[face.fpA].Point, FacePoints[face.fpB].Point);
	}

	public Point FaceMidPoint(int fIdx)
	{
		Face face = Faces[fIdx];
		return Segment.Midpoint(FacePoints[face.fpA].Point, FacePoints[face.fpB].Point);
	}

	public Point FaceSegmentMidpoint(int fIdx)
	{
		Face face = Faces[fIdx];
		return Segment.Midpoint(FacePoints[face.fpA].Point, FacePoints[face.fpB].Point);
	}

	public double FaceSimpleLength(int f)
	{
		Face face = Faces[f];
		return FacePoints[face.fpA].Point.DistanceTo(FacePoints[face.fpB].Point);
	}

	public double FaceSimpleLengthSquared(int f)
	{
		Face face = Faces[f];
		return FacePoints[face.fpA].Point.DistanceSquaredTo(FacePoints[face.fpB].Point);
	}

	public double FacePolylineLength(int f)
	{
		Face face = Faces[f];
		Point point = FacePoints[face.fpA].Point;
		Point point2 = FacePoints[face.fpB].Point;
		Span<Point> faceInternalPoint = ((FaceInternalPoints != null) ? FaceInternalPoints[f].Span : default(Span<Point>));
		if (faceInternalPoint.IsEmpty)
		{
			return point.DistanceTo(point2);
		}
		double distanceToSum = 0.0;
		for (int i = 0; i < faceInternalPoint.Length; i++)
		{
			Point point3 = faceInternalPoint[i];
			distanceToSum += point.DistanceTo(point3);
			point = point3;
		}
		return distanceToSum + point.DistanceTo(point2);
	}

	public IEnumerable<Segment> FaceSegments(int f)
	{
		Face face = Faces[f];
		Segment segment = default;
		segment.A = FacePoints[face.fpA].Point;
		Memory<Point> intPoints = FaceInternalPoints[f];
		if (intPoints.IsEmpty)
		{
			segment.B = FacePoints[face.fpB].Point;
			yield return segment;
			yield break;
		}
		segment.B = intPoints.Span[0];
		yield return segment;
		for (int i = 0; i < intPoints.Length - 1; i++)
		{
			segment.A = intPoints.Span[i];
			segment.B = intPoints.Span[i + 1];
			yield return segment;
		}
		Span<Point> intPointsSpan = intPoints.Span;
		segment.A = intPointsSpan[intPointsSpan.Length - 1];
		segment.B = FacePoints[face.fpB].Point;
		yield return segment;
	}

	public void AppendFaceSegments(int f, List<Segment> into)
	{
		Face face = Faces[f];
		Segment segment = default;
		segment.A = FacePoints[face.fpA].Point;
		Memory<Point> faceInternalPoint = FaceInternalPoints[f];
		if (faceInternalPoint.IsEmpty)
		{
			segment.B = FacePoints[face.fpB].Point;
			into.Add(segment);
			return;
		}
		Span<Point> faceInternalPointSpan = faceInternalPoint.Span;
		into.EnsureCapacity(into.Count + faceInternalPointSpan.Length + 1);
		segment.B = faceInternalPointSpan[0];
		into.Add(segment);
		for (int i = 0; i < faceInternalPointSpan.Length - 1; i++)
		{
			segment.A = faceInternalPointSpan[i];
			segment.B = faceInternalPointSpan[i + 1];
			into.Add(segment);
		}
		segment.A = faceInternalPointSpan[faceInternalPointSpan.Length - 1];
		segment.B = FacePoints[face.fpB].Point;
		into.Add(segment);
	}

	public Polyline FacePolyline(int f)
	{
		Span<Point> faceInternalPoint = FaceInternalPoints[f].Span;
		Face face = Faces[f];
		Polyline polyline = new Polyline(2 + faceInternalPoint.Length);
		polyline.Add(FacePoints[face.fpA].Point);
		polyline.AddRange(faceInternalPoint);
		polyline.Add(FacePoints[face.fpB].Point);
		return polyline;
	}

	public void FacePolylineInto(int f, Polyline pline)
	{
		Span<Point> faceInternalPoint = FaceInternalPoints[f].Span;
		Face face = Faces[f];
		pline.Add(FacePoints[face.fpA].Point);
		pline.AddRange(faceInternalPoint);
		pline.Add(FacePoints[face.fpB].Point);
	}

	public double DistanceToFace(int f, Point pt)
	{
		Face face = Faces[f];
		Memory<Point> faceInternalPoint = FaceInternalPoints[f];
		if (faceInternalPoint.Length == 0)
		{
			return new Segment(FacePoints[face.fpA].Point, FacePoints[face.fpB].Point).DistanceToPoint(pt);
		}
		return FacePolyline(f).DistanceToPoint(pt);
	}

	public int FacePolylinePointCount(int f)
	{
		return FaceInternalPoints[f].Length + 2;
	}

	public List<(int, double)> NodesAlongPolyline(Polyline pline, bool forceToPolylineEndpoints)
	{
		List<(int, bool)> facesAlongPolyline = FacesAlongPolyline(pline);
		List<(int, double)> countOffset = new List<(int, double)>(facesAlongPolyline.Count + 1);
		if (facesAlongPolyline.Count == 0)
		{
			return countOffset;
		}
		double[] cumulativeSegmentLengths = pline.CumulativeSegmentLengths();
		(int, bool) tuple = facesAlongPolyline[0];
		Face face = Faces[tuple.Item1];
		int fpB = ((!tuple.Item2) ? face.fpB : face.fpA);
		if (forceToPolylineEndpoints)
		{
			countOffset.Add((fpB, 0.0));
		}
		else
		{
			Point point = FacePoints[fpB].Point;
			PointOnPolyline ptOnPoly = pline.NearestPointOnPolyline(point);
			countOffset.Add((fpB, pline.DistanceAlongFast(ptOnPoly, ref cumulativeSegmentLengths)));
		}
		for (int i = 0; i < facesAlongPolyline.Count; i++)
		{
			(int, bool) tuple2 = facesAlongPolyline[i];
			Face face2 = Faces[tuple2.Item1];
			int fpA = ((!tuple2.Item2) ? face2.fpA : face2.fpB);
			Point point2 = FacePoints[fpA].Point;
			PointOnPolyline ptOnPoly2 = pline.NearestPointOnPolyline(point2);
			countOffset.Add((fpA, pline.DistanceAlongFast(ptOnPoly2, ref cumulativeSegmentLengths)));
		}
		if (forceToPolylineEndpoints)
		{
			(int, double) tuple3 = countOffset[countOffset.Count - 1];
			countOffset[countOffset.Count - 1] = (tuple3.Item1, 1.0);
		}
		return countOffset;
	}

	public List<(int, bool)> FacesAlongPolyline(Polyline pline)
	{
		List<(int, bool)> valueTuples = new List<(int, bool)>();
		if (pline.PointCount == 0)
		{
			return valueTuples;
		}
		Point startPt = pline.First();
		Point endPt = pline.Last();
		SpatialIndex<int> facePointSpatialIndex = GetFacePointSpatialIndex();
		facePointSpatialIndex.TryGetNearestElement(startPt, (Point _, int ii) => startPt.DistanceTo(FacePoints[ii].Point), out var nearestElement, out var _);
		facePointSpatialIndex.TryGetNearestElement(endPt, (Point _, int ii) => endPt.DistanceTo(FacePoints[ii].Point), out var nearestElement2, out var _);
		int nearestElement3 = nearestElement;
		PointOnPolyline pointOnPolyline = default;
		do
		{
			int otherFacepoint = 0;
			int faceIndex = 0;
			PointOnPolyline pointOnPolyline2 = default;
			Face face = Face.NullFace;
			double maxValue = double.MaxValue;
			FacePoint facePoint = FacePoints[nearestElement3];
			for (int intervalIndex = 0; intervalIndex < facePoint.Faces.Count; intervalIndex++)
			{
				int faceIndex2 = facePoint.Faces[intervalIndex];
				Face face2 = Faces[faceIndex2];
				int otherFacepoint2 = face2.OtherFacepoint(nearestElement3);
				Point point = FacePoints[otherFacepoint2].Point;
				PointOnPolyline pointOnPolyline3 = pline.NearestPointOnPolyline(point);
				if (pointOnPolyline3 > pointOnPolyline)
				{
					double distanceTo = pointOnPolyline3.Point.DistanceTo(point);
					if (distanceTo < maxValue)
					{
						maxValue = distanceTo;
						pointOnPolyline2 = pointOnPolyline3;
						otherFacepoint = otherFacepoint2;
						faceIndex = faceIndex2;
						face = face2;
					}
				}
			}
			if (maxValue == double.MaxValue)
			{
				break;
			}
			bool fpAFaceTerm = face.fpA == nearestElement3;
			valueTuples.Add((faceIndex, fpAFaceTerm));
			nearestElement3 = otherFacepoint;
			pointOnPolyline = pointOnPolyline2;
		}
		while (nearestElement3 != nearestElement2);
		return valueTuples;
	}

	public List<(int, bool)> FacesAlongPolyline_Clipped(Polyline pline)
	{
		List<(int, bool)> valueTuples = new List<(int, bool)>();
		if (Perimeter == null || !Perimeter.ContainsOrIntersects(pline))
		{
			return valueTuples;
		}
		List<Polyline> clipPolyline = Perimeter.ClipPolyline(pline);
		if (clipPolyline.IsNullOrEmpty())
		{
			return null;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (Polyline clipPolyline2 in clipPolyline)
		{
			foreach (var facesAlongPolyline in FacesAlongPolyline(clipPolyline2))
			{
				if (!intSet.Contains(facesAlongPolyline.Item1))
				{
					valueTuples.Add(facesAlongPolyline);
					intSet.Add(facesAlongPolyline.Item1);
				}
			}
		}
		return valueTuples;
	}

	public List<ProfileFaceRange> FacesAlongPolylineWithStations(Polyline pline)
	{
		List<(int, bool)> facesAlongPolyline = FacesAlongPolyline(pline);
		if (facesAlongPolyline.Count == 0)
		{
			return new List<ProfileFaceRange>();
		}
		List<ProfileFaceRange> profileFaceRanges = new List<ProfileFaceRange>(facesAlongPolyline.Count);
		double[] cumulativeSegmentLengths = pline.CumulativeSegmentLengths();
		foreach (var facesAlongPolyline2 in facesAlongPolyline)
		{
			int facesAlongPolyline2Term = facesAlongPolyline2.Item1;
			bool facesAlongPolyline2Term2 = facesAlongPolyline2.Item2;
			Face face = Faces[facesAlongPolyline2Term];
			int fpA = (facesAlongPolyline2Term2 ? face.fpA : face.fpB);
			int fpB = (facesAlongPolyline2Term2 ? face.fpB : face.fpA);
			Point point = FacePoints[fpA].Point;
			Point point2 = FacePoints[fpB].Point;
			PointOnPolyline ptOnPoly = pline.NearestPointOnPolyline(point);
			PointOnPolyline ptOnPoly2 = pline.NearestPointOnPolyline(point2);
			double sStart = pline.DistanceAlongFast(ptOnPoly, ref cumulativeSegmentLengths);
			double sEnd = pline.DistanceAlongFast(ptOnPoly2, ref cumulativeSegmentLengths);
			profileFaceRanges.Add(new ProfileFaceRange(facesAlongPolyline2Term, fpA, fpB, sStart, sEnd));
		}
		return profileFaceRanges;
	}

	public List<ProfileFaceRange> PerimeterFacesAlongPolyline(Polyline pline, out Polyline magnetizedPline)
	{
		int nearestPerimeterFace = NearestPerimeterFace(pline.First(), out var ptOnPoly);
		int nearestPerimeterFace2 = NearestPerimeterFace(pline.MidPointOnPolyline().Point, out var ptOnPoly2);
		int nearestPerimeterFace3 = NearestPerimeterFace(pline.Last(), out var ptOnPoly3);
		bool notIsPointBetweenCCW = !Perimeter.IsPointBetweenCCW(ptOnPoly, ptOnPoly2, ptOnPoly3);
		if (notIsPointBetweenCCW)
		{
			magnetizedPline = Perimeter.Subsection(ptOnPoly3, ptOnPoly);
			magnetizedPline.Reverse();
		}
		else
		{
			magnetizedPline = Perimeter.Subsection(ptOnPoly, ptOnPoly3);
		}
		List<ProfileFaceRange> profileFaceRanges = new List<ProfileFaceRange>();
		Face face = Faces[nearestPerimeterFace];
		int intervalIndex;
		int fpStart;
		int intervalIndex2;
		if (notIsPointBetweenCCW)
		{
			fpStart = face.fpB;
			intervalIndex2 = face.fpA;
			intervalIndex = FacePoints[intervalIndex2].Faces.Last();
		}
		else
		{
			fpStart = face.fpA;
			intervalIndex2 = face.fpB;
			intervalIndex = FacePoints[intervalIndex2].Faces.First();
		}
		if (nearestPerimeterFace == nearestPerimeterFace3 && nearestPerimeterFace == nearestPerimeterFace2)
		{
			Polyline polyline = FacePolyline(nearestPerimeterFace);
			if (notIsPointBetweenCCW)
			{
				polyline.Reverse();
			}
			PointOnPolyline ptOnPoly4 = polyline.NearestPointOnPolyline(magnetizedPline.First());
			double sStart = -1.0 * polyline.DistanceAlong(ptOnPoly4);
			double length = polyline.Length;
			profileFaceRanges.Add(new ProfileFaceRange(nearestPerimeterFace, fpStart, intervalIndex2, sStart, length));
			return profileFaceRanges;
		}
		PointOnPolyline ptOnPoly5 = magnetizedPline.NearestPointOnPolyline(FacePoints[intervalIndex2].Point);
		double distanceAlong = magnetizedPline.DistanceAlong(ptOnPoly5);
		double sStart2 = distanceAlong - FacePolylineLength(nearestPerimeterFace);
		profileFaceRanges.Add(new ProfileFaceRange(nearestPerimeterFace, fpStart, intervalIndex2, sStart2, distanceAlong));
		int ordinal = 0;
		while (intervalIndex != nearestPerimeterFace3)
		{
			Face face2 = Faces[intervalIndex];
			int unity = -1;
			if (notIsPointBetweenCCW)
			{
				fpStart = face2.fpB;
				intervalIndex2 = face2.fpA;
				unity = FacePoints[intervalIndex2].Faces.Last();
			}
			else
			{
				fpStart = face2.fpA;
				intervalIndex2 = face2.fpB;
				unity = FacePoints[intervalIndex2].Faces.First();
			}
			PointOnPolyline ptOnPoly6 = magnetizedPline.NearestPointOnPolyline(FacePoints[fpStart].Point);
			PointOnPolyline ptOnPoly7 = magnetizedPline.NearestPointOnPolyline(FacePoints[intervalIndex2].Point);
			profileFaceRanges.Add(new ProfileFaceRange(intervalIndex, fpStart, intervalIndex2, magnetizedPline.DistanceAlong(ptOnPoly6), magnetizedPline.DistanceAlong(ptOnPoly7)));
			intervalIndex = unity;
			ordinal++;
			if (ordinal > FaceCount)
			{
				return null;
			}
		}
		Face face3 = Faces[nearestPerimeterFace3];
		int fpBFace3Term;
		int fpEnd;
		if (notIsPointBetweenCCW)
		{
			fpBFace3Term = face3.fpB;
			fpEnd = face3.fpA;
		}
		else
		{
			fpBFace3Term = face3.fpA;
			fpEnd = face3.fpB;
		}
		PointOnPolyline ptOnPoly8 = magnetizedPline.NearestPointOnPolyline(FacePoints[fpBFace3Term].Point);
		double distanceAlong2 = magnetizedPline.DistanceAlong(ptOnPoly8);
		double sEnd = FacePolylineLength(nearestPerimeterFace3) + distanceAlong2;
		profileFaceRanges.Add(new ProfileFaceRange(nearestPerimeterFace3, fpBFace3Term, fpEnd, distanceAlong2, sEnd));
		return profileFaceRanges;
	}

	private int NearestPerimeterFace(Point pt, out PointOnPolyline ptOnPoly)
	{
		ptOnPoly = Perimeter.NearestPointOnPolyline(pt);
		if (!MetadataCache.GetFaceSpatialIndex().TryGetNearestElement(pt, (Point coord, int idx) =>
		{
			if (!FaceIsPerimeter(idx))
			{
				return double.MaxValue;
			}
			FacePolyline(idx).NearestPointOnPolyline(coord, out var actualDistance2);
			return actualDistance2;
		}, out var nearestElement, out var _))
		{
			return -1;
		}
		double eps = 0.0001;
		double truncatedDistance = TruncatedDistance(pt, FacePolyline(nearestElement));
		foreach (int facesAroundFace in GetFacesAroundFace(nearestElement))
		{
			if (FaceIsPerimeter(facesAroundFace) && TruncatedDistance(pt, FacePolyline(facesAroundFace)) < truncatedDistance)
			{
				nearestElement = facesAroundFace;
			}
		}
		return nearestElement;
		double TruncatedDistance(Point point, Polyline pline)
		{
			pline.Subsection(eps, pline.Length - eps).NearestPointOnPolyline(pt, out var actualDistance2);
			return actualDistance2;
		}
	}

	public ComplexPolygon ComputeComplexPerimeter()
	{
		HashSet<int> intSet = new HashSet<int>();
		for (int i = 0; i < FaceCount; i++)
		{
			if (Faces[i].cellB == -1)
			{
				intSet.Add(i);
			}
		}
		if (intSet.Count == 0)
		{
			return null;
		}
		List<Polygon> polygons = new List<Polygon>();
		while (intSet.Count > 0)
		{
			int ordinal = intSet.First();
			List<int> intList = new List<int>();
			int ordinal2 = ordinal;
			intList.Add(ordinal2);
			intSet.Remove(ordinal2);
			for (int j = 0; j < FaceCount; j++)
			{
				int fpB = Faces[ordinal2].fpB;
				FacePoint facePoint = FacePoints[fpB];
				int unity = -1;
				int cwFaceByGlobalIndex = facePoint.CWFaceByGlobalIndex(ordinal2);
				Face face = Faces[cwFaceByGlobalIndex];
				if (face.cellB == -1 && face.fpA == fpB)
				{
					unity = cwFaceByGlobalIndex;
				}
				else
				{
					int ccwFaceByGlobalIndex = facePoint.CCWFaceByGlobalIndex(ordinal2);
					Face face2 = Faces[ccwFaceByGlobalIndex];
					if (face2.cellB == -1 && face2.fpA == fpB)
					{
						unity = ccwFaceByGlobalIndex;
					}
				}
				if (unity == -1)
				{
					throw new Exception("Could not walk perimeter at facepoint " + fpB + ", likely mesh-gen issue.");
				}
				if (unity == ordinal)
				{
					break;
				}
				ordinal2 = unity;
				intList.Add(ordinal2);
				intSet.Remove(ordinal2);
			}
			Polygon polygon = new Polygon(intList.Count);
			for (int k = 0; k < intList.Count; k++)
			{
				int ordinal3 = intList[k];
				Face face3 = Faces[ordinal3];
				polygon.Add(FacePoints[face3.fpA].Point);
				Memory<Point> faceInternalPoint = FaceInternalPoints[ordinal3];
				if (!faceInternalPoint.IsEmpty)
				{
					polygon.AddRange(faceInternalPoint.Span);
				}
			}
			polygon.Reverse();
			polygons.Add(polygon);
		}
		if (polygons.Count == 1)
		{
			return ComplexPolygon.FromOuterRing(polygons[0]);
		}
		OperationResult operationResult = ComplexPolygon.TryCreateComplexPolygon(polygons, out var poly, checkIntersections: false);
		if (!operationResult)
		{
			throw new StructuredException(operationResult);
		}
		return poly;
	}

	public Polygon GetSingleRingPerimeter()
	{
		List<int> singleRingPerimeterFaces = GetSingleRingPerimeterFaces();
		Polygon polygon = new Polygon(singleRingPerimeterFaces.Count);
		for (int i = 0; i < singleRingPerimeterFaces.Count; i++)
		{
			int singleRingPerimeterFaceIndex = singleRingPerimeterFaces[i];
			Face face = Faces[singleRingPerimeterFaceIndex];
			polygon.Add(FacePoints[face.fpA].Point);
			Memory<Point> faceInternalPoint = FaceInternalPoints[singleRingPerimeterFaceIndex];
			if (!faceInternalPoint.IsEmpty)
			{
				polygon.AddRange(faceInternalPoint.Span);
			}
		}
		return polygon;
	}

	public List<int> GetSingleRingPerimeterFaces()
	{
		int ordinal = -1;
		for (int i = 0; i < FaceCount; i++)
		{
			if (FaceIsPerimeter(i))
			{
				ordinal = i;
				break;
			}
		}
		List<int> singleRingPerimeterFaces = new List<int>();
		int facesAroundFace2Term = ordinal;
		singleRingPerimeterFaces.Add(facesAroundFace2Term);
		for (int j = 0; j < FaceCount; j++)
		{
			Face face = Faces[facesAroundFace2Term];
			List<int> facesAroundFace = GetFacesAroundFace(facesAroundFace2Term);
			bool condition = false;
			foreach (int facesAroundFace2 in facesAroundFace)
			{
				Face face2 = Faces[facesAroundFace2];
				if (face2.cellB == -1 && face2.fpA == face.fpB)
				{
					facesAroundFace2Term = facesAroundFace2;
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				throw new Exception("Could not walk perimeter hull, likely mesh-gen issue.");
			}
			if (facesAroundFace2Term == ordinal)
			{
				break;
			}
			singleRingPerimeterFaces.Add(facesAroundFace2Term);
		}
		return singleRingPerimeterFaces;
	}

	public List<ProfileCellRange> CellsUnderPolyline(Polyline polyline, bool mergeSplitSegments = true)
	{
		List<ProfileCellRange> profileCellRanges = new List<ProfileCellRange>();
		double segmentLengthSum = 0.0;
		for (int i = 0; i < polyline.SegmentCount; i++)
		{
			Segment segment = polyline.Segments[i];
			foreach (ProfileCellRange cellsUnderSegment in CellsUnderSegment(segment))
			{
				ProfileCellRange current = cellsUnderSegment;
				current.StationStart += segmentLengthSum;
				current.StationEnd += segmentLengthSum;
				profileCellRanges.Add(current);
			}
			segmentLengthSum += segment.Length;
		}
		profileCellRanges = profileCellRanges.OrderBy((ProfileCellRange pcr) => pcr.StationStart).ToList();
		if (mergeSplitSegments)
		{
			List<ProfileCellRange> profileCellRangesList = new List<ProfileCellRange>();
			double naN = double.NaN;
			int cellProfileCellRangeTerm = -1;
			for (int intervalIndex = 0; intervalIndex <= profileCellRanges.Count - 1; intervalIndex++)
			{
				ProfileCellRange profileCellRange = profileCellRanges[intervalIndex];
				if (profileCellRange.Cell == cellProfileCellRangeTerm)
				{
					if (double.IsNaN(naN))
					{
						naN = profileCellRanges[intervalIndex - 1].StationStart;
					}
					continue;
				}
				if (!double.IsNaN(naN))
				{
					double stationEnd = profileCellRanges[intervalIndex - 1].StationEnd;
					profileCellRangesList[profileCellRangesList.Count - 1] = new ProfileCellRange(cellProfileCellRangeTerm, naN, stationEnd);
					naN = double.NaN;
				}
				profileCellRangesList.Add(profileCellRange);
				cellProfileCellRangeTerm = profileCellRange.Cell;
			}
			if (!double.IsNaN(naN))
			{
				profileCellRangesList[profileCellRangesList.Count - 1] = new ProfileCellRange(cellProfileCellRangeTerm, naN, profileCellRanges.Last().StationEnd);
			}
			profileCellRanges = profileCellRangesList;
		}
		return profileCellRanges;
	}

	public int GetCell(Point pt)
	{
		foreach (int element in MetadataCache.GetCellSpatialIndex().GetElements(pt.X, pt.Y))
		{
			if (CellPolygon(element).Contains(pt))
			{
				return element;
			}
		}
		return -1;
	}

	public List<int> GetCells(Polygon polygon)
	{
		List<int> cells = new List<int>();
		for (int i = 0; i < CellCount; i++)
		{
			if (polygon.Contains(CellCenters[i]))
			{
				cells.Add(i);
			}
		}
		return cells;
	}

	public List<ProfileCellRange> CellsUnderSegment(Segment segment)
	{
		List<int> elements = MetadataCache.GetCellSpatialIndex().GetElements(segment);
		List<ProfileCellRange> profileCellRanges = new List<ProfileCellRange>();
		foreach (int element in elements)
		{
			Polygon polygon = CellPolygon(element);
			bool polygonSegmentTerm = polygon.Contains(segment.A);
			bool polygonSegmentTerm2 = polygon.Contains(segment.B);
			if (polygonSegmentTerm & polygonSegmentTerm2)
			{
				profileCellRanges.Add(new ProfileCellRange(element, 0.0, segment.Length));
				continue;
			}
			List<LineIntersection> allIntersections = polygon.GetAllIntersections(segment).ToList();
			if (allIntersections != null && allIntersections.Count > 0)
			{
				double fraction = allIntersections.First().BIntersection.Fraction;
				double fraction2 = allIntersections.First().BIntersection.Fraction;
				for (int i = 1; i < allIntersections.Count; i++)
				{
					fraction = Math.Min(fraction, allIntersections[i].BIntersection.Fraction);
					fraction2 = Math.Max(fraction2, allIntersections[i].BIntersection.Fraction);
				}
				double length = segment.Length;
				if (polygonSegmentTerm)
				{
					profileCellRanges.Add(new ProfileCellRange(element, 0.0, length * fraction2));
				}
				else if (polygonSegmentTerm2)
				{
					profileCellRanges.Add(new ProfileCellRange(element, length * fraction, length));
				}
				else
				{
					profileCellRanges.Add(new ProfileCellRange(element, length * fraction, length * fraction2));
				}
			}
		}
		return profileCellRanges.OrderBy((ProfileCellRange pcr) => pcr.StationStart).ToList();
	}

	public List<(int, int)> ShortestPathBetweenCells(int fromCell, int toCell)
	{
		throw new NotImplementedException();
	}

	public void Floodfill(int seedCell, Func<int, bool> canCrossFace, Action<int> onCellVisit, bool includeSeedCell = false)
	{
		BitArray bitArray = new BitArray(CellCount);
		if (includeSeedCell)
		{
			onCellVisit(seedCell);
		}
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		intListList.Add(seedCell);
		while (intListList.Count > 0)
		{
			for (int i = 0; i < intListList.Count; i++)
			{
				int ordinal = intListList[i];
				Cell cell = Cells[ordinal];
				Span<int> faces = cell.Faces.Span;
				for (int j = 0; j < faces.Length; j++)
				{
					int faceIndex = faces[j];
					Face face = Faces[faceIndex];
					int otherCell = face.OtherCell(ordinal);
					if (otherCell != -1 && !bitArray[otherCell] && canCrossFace(faceIndex))
					{
						onCellVisit(otherCell);
						bitArray[otherCell] = true;
						intList.Add(otherCell);
					}
				}
			}
			intListList.Clear();
			List<int> intList2 = intListList;
			intListList = intList;
			intList = intList2;
		}
	}

	public HashSet<int> GetUniqueCellsAroundFaces(List<int> faces)
	{
		if (faces == null)
		{
			return null;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (int face2 in faces)
		{
			Face face = Faces[face2];
			intSet.Add(face.cellA);
			intSet.Add(face.cellB);
		}
		return intSet;
	}

	public List<int> GetUniqueFacepointsAroundFaces(List<int> faces)
	{
		if (faces == null)
		{
			return null;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (int face2 in faces)
		{
			Face face = Faces[face2];
			intSet.Add(face.fpA);
			intSet.Add(face.fpB);
		}
		return intSet.ToList();
	}

	private static Extent ComputeExtent(Mesh m)
	{
		return m.ComputeExtent();
	}

	private Extent ComputeExtent()
	{
		if (FacePointCount == 0)
		{
			return Extent.NullExtent;
		}
		double minX = double.MaxValue;
		double maxX = double.MinValue;
		double minY = double.MaxValue;
		double maxY = double.MinValue;
		for (int i = 0; i < FacePointCount; i++)
		{
			Point point = FacePoints[i].Point;
			minX = Math.Min(minX, point.X);
			maxX = Math.Max(maxX, point.X);
			minY = Math.Min(minY, point.Y);
			maxY = Math.Max(maxY, point.Y);
		}
		for (int j = 0; j < FaceCount; j++)
		{
			PointCollection.BuildExtent(FaceInternalPoints[j].Span, ref minX, ref maxX, ref minY, ref maxY);
		}
		return new Extent(minX, maxX, minY, maxY);
	}

	public Mesh ReindexMesh(int quantumSize = 16)
	{
		if (CellCount <= quantumSize)
		{
			return this;
		}
		Extent extent = Extent;
		extent = ((!(extent.Width < extent.Height)) ? new Extent(extent.UpperLeft, extent.Width, extent.Width) : new Extent(extent.UpperLeft, extent.Height, extent.Height));
		double x = Math.Sqrt((double)CellCount / (double)quantumSize);
		int ordinal = (int)Math.Pow(2.0, Math.Ceiling(Math.Log2(x)));
		RasterDefinition rasterDefinition = new RasterDefinition(extent, ordinal, ordinal);
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(rasterDefinition);
		for (int i = 0; i < CellCount; i++)
		{
			spatialIndex.Add(CellCenters[i], i);
		}
		int[] repeat = Enumerable.Repeat(-1, FaceCount).ToArray();
		int[] repeatArray = Enumerable.Repeat(-1, FacePointCount).ToArray();
		Mesh mesh = new Mesh();
		mesh.Cells = new Cell[CellCount];
		mesh.Faces = new Face[FaceCount];
		mesh.FacePoints = new FacePoint[FacePointCount];
		mesh.Perimeter = Perimeter.Clone();
		mesh.CellCenters = new Point[CellCount];
		mesh.FaceInternalPoints = new Memory<Point>[FaceCount];
		int ordinal2 = 0;
		int faces = 0;
		int ordinal3 = 0;
		for (int j = 0; j < rasterDefinition.CellCount; j++)
		{
			int invertZOrderIndex = rasterDefinition.InvertZOrderIndex(j);
			foreach (int bin in spatialIndex.Bins[invertZOrderIndex])
			{
				Cell cell = Cells[bin].Clone();
				Span<int> facesSpan = cell.Faces.Span;
				mesh.Cells[ordinal2] = cell;
				mesh.CellCenters[ordinal2] = CellCenters[bin];
				for (int k = 0; k < facesSpan.Length; k++)
				{
					int faceIndex = facesSpan[k];
					int repeat2 = repeat[faceIndex];
					if (repeat2 == -1)
					{
						Face face = Faces[faceIndex].Clone();
						if (face.cellA == bin)
						{
							face.cellA = ordinal2;
						}
						else
						{
							face.cellB = ordinal2;
						}
						int repeat3 = repeatArray[face.fpA];
						FacePoint facePoint;
						if (repeat3 == -1)
						{
							facePoint = FacePoints[face.fpA].Clone();
							mesh.FacePoints[ordinal3] = facePoint;
							repeat3 = ordinal3;
							repeatArray[face.fpA] = repeat3;
							ordinal3++;
						}
						else
						{
							facePoint = mesh.FacePoints[repeatArray[face.fpA]];
						}
						int repeat4 = repeatArray[face.fpB];
						FacePoint facePoint2;
						if (repeatArray[face.fpB] == -1)
						{
							facePoint2 = FacePoints[face.fpB].Clone();
							mesh.FacePoints[ordinal3] = facePoint2;
							repeat4 = ordinal3;
							repeatArray[face.fpB] = repeat4;
							ordinal3++;
						}
						else
						{
							facePoint2 = mesh.FacePoints[repeatArray[face.fpB]];
						}
						facePoint.Faces[facePoint.Faces.IndexOf(faceIndex)] = faces;
						facePoint2.Faces[facePoint2.Faces.IndexOf(faceIndex)] = faces;
						face.fpA = repeat3;
						face.fpB = repeat4;
						mesh.Faces[faces] = face;
						mesh.FaceInternalPoints[faces] = FaceInternalPoints[faceIndex].ToArray();
						repeat2 = (repeat[faceIndex] = faces);
						faces++;
					}
					else
					{
						Face face2 = mesh.Faces[repeat2];
						if (face2.cellA == bin)
						{
							face2.cellA = ordinal2;
						}
						else
						{
							face2.cellB = ordinal2;
						}
						mesh.Faces[repeat2] = face2;
					}
					facesSpan[k] = repeat2;
				}
				ordinal2++;
			}
		}
		mesh.CoalesceCellArrays();
		return mesh;
	}

	public void CoalesceCellArrays()
	{
		MeshFactory.CoalesceCellArrays(Cells);
	}

	public int NearestFaceInCell(int c, Point point, out double fDistance)
	{
		Cell cell = Cells[c];
		int faceIndexTerm = -1;
		fDistance = double.MaxValue;
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			double fDistance2 = FacePolyline(faceIndex).DistanceToPoint(point);
			if (faceIndexTerm == -1 || fDistance2 < fDistance)
			{
				faceIndexTerm = faceIndex;
				fDistance = fDistance2;
			}
		}
		return faceIndexTerm;
	}

	public Tin ExportAsTIN(MeshPropertyTables tables = null)
	{
		List<Point> points = new List<Point>(CellCount + FacePointCount);
		for (int i = 0; i < CellCount; i++)
		{
			points.Add(CellCenters[i]);
		}
		for (int j = 0; j < FacePointCount; j++)
		{
			points.Add(FacePoints[j].Point);
		}
		List<TriangleDefinition> triangleDefinitions = new List<TriangleDefinition>();
		int cellCount = CellCount;
		for (int k = 0; k < CellCount; k++)
		{
			List<int> cellFacePoints = CellFacePoints(k);
			for (int l = 0; l < cellFacePoints.Count; l++)
			{
				int cellFacePoint = cellFacePoints[l];
				int cellFacePoint2 = ((l != cellFacePoints.Count - 1) ? cellFacePoints[l + 1] : cellFacePoints[0]);
				triangleDefinitions.Add(new TriangleDefinition(k, cellCount + cellFacePoint, cellCount + cellFacePoint2, -1, -1, -1));
			}
		}
		Tin tin = Tin.RebuildAdjacencyInformation(points, triangleDefinitions);
		if (tables != null)
		{
			tin.TrackZ();
			for (int m = 0; m < CellCount; m++)
			{
				tin.Z[m] = tables.CellMinElevation(m);
			}
			for (int n = 0; n < FacePointCount; n++)
			{
				FacePoint facePoint = FacePoints[n];
				List<int> cellsAroundFacepoint = GetCellsAroundFacepoint(n);
				IList<int> faces = facePoint.Faces;
				float computed = 0f;
				int ordinal = 0;
				foreach (int cellsAroundFacepoint2 in cellsAroundFacepoint)
				{
					if (cellsAroundFacepoint2 != -1)
					{
						computed += tables.CellMinElevation(cellsAroundFacepoint2);
						ordinal++;
					}
				}
				foreach (int faceIndex in faces)
				{
					if (faceIndex != -1)
					{
						computed += tables.FaceMinElevation(faceIndex);
						ordinal++;
					}
				}
				computed = ((ordinal != 0) ? (computed / (float)ordinal) : (-9999f));
				tin.Z[n + CellCount] = computed;
			}
		}
		return tin;
	}

	public static void ComputeFaceAreaAndTopWidth(float ws, float faceLength, StationElevationProfile areaTable, out double area, out double topWidth)
	{
		for (int i = 1; i < areaTable.Count; i++)
		{
			if (areaTable[i].Y >= (double)ws)
			{
				topWidth = areaTable.InverseSlope(i);
				area = areaTable[i - 1].X + topWidth * ((double)ws - areaTable[i - 1].Y);
				return;
			}
		}
		topWidth = faceLength;
		IReadOnlyList<double> stations = areaTable.Stations;
		double station = stations[stations.Count - 1];
		double topWidth2 = topWidth;
		double ws2 = ws;
		IReadOnlyList<double> values = areaTable.Values;
		area = station + topWidth2 * (ws2 - values[values.Count - 1]);
	}

	public static double ComputeFacePerimeter(float ws, PointCollection perimeterTable)
	{
		for (int i = 1; i < perimeterTable.Count; i++)
		{
			if (perimeterTable[i].Y >= (double)ws)
			{
				double submergenceRatio = ((double)ws - perimeterTable[i - 1].Y) / (perimeterTable[i].Y - perimeterTable[i - 1].Y);
				return perimeterTable[i - 1].X * submergenceRatio + perimeterTable[i].X * (1.0 - submergenceRatio);
			}
		}
		return perimeterTable.Last().X;
	}

	public FaceMidside[] ComputeFaceMidsides()
	{
		FaceMidside[] faceMidsides = new FaceMidside[Faces.Length];
		for (int i = 0; i < Faces.Length; i++)
		{
			faceMidsides[i] = ComputeFaceMidside(i);
		}
		return faceMidsides;
	}

	public Vector[] ComputeFaceNormals()
	{
		Vector[] vectors = new Vector[Faces.Length];
		for (int i = 0; i < Faces.Length; i++)
		{
			Vector vector = FaceSegment(i).PerpendicularUnitVector();
			if (Math.Abs(vector.DeltaX) < 1E-12)
			{
				vector.DeltaX = 0.0;
			}
			else if (Math.Abs(vector.DeltaX - 1.0) < 1E-12)
			{
				vector.DeltaX = Math.Sign(vector.DeltaX);
			}
			if (Math.Abs(vector.DeltaY) < 1E-12)
			{
				vector.DeltaY = 0.0;
			}
			else if (Math.Abs(vector.DeltaY - 1.0) < 1E-12)
			{
				vector.DeltaY = Math.Sign(vector.DeltaY);
			}
			vector.Normalize();
			vectors[i] = new Vector(0.0 - vector.X, 0.0 - vector.Y);
		}
		return vectors;
	}

	public double[] ComputeFaceInvertLengths()
	{
		double[] faceInvertLengths = new double[Faces.Length];
		for (int i = 0; i < Faces.Length; i++)
		{
			faceInvertLengths[i] = 1.0 / FacePolylineLength(i);
		}
		return faceInvertLengths;
	}

	public double[] ComputeCellPolygonAreas()
	{
		int cellCount = CellCount;
		double[] cellPolygonAreas = new double[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			cellPolygonAreas[i] = CellPolygonArea(i);
		}
		return cellPolygonAreas;
	}

	public double[] ComputeCellPolygonAreaInv()
	{
		int cellCount = CellCount;
		double[] cellPolygonAreaInv = new double[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			cellPolygonAreaInv[i] = 1.0 / CellPolygonArea(i);
		}
		return cellPolygonAreaInv;
	}

	public double[] ComputeCellSimpleAreas()
	{
		int cellCount = CellCount;
		double[] cellSimpleAreas = new double[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			cellSimpleAreas[i] = CellSimpleArea(i);
		}
		return cellSimpleAreas;
	}

	public void SetFaceCentroidStations(ReadOnlySpan<float> stations)
	{
		if (stations.Length != FaceCount)
		{
			throw new ArgumentException("Face centroid station count must match the mesh face count.", nameof(stations));
		}
		_faceCentroidStation = stations.ToArray();
	}

	public ReadOnlyMemory<float> FaceCentroidStations => _faceCentroidStation;

	public float FaceCentroidDistance(int fidx)
	{
		if (_faceCentroidStation == null || _faceCentroidStation.Length != FaceCount)
		{
			throw new InvalidOperationException("Face low-elevation centroid stations have not been loaded for this mesh.");
		}
		return _faceCentroidStation[fidx];
	}

	public Point FaceCentroidPoint(int fidx)
	{
		return FaceSegment(fidx).Midpoint();
	}

	public Segment CellToCellSegment(int ca, int cb)
	{
		return new Segment(CellCenters[ca], CellCenters[cb]);
	}

	public Segment FacePerpendicularSegment(int fIdx)
	{
		Face face = Faces[fIdx];
		return CellToCellSegment(face.cellA, face.cellB);
	}

	public FaceMidside ComputeFaceMidside(int fIdx)
	{
		Face face = Faces[fIdx];
		Polyline polyline = FacePolyline(fIdx);
		PointOnPolyline pointOnPolyline = PointOnPolyline.NullPointOnPolyline;
		if (face.cellA != -1 && face.cellB != -1)
		{
			Segment seg = FacePerpendicularSegment(fIdx);
			LineIntersection? firstIntersection = polyline.GetFirstIntersection(seg);
			if (firstIntersection.HasValue)
			{
				double length = polyline.Length;
				double distanceAlongOverLength = polyline.DistanceAlong(firstIntersection.Value.AIntersection) / length;
				if ((distanceAlongOverLength > 0.05) & (distanceAlongOverLength < 0.95))
				{
					pointOnPolyline = firstIntersection.Value.AIntersection;
				}
			}
		}
		if (pointOnPolyline.IsNull())
		{
			pointOnPolyline = polyline.MidPointOnPolyline();
		}
		FaceMidside result = default;
		result.Point.X = pointOnPolyline.Point.X;
		result.Point.Y = pointOnPolyline.Point.Y;
		result.SegmentIndex = pointOnPolyline.SegmentIndex;
		return result;
	}

	public SpatialIndex<int> GetCellSpatialIndex()
	{
		return MetadataCache.GetCellSpatialIndex();
	}

	public SpatialIndex<int> ComputeCellSpatialIndex()
	{
		if (!IsValid())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent, SpatialIndex.ReasonableBinCount(Cells.Length));
		Memory<Point> points = new Point[8];
		for (int i = 0; i < Cells.Length; i++)
		{
			int cellPolygonPoints = TryGetCellPolygonPoints(i, points.Span);
			if (cellPolygonPoints > points.Length)
			{
				points = new Point[cellPolygonPoints];
				TryGetCellPolygonPoints(i, points.Span);
			}
			ValuePolygon polygon = new ValuePolygon(points.Slice(0, cellPolygonPoints));
			spatialIndex.Add(polygon, i);
		}
		return spatialIndex;
	}

	public SpatialIndex<int> GetFaceSpatialIndex()
	{
		return MetadataCache.GetFaceSpatialIndex();
	}

	public SpatialIndex<int> ComputeFaceSpatialIndex()
	{
		if (Faces.Length == 0 || !Extent.IsValid())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent, SpatialIndex.ReasonableBinCount(Faces.Length));
		for (int i = 0; i < Faces.Length; i++)
		{
			Face face = Faces[i];
			Memory<Point> faceInternalPoint = FaceInternalPoints[i];
			Point point = FacePoints[face.fpA].Point;
			Point point2 = FacePoints[face.fpB].Point;
			if (!faceInternalPoint.IsEmpty)
			{
				Span<Point> faceInternalPointSpan = faceInternalPoint.Span;
				Polyline polyline = new Polyline(faceInternalPointSpan.Length + 2);
				polyline.Add(new Point(point));
				polyline.AddRange(faceInternalPointSpan);
				polyline.Add(new Point(point2));
				spatialIndex.Add(polyline, i);
			}
			else
			{
				spatialIndex.Add(new Segment(point, point2), i);
			}
		}
		return spatialIndex;
	}

	public SpatialIndex<int> ComputeUniqueFaceSpatialIndexBySimpleMidpoint(int binCount = 0)
	{
		if (Faces.Length == 0 || !Extent.IsValid())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		int maxBinCount = ((binCount > 0) ? binCount : SpatialIndex.ReasonableBinCount(Faces.Length));
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent, maxBinCount);
		for (int i = 0; i < Faces.Length; i++)
		{
			Point pt = FaceSegmentMidpoint(i);
			spatialIndex.Add(pt, i);
		}
		return spatialIndex;
	}

	public SpatialIndex<int> GetFacePointSpatialIndex()
	{
		return MetadataCache.GetFacePointSpatialIndex();
	}

	public SpatialIndex<int> ComputeFacepointSpatialIndex()
	{
		if (Extent.IsNull())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent, FacePointCount);
		for (int i = 0; i < FacePointCount; i++)
		{
			FacePoint facePoint = FacePoints[i];
			if (!facePoint.IsNull())
			{
				spatialIndex.Add(facePoint.Point, i);
			}
		}
		return spatialIndex;
	}

	public BitArray GetSquareCells()
	{
		return LazyThreadsafe<BitArray>.AllocateOnce(ref _squareCells, _squareCellLock, () => ComputeSquareCells());
	}

	private BitArray ComputeSquareCells()
	{
		BitArray bitArray = new BitArray(CellCount);
		for (int i = 0; i < bitArray.Length; i++)
		{
			bitArray[i] = IsCellSquare(i);
		}
		return bitArray;
	}

	public int FindCell(Point pt)
	{
		List<int> elements = GetCellSpatialIndex().GetElements(pt);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (CellContains(element, pt))
			{
				return element;
			}
		}
		int ordinal = 0;
		double positiveInfinity = double.PositiveInfinity;
		Point[] cellCenters = CellCenters;
		for (int j = 0; j < cellCenters.Length; j++)
		{
			double cellCentersPtTerm = cellCenters[j].X - pt.X;
			double cellCentersPtTerm2 = cellCenters[j].Y - pt.Y;
			double positiveInfinity2 = cellCentersPtTerm * cellCentersPtTerm + cellCentersPtTerm2 * cellCentersPtTerm2;
			if (positiveInfinity2 < positiveInfinity)
			{
				positiveInfinity = positiveInfinity2;
				ordinal = j;
			}
		}
		return -ordinal;
	}

	public int[] FindCells(Memory<Point> points)
	{
		int[] cells = new int[points.Length];
		FindCells(points, cells);
		return cells;
	}

	public void FindCells(Memory<Point> points, Memory<int> outCellIndexes)
	{
		Span<Point> pointsSpan = points.Span;
		Span<int> outCellIndexesSpan = outCellIndexes.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			outCellIndexesSpan[i] = FindCell(pointsSpan[i]);
		}
	}

	public List<DiagnosticFace> FindFacesNear(Point pt, double distanceTolerance)
	{
		SpatialIndex<int> spatialIndex = ComputeFaceSpatialIndex();
		Extent extent = Extent.CenteredOn(pt, distanceTolerance * 2.0);
		List<int> elements = spatialIndex.GetElements(extent);
		List<DiagnosticFace> diagnosticFaces = new List<DiagnosticFace>();
		foreach (int element in elements)
		{
			Polyline polyline = FacePolyline(element);
			if (polyline.DistanceToPoint(pt) <= distanceTolerance)
			{
				diagnosticFaces.Add(new DiagnosticFace(this, Faces[element], element, polyline));
			}
		}
		return diagnosticFaces;
	}

	private bool IsCellSquare(int cidx)
	{
		Cell cell = Cells[cidx];
		if (cell.Faces.Span.Length != 4)
		{
			return false;
		}
		List<int> cellFacePoints = CellFacePoints(cidx);
		FacePoint facePoint = FacePoints[cellFacePoints[0]];
		FacePoint facePoint2 = FacePoints[cellFacePoints[1]];
		FacePoint facePoint3 = FacePoints[cellFacePoints[2]];
		FacePoint facePoint4 = FacePoints[cellFacePoints[3]];
		Span<double> a = stackalloc double[4]
		{
			facePoint.Point.X,
			facePoint2.Point.X,
			facePoint3.Point.X,
			facePoint4.Point.X
		};
		Buffers.MaxMin(a, out var max, out var min);
		a[0] = facePoint.Point.Y;
		a[1] = facePoint2.Point.Y;
		a[2] = facePoint3.Point.Y;
		a[3] = facePoint4.Point.Y;
		Buffers.MaxMin(a, out max, out var min2);
		double pointFacePointTerm = facePoint.Point.X - min;
		double pointFacePoint2Term = facePoint2.Point.X - min;
		double pointFacePoint3Term = facePoint3.Point.X - min;
		double pointFacePoint4Term = facePoint4.Point.X - min;
		double pointFacePointTerm2 = facePoint.Point.Y - min2;
		double pointFacePoint2Term2 = facePoint2.Point.Y - min2;
		double pointFacePoint3Term2 = facePoint3.Point.Y - min2;
		double pointFacePoint4Term2 = facePoint4.Point.Y - min2;
		double computed = pointFacePointTerm + pointFacePoint2Term + pointFacePoint3Term + pointFacePoint4Term;
		double computed2 = pointFacePointTerm2 + pointFacePoint2Term2 + pointFacePoint3Term2 + pointFacePoint4Term2;
		double computed3 = computed / 4.0;
		double computed4 = computed2 / 4.0;
		double computed5 = Math.Abs(pointFacePointTerm - computed3);
		if (Math.Abs(Math.Abs(pointFacePointTerm - computed3) - computed5) > 0.001 || Math.Abs(Math.Abs(pointFacePointTerm2 - computed4) - computed5) > 0.001)
		{
			return false;
		}
		if (Math.Abs(Math.Abs(pointFacePoint2Term - computed3) - computed5) > 0.001 || Math.Abs(Math.Abs(pointFacePoint2Term2 - computed4) - computed5) > 0.001)
		{
			return false;
		}
		if (Math.Abs(Math.Abs(pointFacePoint3Term - computed3) - computed5) > 0.001 || Math.Abs(Math.Abs(pointFacePoint3Term2 - computed4) - computed5) > 0.001)
		{
			return false;
		}
		if (Math.Abs(Math.Abs(pointFacePoint4Term - computed3) - computed5) > 0.001 || Math.Abs(Math.Abs(pointFacePoint4Term2 - computed4) - computed5) > 0.001)
		{
			return false;
		}
		return true;
	}

	public int GetDataHash()
	{
		int dataHash = Perimeter.GetDataHash();
		for (int i = 0; i < CellCount; i++)
		{
			Cell cell = Cells[i];
			dataHash = StableHash.Combine(dataHash, cell.GetDataHash());
		}
		for (int j = 0; j < FaceCount; j++)
		{
			Face face = Faces[j];
			dataHash = StableHash.Combine(dataHash, face.GetDataHash());
			Memory<Point> faceInternalPoint = FaceInternalPoints[j];
			if (!faceInternalPoint.IsEmpty)
			{
				Span<Point> faceInternalPointSpan = faceInternalPoint.Span;
				for (int k = 0; k < faceInternalPointSpan.Length; k++)
				{
					dataHash = StableHash.Combine(dataHash, faceInternalPointSpan[k].GetHashCode());
				}
			}
		}
		for (int l = 0; l < FacePointCount; l++)
		{
			FacePoint facePoint = FacePoints[l];
			dataHash = StableHash.Combine(dataHash, facePoint.GetDataHash());
		}
		return dataHash;
	}

	public void ComputeCellAdjacencyMatrix(out int[] rowPointers, out int[] columnIndices, bool addDiagonal = true, bool excludeBoundaryCells = true)
	{
		int cellLength = Cells.Length;
		rowPointers = new int[cellLength + 1];
		int count = cellLength * 8;
		int[] intValues = new int[count];
		int rowPointers2 = 0;
		rowPointers[0] = rowPointers2;
		for (int i = 0; i < cellLength; i++)
		{
			if (addDiagonal)
			{
				intValues[rowPointers2] = i;
				rowPointers2++;
			}
			List<int> cellsAroundCellWithBoundaries = ((!excludeBoundaryCells) ? GetCellsAroundCellWithBoundaries(i) : GetCellsAroundCell(i));
			cellsAroundCellWithBoundaries.Sort();
			for (int j = 0; j < cellsAroundCellWithBoundaries.Count; j++)
			{
				int cellsAroundCellWithBoundary = cellsAroundCellWithBoundaries[j];
				if (cellsAroundCellWithBoundary < cellLength)
				{
					intValues[rowPointers2] = cellsAroundCellWithBoundary;
					rowPointers2++;
				}
			}
			rowPointers[i + 1] = rowPointers2;
		}
		count = rowPointers2;
		columnIndices = new int[count];
		for (int i = 0; i < count; i++)
		{
			columnIndices[i] = intValues[i];
		}
	}

	public void ComputeCellAdjacency(out List<int>[] rows, bool addDiagonal = true)
	{
		rows = new List<int>[CellCount];
		for (int i = 0; i < CellCount; i++)
		{
			rows[i] = new List<int>();
			if (addDiagonal)
			{
				rows[i].Add(i);
			}
			rows[i].AddRange(GetCellsAroundCell(i));
		}
	}

	public void ComputeFaceAdjacencyMatrix(out int[] rowPointers, out int[] columnIndices)
	{
		int faceLength = Faces.Length;
		rowPointers = new int[faceLength + 1];
		int count = faceLength * 8;
		int[] intValues = new int[count];
		int rowPointers2 = 0;
		rowPointers[0] = rowPointers2;
		for (int i = 0; i < faceLength; i++)
		{
			intValues[rowPointers2] = i;
			rowPointers2++;
			List<int> facesAroundFace = GetFacesAroundFace(i);
			for (int j = 0; j < facesAroundFace.Count; j++)
			{
				int facesAroundFace2 = facesAroundFace[j];
				intValues[rowPointers2] = facesAroundFace2;
				rowPointers2++;
			}
			rowPointers[i + 1] = rowPointers2;
		}
		count = rowPointers2;
		columnIndices = new int[count];
		for (int i = 0; i < count; i++)
		{
			columnIndices[i] = intValues[i];
		}
	}

	public void ComputeCellSubsetAdjacencyMatrix(IReadOnlyList<int> subsetCells, out int[] rowPointers, out int[] columnIndices, bool addDiagonal, out int[] localToGlobal, out Dictionary<int, int> globalToLocal)
	{
		List<int> intList;
		if (subsetCells == null)
		{
			int cellCount = CellCount;
			intList = new List<int>(cellCount);
			for (int i = 0; i < cellCount; i++)
			{
				intList.Add(i);
			}
		}
		else
		{
			intList = new List<int>(subsetCells);
		}
		int count = intList.Count;
		localToGlobal = new int[count];
		globalToLocal = new Dictionary<int, int>(count);
		for (int j = 0; j < count; j++)
		{
			localToGlobal[j] = intList[j];
			globalToLocal[intList[j]] = j;
		}
		List<int>[] intListArray = new List<int>[count];
		for (int k = 0; k < count; k++)
		{
			intListArray[k] = new List<int>(8);
		}
		for (int l = 0; l < FaceCount; l++)
		{
			int cellA = Faces[l].cellA;
			int cellB = Faces[l].cellB;
			if (cellA >= 0 && cellB >= 0 && globalToLocal.TryGetValue(cellA, out var localValue) && globalToLocal.TryGetValue(cellB, out var localValue2))
			{
				intListArray[localValue].Add(localValue2);
				intListArray[localValue2].Add(localValue);
			}
		}
		rowPointers = new int[count + 1];
		List<int> intListList = new List<int>(count * 8 + (addDiagonal ? count : 0));
		rowPointers[0] = 0;
		for (int m = 0; m < count; m++)
		{
			if (addDiagonal)
			{
				intListList.Add(m);
			}
			if (intListArray[m].Count > 0)
			{
				List<int> intListArrayList = intListArray[m];
				intListArrayList.Sort();
				int ordinal = -1;
				for (int n = 0; n < intListArrayList.Count; n++)
				{
					int ordinal2 = intListArrayList[n];
					if (!((ordinal2 == m) & addDiagonal) && ordinal2 != ordinal)
					{
						intListList.Add(ordinal2);
						ordinal = ordinal2;
					}
				}
			}
			rowPointers[m + 1] = intListList.Count;
		}
		columnIndices = intListList.ToArray();
	}

	public void ComputeFacePointAdjacencyMatrix(out int[] rowPointers, out int[] columnIndices)
	{
		int facePointLength = FacePoints.Length;
		rowPointers = new int[facePointLength + 1];
		int count = facePointLength * 8;
		int[] intValues = new int[count];
		int rowPointers2 = 0;
		rowPointers[0] = rowPointers2;
		for (int i = 0; i < facePointLength; i++)
		{
			intValues[rowPointers2] = i;
			rowPointers2++;
			List<int> facePointsAroundFacePoint = GetFacePointsAroundFacePoint(i);
			for (int j = 0; j < facePointsAroundFacePoint.Count; j++)
			{
				int facePointsAroundFacePoint2 = facePointsAroundFacePoint[j];
				intValues[rowPointers2] = facePointsAroundFacePoint2;
				rowPointers2++;
			}
			rowPointers[i + 1] = rowPointers2;
		}
		count = rowPointers2;
		columnIndices = new int[count];
		for (int i = 0; i < count; i++)
		{
			columnIndices[i] = intValues[i];
		}
	}

	public JaggedWeights<float> SampleCellCenters(RasterDefinition destRaster, ISpatialTransformer sourceToDestTransform = null)
	{
		if (sourceToDestTransform != null)
		{
			Point[] transform = sourceToDestTransform.Transform(CellCenters);
			return destRaster.GetBilinearInterpolationWeights<float>(transform);
		}
		return destRaster.GetBilinearInterpolationWeights<float>(CellCenters);
	}

	public JaggedWeights<float> SampleFaceMidpoints(RasterDefinition destRaster, ISpatialTransformer sourceToDestTransform = null)
	{
		Point[] points = (from f in Enumerable.Range(0, FaceCount)
			select FaceMidPoint(f)).ToArray();
		if (sourceToDestTransform != null)
		{
			Point[] transform = sourceToDestTransform.Transform(points);
			return destRaster.GetBilinearInterpolationWeights<float>(transform);
		}
		return destRaster.GetBilinearInterpolationWeights<float>(points);
	}

	public Mesh Clone()
	{
		Mesh mesh = new Mesh();
		mesh._perimeter = _perimeter?.Clone();
		mesh.FacePoints = FacePoints.Select((FacePoint fp) => fp.Clone()).ToArray();
		mesh.Faces = Faces.Select((Face f) => f.Clone()).ToArray();
		mesh.Cells = Cells.Select((Cell f) => f.Clone()).ToArray();
		mesh.CellCenters = CellCenters?.ToArray();
		mesh.FaceInternalPoints = new Memory<Point>[FaceCount];
		int sum = FaceInternalPoints.Sum((Memory<Point> fip) => fip.Length);
		if (sum != 0)
		{
			Memory<Point> points = new Point[sum];
			int faceInternalPointLengthSum = 0;
			for (int intervalIndex = 0; intervalIndex < FaceCount; intervalIndex++)
			{
				Memory<Point> faceInternalPoint = FaceInternalPoints[intervalIndex];
				if (!faceInternalPoint.IsEmpty)
				{
					_ = faceInternalPoint.Span;
					Memory<Point> faceInternalPoints = points.Slice(faceInternalPointLengthSum, faceInternalPoint.Length);
					faceInternalPointLengthSum += faceInternalPoint.Length;
					faceInternalPoint.CopyTo(faceInternalPoints);
					mesh.FaceInternalPoints[intervalIndex] = faceInternalPoints;
				}
			}
		}
		mesh.CoalesceCellArrays();
		mesh.StreamwiseData = StreamwiseData?.Clone();
		return mesh;
	}

	public Mesh Subset(List<int> cellIndexes, out int[] cellMapping, out (int, int)[] faceMapping, out Dictionary<int, int> cellGlobalToLocal, out Dictionary<int, int> faceGlobalToLocal)
	{
		cellMapping = cellIndexes.ToArray();
		Mesh mesh = new Mesh();
		mesh._perimeter = null;
		mesh.Cells = cellIndexes.Select((int cIdx) => Cells[cIdx].Clone()).ToArray();
		if (CellCenters != null)
		{
			mesh.CellCenters = cellIndexes.Select((int cIdx) => CellCenters[cIdx]).ToArray();
		}
		else
		{
			mesh.CellCenters = null;
		}
		mesh.FacePoints = FacePoints;
		faceGlobalToLocal = new Dictionary<int, int>();
		cellGlobalToLocal = new Dictionary<int, int>();
		List<int> intList = new List<int>();
		for (int cellGlobalToLocal2 = 0; cellGlobalToLocal2 < cellIndexes.Count; cellGlobalToLocal2++)
		{
			int key = cellIndexes[cellGlobalToLocal2];
			cellGlobalToLocal[key] = cellGlobalToLocal2;
			Span<int> faces = mesh.Cells[cellGlobalToLocal2].Faces.Span;
			for (int intervalIndex = 0; intervalIndex < faces.Length; intervalIndex++)
			{
				int faceIndex = faces[intervalIndex];
				if (!faceGlobalToLocal.TryGetValue(faceIndex, out var faceGlobalToLocal2))
				{
					faceGlobalToLocal2 = intList.Count;
					intList.Add(faceIndex);
					faceGlobalToLocal[faceIndex] = faceGlobalToLocal2;
				}
				faces[intervalIndex] = faceGlobalToLocal2;
			}
		}
		mesh.Faces = intList.Select((int fIdxGlobal) => Faces[fIdxGlobal]).ToArray();
		faceMapping = intList.Select((int fIdxGlobal) => (fIdxGlobal, 1)).ToArray();
		mesh.FaceInternalPoints = intList.Select((int fIdxGlobal) => FaceInternalPoints[fIdxGlobal]).ToArray();
		for (int intervalIndex2 = 0; intervalIndex2 < mesh.Faces.Length; intervalIndex2++)
		{
			ref Face reference = ref mesh.Faces[intervalIndex2];
			if (!cellGlobalToLocal.TryGetValue(reference.cellA, out var unity))
			{
				unity = -1;
			}
			if (!cellGlobalToLocal.TryGetValue(reference.cellB, out var unity2))
			{
				unity2 = -1;
			}
			if (unity < 0)
			{
				reference.cellB = unity;
				reference.cellA = unity2;
				int fpA = reference.fpA;
				reference.fpA = reference.fpB;
				reference.fpB = fpA;
				faceMapping[intervalIndex2] = (intList[intervalIndex2], -1);
				mesh.FaceInternalPoints[intervalIndex2].Span.Reverse();
			}
			else
			{
				reference.cellA = unity;
				reference.cellB = unity2;
			}
		}
		mesh.CoalesceCellArrays();
		if (StreamwiseData != null)
		{
			mesh.StreamwiseData = StreamwiseData.Subset(mesh, cellMapping, faceMapping);
		}
		return mesh;
	}

	public void GetOneRing(int cIdx, out HashSet<int> cells, out HashSet<int> faces, out HashSet<int> nodes)
	{
		cells = new HashSet<int>();
		faces = new HashSet<int>();
		nodes = new HashSet<int>();
		Cell cell = Cells[cIdx];
		Span<int> facesSpan = cell.Faces.Span;
		for (int i = 0; i < facesSpan.Length; i++)
		{
			int faceIndex = facesSpan[i];
			Face face = Faces[faceIndex];
			int fPPrev = face.GetFPPrev(cIdx);
			nodes.Add(fPPrev);
			FacePoint facePoint = FacePoints[fPPrev];
			for (int j = 0; j < facePoint.Faces.Count; j++)
			{
				int faceIndex2 = facePoint.Faces[j];
				Face face2 = Faces[faceIndex2];
				faces.Add(faceIndex2);
				int cellA = face2.cellA;
				if (cellA != cIdx && cellA != -1)
				{
					cells.Add(cellA);
				}
				int cellB = face2.cellB;
				if (cellB != cIdx && cellB != -1)
				{
					cells.Add(cellB);
				}
			}
		}
	}

	public HashSet<int> GetOneRingCells(Memory<int> cells)
	{
		HashSet<int> oneRingCells = new HashSet<int>();
		Span<int> cellsSpan = cells.Span;
		for (int i = 0; i < cellsSpan.Length; i++)
		{
			int cellIndex = cellsSpan[i];
			Cell cell = Cells[cellIndex];
			Span<int> faces = cell.Faces.Span;
			for (int j = 0; j < faces.Length; j++)
			{
				Face face = Faces[faces[j]];
				FacePoint facePoint = FacePoints[face.GetFPPrev(cellIndex)];
				for (int k = 0; k < facePoint.Faces.Count; k++)
				{
					int faceIndex = facePoint.Faces[k];
					Face face2 = Faces[faceIndex];
					int cellA = face2.cellA;
					int cellB = face2.cellB;
					if (cellA != -1)
					{
						oneRingCells.Add(cellA);
					}
					if (cellB != -1)
					{
						oneRingCells.Add(cellB);
					}
				}
			}
		}
		return oneRingCells;
	}

	public int[] GetLocalNodeIndexes(int cIdx)
	{
		Cell cell = Cells[cIdx];
		int[] localNodeIndexes = new int[cell.Faces.Length];
		GetLocalNodeIndexes(cIdx, localNodeIndexes);
		return localNodeIndexes;
	}

	public void GetLocalNodeIndexes(int cIdx, Span<int> outNodeIndexes)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (outNodeIndexes.Length != faces.Length)
		{
			throw new ArgumentException("outNodeIndexes not sized correctly.");
		}
		for (int i = 0; i < faces.Length; i++)
		{
			int index = i;
			int faceIndex = faces[i];
			int fPPrev = Faces[faceIndex].GetFPPrev(cIdx);
			outNodeIndexes[index] = fPPrev;
		}
	}

	public T[] GetLocalNodeValues<T>(int cIdx, Span<T> globalNodeValues) where T : unmanaged
	{
		Cell cell = Cells[cIdx];
		T[] localNodeValues = new T[cell.Faces.Length];
		GetLocalNodeValues(cIdx, globalNodeValues, localNodeValues);
		return localNodeValues;
	}

	public void GetLocalNodeValues<T>(int cIdx, Span<T> globalNodeValues, Span<T> outLocalNodeValues) where T : unmanaged
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (outLocalNodeValues.Length != faces.Length)
		{
			throw new ArgumentException("outLocalNodeValues not sized correctly.");
		}
		for (int i = 0; i < faces.Length; i++)
		{
			int index = i;
			int faceIndex = faces[i];
			int fPPrev = Faces[faceIndex].GetFPPrev(cIdx);
			outLocalNodeValues[index] = globalNodeValues[fPPrev];
		}
	}

	public T[] GetLocalNodeValues<T>(int cIdx, T[][] fpValuesPerCell) where T : unmanaged
	{
		Cell cell = Cells[cIdx];
		T[] localNodeValues = new T[cell.Faces.Span.Length];
		GetLocalNodeValues(cIdx, fpValuesPerCell, localNodeValues);
		return localNodeValues;
	}

	public void GetLocalNodeValues<T>(int cIdx, T[][] nodeValuesPerCell, Span<T> outLocalNodeValues) where T : unmanaged
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (outLocalNodeValues.Length != faces.Length)
		{
			throw new ArgumentException("outLocalNodeValues not sized correctly.");
		}
		for (int i = 0; i < faces.Length; i++)
		{
			int index = i;
			int faceIndex = faces[i];
			int fPPrev = Faces[faceIndex].GetFPPrev(cIdx);
			outLocalNodeValues[index] = nodeValuesPerCell[fPPrev][FacePoints[fPPrev].Faces.IndexOf(faceIndex)];
		}
	}

	public Mesh ReorderCellsByAdjacency(List<int> regionOrder = null, List<int> cellRegions = null, List<int>[] regionCells = null)
	{
		int cellCount = CellCount;
		_ = FaceCount;
		if (cellRegions == null || regionCells == null || regionOrder == null)
		{
			cellRegions = new List<int>(cellCount);
			for (int i = 0; i < cellCount; i++)
			{
				cellRegions.Add(0);
			}
			regionCells = new List<int>[1];
			List<int> regionCellsList = new List<int>(cellCount);
			for (int j = 0; j < cellCount; j++)
			{
				regionCellsList.Add(j);
			}
			regionCells[0] = regionCellsList;
			regionOrder = new List<int>(1) { 0 };
		}
		if (cellRegions.Count != cellCount)
		{
			throw new ArgumentException("cellRegions must be length CellCount", "cellRegions");
		}
		int[] cellOldToNew = Enumerable.Repeat(-1, cellCount).ToArray();
		int nextCell = 0;
		foreach (int regionOrder2 in regionOrder)
		{
			AssignRegionCellOrder(regionOrder2);
		}
		if (nextCell < cellCount)
		{
			List<int> intList = new List<int>();
			for (int k = 0; k < cellCount; k++)
			{
				if (cellOldToNew[k] < 0)
				{
					intList.Add(k);
				}
			}
			intList.Sort((int a, int b) =>
			{
				Point point = CellCenters[a];
				Point point2 = CellCenters[b];
				int pointPoint2Term = point.X.CompareTo(point2.X);
				return (pointPoint2Term != 0) ? pointPoint2Term : point.Y.CompareTo(point2.Y);
			});
			foreach (int ordinal in intList)
			{
				cellOldToNew[ordinal] = nextCell++;
			}
		}
		ApplyCellOrdering(cellOldToNew, out var _);
		return this;
		void AssignRegionCellOrder(int region)
		{
			List<int> intListList = regionCells[region].Where((int c) => cellOldToNew[c] < 0).ToList();
			if (intListList.Count != 0)
			{
				Dictionary<int, int> intByInt = new Dictionary<int, int>(intListList.Count);
				for (int intervalIndex = 0; intervalIndex < intListList.Count; intervalIndex++)
				{
					intByInt[intListList[intervalIndex]] = intervalIndex;
				}
				ComputeCellSubsetAdjacencyMatrix(intListList, out var rowPointers, out var columnIndices, addDiagonal: false, out var localToGlobal, out var _);
				Reorder reorder = new Reorder();
				reorder.Compute(rowPointers, columnIndices, isReverse: true);
				localToGlobal = reorder.Perm;
				foreach (int index in localToGlobal)
				{
					int indexTerm = intListList[index];
					if (cellOldToNew[indexTerm] < 0)
					{
						cellOldToNew[indexTerm] = nextCell++;
					}
				}
			}
		}
	}

	public Mesh ReorderCellsRandom(int? seed = null)
	{
		int cellCount = CellCount;
		Random random = (seed.HasValue ? new Random(seed.Value) : new Random());
		int[] range = Enumerable.Range(0, cellCount).ToArray();
		for (int cellCountOffset = cellCount - 1; cellCountOffset > 0; cellCountOffset--)
		{
			int next = random.Next(cellCountOffset + 1);
			ref int reference = ref range[cellCountOffset];
			ref int reference2 = ref range[next];
			int range2 = range[next];
			int range3 = range[cellCountOffset];
			reference = range2;
			reference2 = range3;
		}
		int[] intValues = new int[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			int range4 = range[i];
			intValues[range4] = i;
		}
		ApplyCellOrdering(intValues, out var _);
		return this;
	}

	public void ApplyCellOrdering(int[] cellOldToNew, out int[] faceOldToNew)
	{
		int cellCount = CellCount;
		int faceCount = FaceCount;
		if (cellOldToNew == null || cellOldToNew.Length != cellCount)
		{
			throw new ArgumentException("cellOldToNew must be length CellCount", "cellOldToNew");
		}
		int[] intValues = new int[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			intValues[cellOldToNew[i]] = i;
		}
		Cell[] cells = new Cell[cellCount];
		Point[] cellCenters = ((CellCenters != null) ? new Point[cellCount] : null);
		for (int j = 0; j < cellCount; j++)
		{
			int ordinal = intValues[j];
			cells[j] = Cells[ordinal].Clone();
			if (cellCenters != null)
			{
				cellCenters[j] = CellCenters[ordinal];
			}
		}
		Cells = cells;
		if (cellCenters != null)
		{
			CellCenters = cellCenters;
		}
		for (int k = 0; k < faceCount; k++)
		{
			Face face = Faces[k];
			int cellA = ((face.cellA >= 0) ? cellOldToNew[face.cellA] : face.cellA);
			int cellB = ((face.cellB >= 0) ? cellOldToNew[face.cellB] : face.cellB);
			face.cellA = cellA;
			face.cellB = cellB;
			Faces[k] = face;
		}
		int[] thenBy = Enumerable.Range(0, faceCount).OrderBy((int f) =>
		{
			int cellA2 = Faces[f].cellA;
			int cellB2 = Faces[f].cellB;
			if (cellA2 < 0 && cellB2 < 0)
			{
				return int.MaxValue;
			}
			if (cellA2 < 0)
			{
				return cellB2;
			}
			return (cellB2 < 0) ? cellA2 : Math.Min(cellA2, cellB2);
		}).ThenBy((int f) =>
		{
			int cellA2 = Faces[f].cellA;
			int cellB2 = Faces[f].cellB;
			if (cellA2 < 0 && cellB2 < 0)
			{
				return int.MaxValue;
			}
			return (cellA2 < 0 || cellB2 < 0) ? 2147483646 : Math.Max(cellA2, cellB2);
		})
			.ThenBy((int f) => f)
			.ToArray();
		faceOldToNew = new int[faceCount];
		int[] intValuesArray = new int[faceCount];
		for (int faceOldToNew2 = 0; faceOldToNew2 < faceCount; faceOldToNew2++)
		{
			int thenBy2 = thenBy[faceOldToNew2];
			faceOldToNew[thenBy2] = faceOldToNew2;
			intValuesArray[faceOldToNew2] = thenBy2;
		}
		Face[] faces = new Face[faceCount];
		Memory<Point>[] faceInternalPoints = ((FaceInternalPoints != null) ? new Memory<Point>[faceCount] : null);
		for (int intervalIndex = 0; intervalIndex < faceCount; intervalIndex++)
		{
			int ordinal2 = intValuesArray[intervalIndex];
			faces[intervalIndex] = Faces[ordinal2];
			if (faceInternalPoints != null)
			{
				faceInternalPoints[intervalIndex] = FaceInternalPoints[ordinal2];
			}
		}
		Faces = faces;
		if (faceInternalPoints != null)
		{
			FaceInternalPoints = faceInternalPoints;
		}
		for (int intervalIndex2 = 0; intervalIndex2 < cellCount; intervalIndex2++)
		{
			Span<int> facesSpan = Cells[intervalIndex2].Faces.Span;
			if (facesSpan.Length != 0)
			{
				int[] facesArray = new int[facesSpan.Length];
				for (int intervalIndex3 = 0; intervalIndex3 < facesSpan.Length; intervalIndex3++)
				{
					facesArray[intervalIndex3] = faceOldToNew[facesSpan[intervalIndex3]];
				}
				Cells[intervalIndex2].Faces = facesArray;
			}
		}
		if (FacePoints != null)
		{
			for (int intervalIndex4 = 0; intervalIndex4 < FacePoints.Length; intervalIndex4++)
			{
				FacePoint facePoint = FacePoints[intervalIndex4];
				if (facePoint.Faces != null)
				{
					for (int intervalIndex5 = 0; intervalIndex5 < facePoint.Faces.Count; intervalIndex5++)
					{
						int faceIndex = facePoint.Faces[intervalIndex5];
						if (faceIndex >= 0 && faceIndex < faceCount)
						{
							facePoint.Faces[intervalIndex5] = faceOldToNew[faceIndex];
						}
					}
				}
				FacePoints[intervalIndex4] = facePoint;
			}
		}
		if (FacePoints != null && FacePoints.Length != 0)
		{
			int facePointLength = FacePoints.Length;
			int count = Faces?.Length ?? 0;
			int[] intValues2 = new int[facePointLength];
			for (int intervalIndex6 = 0; intervalIndex6 < facePointLength; intervalIndex6++)
			{
				intValues2[intervalIndex6] = -1;
			}
			int ordinal3 = 0;
			for (int intervalIndex7 = 0; intervalIndex7 < count; intervalIndex7++)
			{
				Face face2 = Faces[intervalIndex7];
				int fpA = face2.fpA;
				int fpB = face2.fpB;
				if ((uint)fpA < (uint)facePointLength && intValues2[fpA] < 0)
				{
					intValues2[fpA] = ordinal3++;
				}
				if ((uint)fpB < (uint)facePointLength && intValues2[fpB] < 0)
				{
					intValues2[fpB] = ordinal3++;
				}
			}
			for (int intervalIndex8 = 0; intervalIndex8 < facePointLength; intervalIndex8++)
			{
				if (intValues2[intervalIndex8] < 0)
				{
					intValues2[intervalIndex8] = ordinal3++;
				}
			}
			int[] intValues3 = new int[facePointLength];
			for (int intervalIndex9 = 0; intervalIndex9 < facePointLength; intervalIndex9++)
			{
				intValues3[intValues2[intervalIndex9]] = intervalIndex9;
			}
			FacePoint[] facePoints = new FacePoint[facePointLength];
			for (int intervalIndex10 = 0; intervalIndex10 < facePointLength; intervalIndex10++)
			{
				facePoints[intervalIndex10] = FacePoints[intValues3[intervalIndex10]];
			}
			FacePoints = facePoints;
			for (int intervalIndex11 = 0; intervalIndex11 < count; intervalIndex11++)
			{
				Face face3 = Faces[intervalIndex11];
				if ((uint)face3.fpA < (uint)facePointLength)
				{
					face3.fpA = intValues2[face3.fpA];
				}
				if ((uint)face3.fpB < (uint)facePointLength)
				{
					face3.fpB = intValues2[face3.fpB];
				}
				Faces[intervalIndex11] = face3;
			}
		}
		StreamwiseData?.ApplyCellOrdering(cellOldToNew);
	}

	public static List<int>[] RegionCellsLists(List<int> cellRegions, int numRegions)
	{
		List<int>[] intListArray = new List<int>[numRegions];
		if (numRegions > 0)
		{
			for (int i = 0; i < numRegions; i++)
			{
				intListArray[i] = new List<int>();
			}
			for (int j = 0; j < cellRegions.Count; j++)
			{
				int cellRegion = cellRegions[j];
				if (cellRegion >= 0)
				{
					intListArray[cellRegion].Add(j);
				}
			}
		}
		return intListArray;
	}

	public List<int> OrderedRegions(List<int> cellRegions, int numRegions, out List<int>[] regionCells)
	{
		int cellCount = CellCount;
		int count = 0;
		for (int i = 0; i < cellCount; i++)
		{
			count = Math.Max(count, cellRegions[i] + 1);
		}
		regionCells = RegionCellsLists(cellRegions, numRegions);
		Point[] points = new Point[count];
		for (int j = 0; j < count; j++)
		{
			if (regionCells[j].Count == 0)
			{
				points[j] = Point.NullPoint;
				continue;
			}
			double computed = 0.0;
			double computed2 = 0.0;
			int ordinal = 0;
			foreach (int regionCell in regionCells[j])
			{
				Point point = CellCenters[regionCell];
				computed += point.X;
				computed2 += point.Y;
				ordinal++;
			}
			points[j] = new Point(computed / (double)ordinal, computed2 / (double)ordinal);
		}
		return OrderedRegionsFromCenters(points);
	}

	private static List<int> OrderedRegionsFromCenters(Point[] regionCenters)
	{
		int regionCenterLength = regionCenters.Length;
		List<int> intList = new List<int>();
		HashSet<int> range = new HashSet<int>(Enumerable.Range(0, regionCenterLength));
		int ordinal = -1;
		double maxValue = double.MaxValue;
		double maxValue2 = double.MaxValue;
		for (int i = 0; i < regionCenterLength; i++)
		{
			Point point = regionCenters[i];
			if (!point.IsNull() && (point.X < maxValue || (Math.Abs(point.X - maxValue) < 1E-12 && point.Y < maxValue2)))
			{
				maxValue = point.X;
				maxValue2 = point.Y;
				ordinal = i;
			}
		}
		if (ordinal == -1)
		{
			return intList;
		}
		int ordinal2 = ordinal;
		while (range.Count > 0)
		{
			intList.Add(ordinal2);
			range.Remove(ordinal2);
			if (range.Count == 0)
			{
				break;
			}
			double maxValue3 = double.MaxValue;
			int range2Term = -1;
			Point point2 = regionCenters[ordinal2];
			foreach (int range2 in range)
			{
				double distanceSquaredTo = point2.DistanceSquaredTo(regionCenters[range2]);
				if (distanceSquaredTo < maxValue3)
				{
					maxValue3 = distanceSquaredTo;
					range2Term = range2;
				}
			}
			ordinal2 = range2Term;
		}
		return intList;
	}
}
