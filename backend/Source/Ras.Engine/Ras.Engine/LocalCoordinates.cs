using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Utility.Logging;

namespace Ras.Engine;

public class LocalCoordinates
{
	private Mesh _mesh;

	private ComputeControl.CellFlag[] _cellFlags;

	public Vector[,] FaceNormal { get; private set; }

	public double[] FaceInvDistances { get; private set; }

	public float[,] CellCurvature { get; private set; }

	public LocalCoordinates(Mesh mesh, ComputeControl.CellFlag[] cellFlags)
	{
		_mesh = mesh;
		_cellFlags = cellFlags;
		SetNormals();
		SetFaceInvDistances();
		SetCellCurvature();
	}

	public Vector RotateThroughFace(Vector input, int fIdx, int fromSide)
	{
		Vector vector = FaceNormal[fIdx, 2];
		if (fromSide == 0)
		{
			return new Vector(vector.X * input.X - vector.Y * input.Y, vector.Y * input.X + vector.X * input.Y);
		}
		return new Vector(vector.X * input.X + vector.Y * input.Y, (0.0 - vector.Y) * input.X + vector.X * input.Y);
	}

	public Vector RotateToFace(Vector input, int fIdx, int fromSide)
	{
		Vector vector = FaceNormal[fIdx, fromSide];
		return new Vector(vector.X * input.X + vector.Y * input.Y, (0.0 - vector.Y) * input.X + vector.X * input.Y);
	}

	private void SetNormals()
	{
		FaceNormal = new Vector[_mesh.FaceCount, 3];
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				FaceNormal[i, j] = new Vector(double.NaN, double.NaN);
			}
		}
		Vector[] faceNormal = _mesh.MetadataCache.FaceNormal;
		for (int k = 0; k < _mesh.CellCount; k++)
		{
			if ((_cellFlags[k] & ComputeControl.CellFlag.StreamwiseCoordinates) == 0)
			{
				SetCellFaceNormalsCartesian(k, faceNormal);
			}
			else if (_mesh.StreamwiseData == null)
			{
				SetCellFaceNormalsInferredStreamwise(k);
			}
			else
			{
				SetCellFaceNormalsStreamwise(k);
			}
		}
		for (int l = 0; l < _mesh.FaceCount; l++)
		{
			if (_mesh.Faces[l].cellB < 0)
			{
				FaceNormal[l, 2] = new Vector(1.0, 0.0);
				continue;
			}
			Vector vector = FaceNormal[l, 0];
			Vector vector2 = FaceNormal[l, 1];
			double dx = vector.X * vector2.X + vector.Y * vector2.Y;
			double dy = vector.X * vector2.Y - vector.Y * vector2.X;
			FaceNormal[l, 2] = new Vector(dx, dy);
		}
	}

	private void SetCellFaceNormalsCartesian(int cIdx, Vector[] fNorms)
	{
		Span<int> faces = _mesh.Cells[cIdx].Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = _mesh.Faces[faceIndex];
			if (face.cellA == cIdx)
			{
				FaceNormal[faceIndex, 0] = fNorms[faceIndex];
				continue;
			}
			if (face.cellB == cIdx)
			{
				FaceNormal[faceIndex, 1] = fNorms[faceIndex];
				continue;
			}
			throw new Exception("Mesh is corrupt");
		}
	}

	private void SetCellFaceNormalsStreamwise(int cIdx)
	{
		Span<int> faces = _mesh.Cells[cIdx].Faces.Span;
		List<Vector> faceNormals = _mesh.StreamwiseData.GetFaceNormals(cIdx);
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			int cellAFacesTerm = ((_mesh.Faces[faceIndex].cellA != cIdx) ? 1 : 0);
			FaceNormal[faceIndex, cellAFacesTerm] = faceNormals[i];
		}
	}

	private void SetCellFaceNormalsInferredStreamwise(int cIdx)
	{
		Span<int> faces = _mesh.Cells[cIdx].Faces.Span;
		if (faces.Length != 4)
		{
			throw new Exception("Currently only quad cells can have streamwise coordinates");
		}
		for (int i = 0; i < 4; i++)
		{
			int faceIndex = faces[i];
			Face face = _mesh.Faces[faceIndex];
			int xComponent;
			int intervalIndex;
			if (face.cellA == cIdx)
			{
				xComponent = 1;
				intervalIndex = 0;
			}
			else
			{
				if (face.cellB != cIdx)
				{
					throw new Exception("Mesh corrupt");
				}
				xComponent = -1;
				intervalIndex = 1;
			}
			switch (i)
			{
			case 0:
				FaceNormal[faceIndex, intervalIndex] = new Vector(-xComponent, 0.0);
				break;
			case 1:
				FaceNormal[faceIndex, intervalIndex] = new Vector(0.0, -xComponent);
				break;
			case 2:
				FaceNormal[faceIndex, intervalIndex] = new Vector(xComponent, 0.0);
				break;
			case 3:
				FaceNormal[faceIndex, intervalIndex] = new Vector(0.0, xComponent);
				break;
			default:
				throw new Exception("Iteration fail");
			}
		}
	}

	private void SetFaceInvDistances()
	{
		FaceInvDistances = _mesh.MetadataCache.FaceInvPerpDistances.ToArray();
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			Face face = _mesh.Faces[i];
			if (face.cellB >= 0 && ((_cellFlags[face.cellA] | _cellFlags[face.cellB]) & ComputeControl.CellFlag.StreamwiseCoordinates) != 0)
			{
				double cellFlagsCellATerm = 0.0;
				Point point = _mesh.FacePolyline(i).MidPointOnPolyline().Point;
				cellFlagsCellATerm = (((_cellFlags[face.cellA] & ComputeControl.CellFlag.StreamwiseCoordinates) == 0) ? (cellFlagsCellATerm + _mesh.CellCenters[face.cellA].DistanceTo(point)) : (cellFlagsCellATerm + CurvilinearDistanceInCell(face.cellA, point, _mesh.CellCenters[face.cellA])));
				cellFlagsCellATerm = (((_cellFlags[face.cellB] & ComputeControl.CellFlag.StreamwiseCoordinates) == 0) ? (cellFlagsCellATerm + _mesh.CellCenters[face.cellB].DistanceTo(point)) : (cellFlagsCellATerm + CurvilinearDistanceInCell(face.cellB, point, _mesh.CellCenters[face.cellB])));
				FaceInvDistances[i] = 1.0 / cellFlagsCellATerm;
			}
		}
	}

	public void SetCellCurvature()
	{
		CellCurvature = new float[_mesh.CellCount, 2];
		Vector[] vectors = new Vector[2];
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			if ((_cellFlags[i] & ComputeControl.CellFlag.StreamwiseCoordinates) == 0)
			{
				CellCurvature[i, 0] = 0f;
				CellCurvature[i, 1] = 0f;
				continue;
			}
			UnitCoonsPatch unitCoonsPatch = CellCoonsPatch(i);
			Span<int> faces = _mesh.Cells[i].Faces.Span;
			for (int j = 0; j < 2; j++)
			{
				double curvilinearDistance = unitCoonsPatch.CurvilinearDistance((j == 0) ? new Point(0.0, 0.5) : new Point(0.5, 0.0), (j == 0) ? new Point(1.0, 0.5) : new Point(0.5, 1.0), unitCoords: true);
				for (int k = 0; k < 2; k++)
				{
					int faceIndex = faces[j + 2 * k];
					Face face = _mesh.Faces[faceIndex];
					int cellAFaceTerm = ((i != face.cellA) ? 1 : 0);
					int ordinal = 1 - 2 * cellAFaceTerm;
					vectors[k] = ordinal * _mesh.MetadataCache.FaceNormal[faceIndex];
				}
				double angleBetweenRadsVectorsTerm = 0.0 - vectors[1].AngleBetweenRads(-1f * vectors[0]);
				CellCurvature[i, j] = (float)(angleBetweenRadsVectorsTerm / curvilinearDistance);
			}
		}
	}

	public double CurvilinearDistanceInCell(int cIdx, Point a, Point b)
	{
		return CellCoonsPatch(cIdx).CurvilinearDistance(a, b);
	}

	public UnitCoonsPatch CellCoonsPatch(int cIdx)
	{
		Span<int> faces = _mesh.Cells[cIdx].Faces.Span;
		if (faces.Length != 4)
		{
			throw new StructuredException(Message.GenericError("Non-quad cell is not supported by local/streamwise coordinate"));
		}
		Polyline[] polylines = new Polyline[faces.Length];
		for (int i = 0; i < faces.Length; i++)
		{
			polylines[i] = _mesh.FacePolyline(faces[i]);
			if (_mesh.Faces[faces[i]].cellB == cIdx)
			{
				polylines[i] = polylines[i].Clone();
				polylines[i].Reverse();
			}
		}
		return new UnitCoonsPatch(polylines[1], polylines[2], polylines[3], polylines[0]);
	}

	public Vector[] CellCoordXVectors()
	{
		Vector[] vectors = new Vector[_mesh.CellCount];
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			if ((_cellFlags[i] & ComputeControl.CellFlag.StreamwiseCoordinates) != 0)
			{
				UnitCoonsPatch unitCoonsPatch = CellCoonsPatch(i);
				Point point = unitCoonsPatch.Forward(new Point(0.5, 0.5));
				Point point2 = unitCoonsPatch.Forward(new Point(0.5001, 0.5));
				vectors[i] = (point2 - point).UnitVector();
			}
			else
			{
				vectors[i] = new Vector(1.0, 0.0);
			}
		}
		return vectors;
	}
}
