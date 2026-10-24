using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Geospatial.DataStructures;
using Utility;
using Utility.Matrices;
using Utility.Units;

namespace Geospatial.Vectors.Meshes;

public class HydraulicMeshComputer
{
	public enum MultidimensionalSlopeLimiter
	{
		None,
		BarthJespersen,
		SmoothCubic
	}

	public sealed class ScalarGradientWlsWorkspace
	{
		private readonly HydraulicMeshComputer _hmc;

		private readonly SparseMatrix<Vector> _coefficients;

		private readonly int[] _neighborFaceIndices;

		private readonly byte[] _neighborWet;

		private readonly bool[] _centerWet;

		private readonly bool[] _prepared;

		public int CellCount => _hmc.CellCount;

		internal ScalarGradientWlsWorkspace(HydraulicMeshComputer hmc)
		{
			_hmc = hmc;
			hmc.EnsureScalarGradientWlsConnectivity();
			int cellCount = hmc.CellCount;
			_coefficients = new SparseMatrix<Vector>(hmc._scalarGradientWlsStructure);
			_neighborFaceIndices = hmc._scalarGradientWlsNeighborFaceIndices;
			_neighborWet = new byte[_coefficients.NonZeroCount];
			_centerWet = new bool[cellCount];
			_prepared = new bool[cellCount];
		}

		public void PrepareCell(int cIdx, ReadOnlySpan<double> cellVolumes)
		{
			PrepareCell(cIdx, cellVolumes, default, null, null);
		}

		public void PrepareCell(int cIdx, ReadOnlySpan<double> cellVolumes, ReadOnlySpan<double> faceAreas, bool[] cellActive, bool[] faceActive)
		{
			int rowPointerStart = _coefficients.RowPointers[cIdx];
			int rowPointerEnd = _coefficients.RowPointers[cIdx + 1];
			bool isWet = IsWet(cellVolumes[cIdx], cellActive, cIdx);
			bool preparedCIdxTerm = !_prepared[cIdx] || _centerWet[cIdx] != isWet;
			for (int i = rowPointerStart; i < rowPointerEnd; i++)
			{
				int columnIndex = _coefficients.ColumnIndices[i];
				int face = _neighborFaceIndices[i];
				byte b = ((isWet && IsWet(cellVolumes[columnIndex], cellActive, columnIndex) && IsNeighborConnected(cIdx, columnIndex, face, faceAreas, faceActive)) ? ((byte)1) : ((byte)0));
				preparedCIdxTerm |= _neighborWet[i] != b;
				_neighborWet[i] = b;
			}
			if (preparedCIdxTerm)
			{
				_coefficients.Values.AsSpan(rowPointerStart, rowPointerEnd - rowPointerStart).Clear();
				if (isWet)
				{
					PrepareWetCell(cIdx, rowPointerStart, rowPointerEnd);
				}
				_centerWet[cIdx] = isWet;
				_prepared[cIdx] = true;
			}
		}

		public Vector ScalarGradient(int cIdx, ReadOnlySpan<double> cellValues)
		{
			if (!_centerWet[cIdx])
			{
				return Vector.Zero;
			}
			return SparseMatrix<Vector>.ProductRowDifferences(_coefficients, cellValues, cIdx);
		}

		public Vector ScalarGradientLimited(int cIdx, ReadOnlySpan<double> cellValues, MultidimensionalSlopeLimiter limiterType, out double limiter)
		{
			Vector gradient = ScalarGradient(cIdx, cellValues);
			limiter = ScalarGradientLimiter(cIdx, gradient, cellValues, limiterType);
			return new Vector(gradient.DeltaX * limiter, gradient.DeltaY * limiter);
		}

		public double ScalarGradientLimiter(int cIdx, Vector gradient, ReadOnlySpan<double> cellValues, MultidimensionalSlopeLimiter limiterType)
		{
			return SlopeLimiter(cIdx, gradient, cellValues, limiterType);
		}

		internal Vector ScalarGradientLimitedWithBoundary(int cIdx, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, ScalarBoundaryReconstructionWorkspace boundary, MultidimensionalSlopeLimiter limiterType, Vector fallback)
		{
			Vector vector = ScalarGradientWithBoundary(cIdx, cellValues, cellVolumes, boundary, limiterType, fallback, 1.0, out var limiter);
			return new Vector(vector.DeltaX * limiter, vector.DeltaY * limiter);
		}

		internal Vector ScalarGradientWithBoundary(int cIdx, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, ScalarBoundaryReconstructionWorkspace boundary, MultidimensionalSlopeLimiter limiterType, Vector fallbackGradient, double fallbackLimiter, out double limiter)
		{
			if (!_centerWet[cIdx])
			{
				limiter = 1.0;
				return Vector.Zero;
			}
			Point point = _hmc.Mesh.CellCenters[cIdx];
			double cellValue = cellValues[cIdx];
			double a = 0.0;
			double a2 = 0.0;
			double a3 = 0.0;
			double b = 0.0;
			double b2 = 0.0;
			double cellValue2 = cellValue;
			double cellValue3 = cellValue;
			int observationCount = 0;
			int rowPointerStart = _coefficients.RowPointers[cIdx];
			int rowPointerEnd = _coefficients.RowPointers[cIdx + 1];
			for (int i = rowPointerStart; i < rowPointerEnd; i++)
			{
				if (_neighborWet[i] != 0)
				{
					int columnIndex = _coefficients.ColumnIndices[i];
					Point point2 = _hmc.Mesh.CellCenters[columnIndex];
					double cellValue4 = cellValues[columnIndex];
					AddObservation(point2.X - point.X, point2.Y - point.Y, cellValue4 - cellValue, ref a, ref a2, ref a3, ref b, ref b2, ref observationCount);
					cellValue2 = Math.Min(cellValue2, cellValue4);
					cellValue3 = Math.Max(cellValue3, cellValue4);
				}
			}
			Span<int> faces = _hmc.Mesh.Cells[cIdx].Faces.Span;
			for (int j = 0; j < faces.Length; j++)
			{
				int face = faces[j];
				if (boundary.TryGetObservation(face, cIdx, cellValue, out var deltaX, out var deltaY, out var deltaValue, out var observationValue))
				{
					AddObservation(deltaX, deltaY, deltaValue, ref a, ref a2, ref a3, ref b, ref b2, ref observationCount);
					cellValue2 = Math.Min(cellValue2, observationValue);
					cellValue3 = Math.Max(cellValue3, observationValue);
				}
			}
			double a3Term = a + a3;
			double a3A2Term = a * a3 - a2 * a2;
			double computed = 1E-10 * a3Term * a3Term;
			if (observationCount < 2 || !double.IsFinite(a3Term) || !double.IsFinite(a3A2Term) || a3A2Term <= computed)
			{
				limiter = fallbackLimiter;
				return fallbackGradient;
			}
			double computed2 = 1.0 / a3A2Term;
			Vector vector = new Vector((a3 * b - a2 * b2) * computed2, (a * b2 - a2 * b) * computed2);
			limiter = BoundarySlopeLimiter(cIdx, vector, cellValue, cellValue2, cellValue3, limiterType);
			return vector;
		}

		private static void AddObservation(double deltaX, double deltaY, double deltaValue, ref double a11, ref double a12, ref double a22, ref double b1, ref double b2, ref int observationCount)
		{
			double deltaXDeltaYTerm = deltaX * deltaX + deltaY * deltaY;
			if (deltaXDeltaYTerm > 0.0 && double.IsFinite(deltaXDeltaYTerm) && double.IsFinite(deltaValue))
			{
				double computed = 1.0 / deltaXDeltaYTerm;
				a11 += computed * deltaX * deltaX;
				a12 += computed * deltaX * deltaY;
				a22 += computed * deltaY * deltaY;
				b1 += computed * deltaX * deltaValue;
				b2 += computed * deltaY * deltaValue;
				observationCount++;
			}
		}

		private double BoundarySlopeLimiter(int cIdx, Vector gradient, double cellValue, double minimum, double maximum, MultidimensionalSlopeLimiter limiterType)
		{
			if (limiterType == MultidimensionalSlopeLimiter.None)
			{
				return 1.0;
			}
			Point point = _hmc.Mesh.CellCenters[cIdx];
			double limitFaceTerm = 1.0;
			Span<int> faces = _hmc.Mesh.Cells[cIdx].Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int fIdx = faces[i];
				Point point2 = _hmc.Mesh.FaceMidPoint(fIdx);
				double deltaXGradientTerm = gradient.DeltaX * (point2.X - point.X) + gradient.DeltaY * (point2.Y - point.Y);
				double limitFace;
				if (deltaXGradientTerm > 0.0)
				{
					limitFace = LimitFace((maximum - cellValue) / deltaXGradientTerm, limiterType);
				}
				else
				{
					limitFace = ((!(deltaXGradientTerm < 0.0)) ? 1.0 : LimitFace((minimum - cellValue) / deltaXGradientTerm, limiterType));
				}
				limitFaceTerm = Math.Min(limitFaceTerm, limitFace);
			}
			return limitFaceTerm;
		}

		private void PrepareWetCell(int cIdx, int start, int end)
		{
			Point point = _hmc.Mesh.CellCenters[cIdx];
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			int ordinal = 0;
			for (int i = start; i < end; i++)
			{
				if (_neighborWet[i] != 0)
				{
					Point point2 = _hmc.Mesh.CellCenters[_coefficients.ColumnIndices[i]];
					double point2PointTerm = point2.X - point.X;
					double point2PointTerm2 = point2.Y - point.Y;
					double computed4 = point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2;
					if (!(computed4 <= 0.0))
					{
						double computed5 = 1.0 / computed4;
						computed += computed5 * point2PointTerm * point2PointTerm;
						computed2 += computed5 * point2PointTerm * point2PointTerm2;
						computed3 += computed5 * point2PointTerm2 * point2PointTerm2;
						ordinal++;
					}
				}
			}
			double computed6 = computed + computed3;
			double computed7 = computed * computed3 - computed2 * computed2;
			double computed8 = 1E-10 * computed6 * computed6;
			if (ordinal < 2 || !double.IsFinite(computed6) || !double.IsFinite(computed7) || computed7 <= computed8)
			{
				return;
			}
			double computed9 = 1.0 / computed7;
			for (int j = start; j < end; j++)
			{
				if (_neighborWet[j] != 0)
				{
					Point point3 = _hmc.Mesh.CellCenters[_coefficients.ColumnIndices[j]];
					double point3PointTerm = point3.X - point.X;
					double point3PointTerm2 = point3.Y - point.Y;
					double computed10 = point3PointTerm * point3PointTerm + point3PointTerm2 * point3PointTerm2;
					if (!(computed10 <= 0.0))
					{
						double computed11 = point3PointTerm / computed10;
						double computed12 = point3PointTerm2 / computed10;
						_coefficients.Values[j] = new Vector((computed3 * computed11 - computed2 * computed12) * computed9, (computed * computed12 - computed2 * computed11) * computed9);
					}
				}
			}
		}

		private double SlopeLimiter(int cIdx, Vector gradient, ReadOnlySpan<double> cellValues, MultidimensionalSlopeLimiter limiterType)
		{
			if (limiterType == MultidimensionalSlopeLimiter.None)
			{
				return 1.0;
			}
			double cellValue = cellValues[cIdx];
			double cellValue2 = cellValue;
			double cellValue3 = cellValue;
			int rowPointerStart = _coefficients.RowPointers[cIdx];
			int rowPointerEnd = _coefficients.RowPointers[cIdx + 1];
			for (int i = rowPointerStart; i < rowPointerEnd; i++)
			{
				if (_neighborWet[i] != 0)
				{
					double cellValue4 = cellValues[_coefficients.ColumnIndices[i]];
					cellValue2 = Math.Min(cellValue2, cellValue4);
					cellValue3 = Math.Max(cellValue3, cellValue4);
				}
			}
			Point point = _hmc.Mesh.CellCenters[cIdx];
			double deltaX = gradient.DeltaX;
			double deltaY = gradient.DeltaY;
			double limitFaceTerm = 1.0;
			Span<int> faces = _hmc.Mesh.Cells[cIdx].Faces.Span;
			for (int j = 0; j < faces.Length; j++)
			{
				int fIdx = faces[j];
				Point point2 = _hmc.Mesh.FaceMidPoint(fIdx);
				double deltaXPoint2Term = deltaX * (point2.X - point.X) + deltaY * (point2.Y - point.Y);
				double limitFace;
				if (deltaXPoint2Term > 0.0)
				{
					limitFace = LimitFace((cellValue3 - cellValue) / deltaXPoint2Term, limiterType);
				}
				else
				{
					limitFace = ((!(deltaXPoint2Term < 0.0)) ? 1.0 : LimitFace((cellValue2 - cellValue) / deltaXPoint2Term, limiterType));
				}
				limitFaceTerm = Math.Min(limitFaceTerm, limitFace);
			}
			return limitFaceTerm;
		}

		private static double LimitFace(double ratio, MultidimensionalSlopeLimiter limiterType)
		{
			return limiterType switch
			{
				MultidimensionalSlopeLimiter.BarthJespersen => Math.Min(1.0, ratio), 
				MultidimensionalSlopeLimiter.SmoothCubic => SmoothCubic(ratio), 
				_ => throw new ArgumentOutOfRangeException("limiterType"), 
			};
		}

		private static bool IsWet(double magnitude, bool[] active, int index)
		{
			if (magnitude > 0.0 && double.IsFinite(magnitude))
			{
				if (active != null)
				{
					return active[index];
				}
				return true;
			}
			return false;
		}

		private bool IsNeighborConnected(int cell, int neighbor, int face, ReadOnlySpan<double> faceAreas, bool[] faceActive)
		{
			if (faceAreas.IsEmpty)
			{
				return true;
			}
			if (face >= 0)
			{
				return IsWet(faceAreas[face], faceActive, face);
			}
			Span<int> faces = _hmc.Mesh.Cells[cell].Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				if (_hmc.Mesh.Faces[faceIndex].IsEitherCell(neighbor) && IsWet(faceAreas[faceIndex], faceActive, faceIndex))
				{
					return true;
				}
			}
			return false;
		}
	}

	public struct CellCompute(HydraulicMeshComputer hmc)
	{
		private readonly HydraulicMeshComputer _hmc = hmc;

		public float Scalar_FromFaces_PlanarZ(int cIdx, Span<FaceValues> faceValues, Func<int, Point> GetFaceApplicationPoint)
		{
			Cell cell = _hmc.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			PlanarRegression planarRegression = new PlanarRegression(_hmc.Mesh.CellCenters[cIdx]);
			Span<int> facesSpan = faces;
			for (int i = 0; i < facesSpan.Length; i++)
			{
				int faceIndex = facesSpan[i];
				Face face = _hmc.Faces[faceIndex];
				float valueB = ((cIdx != face.cellA) ? faceValues[faceIndex].ValueB : faceValues[faceIndex].ValueA);
				if ((double)valueB != -9999.0)
				{
					planarRegression.Add(GetFaceApplicationPoint(faceIndex), valueB);
				}
			}
			if (planarRegression.Count == 0)
			{
				return -9999f;
			}
			return (float)planarRegression.SolveZ();
		}

		public float Scalar_FromNodes_PlanarZ(int cIdx, Span<float> nodeValues)
		{
			Cell cell = _hmc.Cells[cIdx];
			PlanarRegression planarRegression = new PlanarRegression(_hmc.Mesh.CellCenters[cIdx]);
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Face face = _hmc.Faces[faceIndex];
				int fPNext = face.GetFPNext(cIdx);
				float nodeValue = nodeValues[faceIndex];
				if ((double)nodeValue != -9999.0)
				{
					FacePoint facePoint = _hmc.FacePoints[fPNext];
					planarRegression.Add(facePoint.Point, nodeValue);
				}
			}
			if (planarRegression.Count == 0)
			{
				return -9999f;
			}
			return (float)planarRegression.SolveZ();
		}

		public float[] Scalar_FromFaces(Span<float> faceScalarValues, SpatialAveragingMode mode)
		{
			float[] floatValues = new float[_hmc.Mesh.CellCount];
			Scalar_FromFaces(faceScalarValues, mode, floatValues);
			return floatValues;
		}

		public void Scalar_FromFaces(Span<float> faceScalarValues, SpatialAveragingMode mode, Span<float> outCellVals)
		{
			if (outCellVals.Length != _hmc.Mesh.CellCount)
			{
				throw new ArgumentException("outCellVals wrong size.");
			}
			switch (mode)
			{
			case SpatialAveragingMode.SimpleAverage:
				Scalar_FromFaces_SimpleAverage(faceScalarValues, outCellVals);
				break;
			case SpatialAveragingMode.PlanarRegression:
				throw new NotImplementedException();
			default:
				throw new NotSupportedException();
			}
		}

		public T[] Scalar_FromFaces_SimpleAverage<T>(Span<T> faceScalarValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			T[] values = new T[_hmc.Mesh.CellCount];
			Scalar_FromFaces_SimpleAverage(faceScalarValues, values);
			return values;
		}

		public void Scalar_FromFaces_SimpleAverage<T>(Span<T> faceScalarValues, Span<T> outCellVals) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			for (int i = 0; i < _hmc.Mesh.CellCount; i++)
			{
				outCellVals[i] = Scalar_FromFaces_SimpleAverage(i, faceScalarValues);
			}
		}

		public T Scalar_FromFaces_SimpleAverage<T>(int cIdx, Span<T> faceScalarValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			int length = faces.Length;
			T zero = T.Zero;
			for (int i = 0; i < length; i++)
			{
				int index = faces[i];
				zero += faceScalarValues[index];
			}
			return zero / T.CreateTruncating(length);
		}

		public float Scalar_FromFaces_PlanarZ(int cIdx, Span<float> faceValues, Func<int, Point> getFaceApplicationPoint)
		{
			Cell cell = _hmc.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			PlanarRegression planarRegression = new PlanarRegression(_hmc.Mesh.CellCenters[cIdx]);
			Span<int> facesSpan = faces;
			for (int i = 0; i < facesSpan.Length; i++)
			{
				int faceIndex = facesSpan[i];
				float faceValue = faceValues[faceIndex];
				if ((double)faceValue != -9999.0)
				{
					planarRegression.Add(getFaceApplicationPoint(faceIndex), faceValue);
				}
			}
			if (planarRegression.Count == 0)
			{
				return -9999f;
			}
			return (float)planarRegression.SolveZ();
		}

		public T[] Scalar_FromFaces_AbsMaximum<T>(Span<T> faceScalarValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			T[] values = new T[_hmc.Mesh.CellCount];
			Scalar_FromFaces_AbsMaximum(faceScalarValues, values);
			return values;
		}

		public void Scalar_FromFaces_AbsMaximum<T>(Span<T> faceScalarValues, Span<T> outCellValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			for (int i = 0; i < _hmc.Mesh.CellCount; i++)
			{
				outCellValues[i] = Scalar_FromFaces_AbsMaximum(i, faceScalarValues);
			}
		}

		public T Scalar_FromFaces_AbsMaximum<T>(int cIdx, Span<T> faceScalarValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			int length = faces.Length;
			T zero = T.Zero;
			for (int i = 0; i < length; i++)
			{
				int index = faces[i];
				T absoluteFaceScalarValue = T.Abs(faceScalarValues[index]);
				if (zero < absoluteFaceScalarValue)
				{
					zero = absoluteFaceScalarValue;
				}
			}
			return zero;
		}

		public T Scalar_FromFaces_AbsMaximum<T>(int cIdx, Span<T> faceScalarValues, T NDV) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			int length = faces.Length;
			bool condition = false;
			T zero = T.Zero;
			for (int i = 0; i < length; i++)
			{
				int index = faces[i];
				T absoluteFaceScalarValue = T.Abs(faceScalarValues[index]);
				if (!(absoluteFaceScalarValue == NDV))
				{
					condition = true;
					if (zero < absoluteFaceScalarValue)
					{
						zero = absoluteFaceScalarValue;
					}
				}
			}
			if (condition)
			{
				return zero;
			}
			return NDV;
		}

		public Vector Vector_FromFaces_PerpLeastSquares_Local(int cIdx, Span<float> localFacePerpendicularVectorMagnitude)
		{
			Vector[] faceNormal = _hmc.FaceNormal;
			FaceVelocityCoef faceVelocityCoef = _hmc.CellComputes.VelocityCoef(cIdx);
			Cell cell = _hmc.Mesh.Cells[cIdx];
			double computed = 0.0;
			double computed2 = 0.0;
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Vector vector = faceNormal[faceIndex];
				float localFacePerpendicularVectorMagnitude2 = localFacePerpendicularVectorMagnitude[i];
				computed += vector.X * (double)localFacePerpendicularVectorMagnitude2;
				computed2 += vector.Y * (double)localFacePerpendicularVectorMagnitude2;
			}
			return faceVelocityCoef.Compute(computed, computed2);
		}

		public Vector Vector_FromFaces_PerpLeastSquares_Global(int cIdx, Span<float> globalFacePerpendicularVectorMagnitude)
		{
			Vector[] faceNormal = _hmc.FaceNormal;
			FaceVelocityCoef faceVelocityCoef = VelocityCoef(cIdx);
			Cell cell = _hmc.Mesh.Cells[cIdx];
			double computed = 0.0;
			double computed2 = 0.0;
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Vector vector = faceNormal[faceIndex];
				float globalFacePerpendicularVectorMagnitude2 = globalFacePerpendicularVectorMagnitude[faceIndex];
				computed += vector.X * (double)globalFacePerpendicularVectorMagnitude2;
				computed2 += vector.Y * (double)globalFacePerpendicularVectorMagnitude2;
			}
			return faceVelocityCoef.Compute(computed, computed2);
		}

		public Vector[] Vector_FromFaces_SimpleAverage(Span<Vector> faceVectors)
		{
			Vector[] vectors = new Vector[_hmc.Mesh.CellCount];
			Vector_FromFaces_SimpleAverage(faceVectors, vectors);
			return vectors;
		}

		public void Vector_FromFaces_SimpleAverage(Span<Vector> faceVectors, Span<Vector> outCellVals)
		{
			for (int i = 0; i < _hmc.Mesh.CellCount; i++)
			{
				outCellVals[i] = Vector_FromFaces_SimpleAverage(i, faceVectors);
			}
		}

		public Vector Vector_FromFaces_SimpleAverage(int cIdx, Span<Vector> faceVectors)
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			int length = faces.Length;
			Vector zero = Vector.Zero;
			for (int i = 0; i < length; i++)
			{
				int index = faces[i];
				zero += faceVectors[index];
			}
			return zero / (double)length;
		}

		public FaceVelocityCoef VelocityCoef(int cIdx)
		{
			FaceVelocityCoef result = default;
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			Vector[] faceNormal = _hmc.FaceNormal;
			Span<int> facesSpan = faces;
			for (int i = 0; i < facesSpan.Length; i++)
			{
				int faceIndex = facesSpan[i];
				result.AddFaceNormal(faceNormal[faceIndex]);
			}
			result.Complete();
			return result;
		}

		public Vector[] VelocityTimeSeries(int cIdx, Dictionary<int, Memory<float>> faceVelocities)
		{
			Memory<float> floatSpan = faceVelocities.ElementAt(0).Value;
			int length = floatSpan.Length;
			Vector[] vectors = new Vector[length];
			FaceVelocityCoef faceVelocityCoef = VelocityCoef(cIdx);
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < length; i++)
			{
				double computed = 0.0;
				double computed2 = 0.0;
				Span<int> facesSpan = faces;
				for (int j = 0; j < facesSpan.Length; j++)
				{
					int faceIndex = facesSpan[j];
					Vector vector = _hmc.FaceNormal[faceIndex];
					floatSpan = faceVelocities[faceIndex];
					float computed3 = floatSpan.Span[i];
					computed += vector.X * (double)computed3;
					computed2 += vector.Y * (double)computed3;
				}
				vectors[i] = new Vector(faceVelocityCoef.A22 * computed - faceVelocityCoef.A12 * computed2, (0.0 - faceVelocityCoef.A12) * computed + faceVelocityCoef.A11 * computed2);
			}
			return vectors;
		}

		public void PerotVelocity(Span<double> faceVel, Span<Vector> cellVel, List<int> cellIndexes, int start, int stop)
		{
			Span<int> cellIndexesSpan = CollectionsMarshal.AsSpan(cellIndexes);
			PerotVelocity(cellIndexesSpan.Slice(start, stop + 1 - start), faceVel, cellVel);
		}

		public void PerotVelocity(Span<int> cells, Span<double> faceVel, Span<Vector> cellVel)
		{
			Span<Vector2> perotUVs = _hmc.PerotUV;
			Mesh mesh = _hmc.Mesh;
			int[] cellFaceStartIndex = _hmc.CellFaceStartIndex;
			for (int i = 0; i < cells.Length; i++)
			{
				int cellIndex = cells[i];
				int length = mesh.Cells[cellIndex].Faces.Span.Length;
				int start = cellFaceStartIndex[cellIndex];
				cellVel[cellIndex] = PerotVelocity(cellIndex, faceVel, perotUVs.Slice(start, length));
			}
		}

		public void PerotVelocity(Span<double> faceVel, Span<Vector> cellVel)
		{
			Span<Vector2> perotUVs = _hmc.PerotUV;
			Mesh mesh = _hmc.Mesh;
			int lengthSum = 0;
			for (int i = 0; i < mesh.CellCount; i++)
			{
				int length = mesh.Cells[i].Faces.Span.Length;
				cellVel[i] = PerotVelocity(i, faceVel, perotUVs.Slice(lengthSum, length));
				lengthSum += length;
			}
		}

		private Vector PerotVelocity(int cIdx, Span<double> globalFaceNormalVelocities, Span<Vector2> localPerotUVFaces)
		{
			Span<int> faces = _hmc.Mesh.Cells[cIdx].Faces.Span;
			int length = faces.Length;
			double xComponent = 0.0;
			double yComponent = 0.0;
			for (int i = 0; i < length; i++)
			{
				Vector2 vector = localPerotUVFaces[i];
				int index = faces[i];
				double globalFaceNormalVelocity = globalFaceNormalVelocities[index];
				xComponent += (double)vector.X * globalFaceNormalVelocity;
				yComponent += (double)vector.Y * globalFaceNormalVelocity;
			}
			return new Vector(xComponent, yComponent);
		}

		public void VelocityWLS(Span<double> faceArea, Span<double> faceVel, Span<Vector> cellVel)
		{
			for (int i = 0; i < _hmc.CellCount; i++)
			{
				cellVel[i] = VelocityWLS(i, faceArea, faceVel);
			}
		}

		public void VelocityWLS(Span<double> faceArea, Span<double> faceVel, Span<Vector> cellVel, List<int> cellList, int start, int stop)
		{
			Span<int> cellListSpan = CollectionsMarshal.AsSpan(cellList);
			VelocityWLS(cellListSpan.Slice(start, stop + 1 - start), faceArea, faceVel, cellVel);
		}

		public void VelocityWLS(Span<double> faceArea, Span<double> faceVel, Span<Vector> cellVel, Vector[,] localFaceNorm, List<int> cellList, int start, int stop)
		{
			Span<int> cellListSpan = CollectionsMarshal.AsSpan(cellList);
			VelocityWLS(cellListSpan.Slice(start, stop + 1 - start), faceArea, faceVel, cellVel, localFaceNorm);
		}

		public void VelocityWLS(Span<int> cellIndexes, Span<double> faceArea, Span<double> faceVel, Span<Vector> cellVel)
		{
			Span<int> cellIndexesSpan = cellIndexes;
			for (int i = 0; i < cellIndexesSpan.Length; i++)
			{
				int cellIndex = cellIndexesSpan[i];
				cellVel[cellIndex] = VelocityWLS(cellIndex, faceArea, faceVel);
			}
		}

		public void VelocityWLS(Span<int> cellIndexes, Span<double> faceArea, Span<double> faceVel, Span<Vector> cellVel, Vector[,] localFaceNorm)
		{
			Span<int> cellIndexesSpan = cellIndexes;
			for (int i = 0; i < cellIndexesSpan.Length; i++)
			{
				int cellIndex = cellIndexesSpan[i];
				cellVel[cellIndex] = VelocityWLS(cellIndex, faceArea, faceVel, localFaceNorm);
			}
		}

		public Vector VelocityWLS(int cIdx, Span<double> faceArea, Span<double> faceVel, int excludeFIdx = -1)
		{
			double cellAInv = _hmc.CellAInv[cIdx];
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			Vector[] faceNormal = _hmc.FaceNormal;
			Span<int> facesSpan = faces;
			for (int i = 0; i < facesSpan.Length; i++)
			{
				int faceIndex = facesSpan[i];
				if (faceIndex != excludeFIdx)
				{
					Vector vector = faceNormal[faceIndex];
					double faceVel2 = faceVel[faceIndex];
					double deltaX = vector.DeltaX;
					double deltaY = vector.DeltaY;
					double faceAreaFaceIndexTerm = Math.Max(faceArea[faceIndex] * cellAInv, 0.001);
					double deltaXTerm = faceAreaFaceIndexTerm * deltaX;
					double deltaYTerm = faceAreaFaceIndexTerm * deltaY;
					computed += deltaXTerm * deltaX;
					computed2 += deltaXTerm * deltaY;
					computed3 += deltaYTerm * deltaY;
					computed4 += deltaXTerm * faceVel2;
					computed5 += deltaYTerm * faceVel2;
				}
			}
			double computed6 = computed * computed3 - computed2 * computed2;
			computed6 = 1.0 / Math.Max(computed6, 1E-10);
			return new Vector((computed3 * computed4 - computed2 * computed5) * computed6, (computed * computed5 - computed2 * computed4) * computed6);
		}

		public Vector VelocityWLS(int cIdx, Span<double> faceArea, Span<double> faceVel, Vector[,] localFaceNorm)
		{
			double cellAInv = _hmc.CellAInv[cIdx];
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Vector vector = localFaceNorm[faceIndex, (_hmc.Mesh.Faces[faceIndex].cellA != cIdx) ? 1u : 0u];
				double faceVel2 = faceVel[faceIndex];
				double deltaX = vector.DeltaX;
				double deltaY = vector.DeltaY;
				double faceAreaFaceIndexTerm = Math.Max(faceArea[faceIndex] * cellAInv, 0.001);
				double deltaXTerm = faceAreaFaceIndexTerm * deltaX;
				double deltaYTerm = faceAreaFaceIndexTerm * deltaY;
				computed += deltaXTerm * deltaX;
				computed2 += deltaXTerm * deltaY;
				computed3 += deltaYTerm * deltaY;
				computed4 += deltaXTerm * faceVel2;
				computed5 += deltaYTerm * faceVel2;
			}
			double computed6 = computed * computed3 - computed2 * computed2;
			computed6 = 1.0 / Math.Max(computed6, 1E-10);
			return new Vector((computed3 * computed4 - computed2 * computed5) * computed6, (computed * computed5 - computed2 * computed4) * computed6);
		}

		public void GradientXY(Span<double> faceValuesAB, Span<double> cellVol, Span<Vector> outCellGradient)
		{
			if (faceValuesAB.Length != _hmc.Mesh.FaceCount * 2)
			{
				throw new ArgumentException("faceValuesAB wrong size.");
			}
			Span<FaceAB> faceValsLR = MemoryMarshal.Cast<double, FaceAB>(faceValuesAB);
			GradientXY(faceValsLR, cellVol, outCellGradient);
		}

		public void GradientXY(Span<FaceAB> faceValsLR, Span<double> cellVol, Span<Vector> outCellGradient)
		{
			for (int i = 0; i < _hmc.Mesh.CellCount; i++)
			{
				if (cellVol[i] == 0.0)
				{
					outCellGradient[i] = Vector.Zero;
				}
				else
				{
					outCellGradient[i] = GradientXY(i, faceValsLR);
				}
			}
		}

		public Vector GradientXY(int cIdx, Span<FaceAB> faceValsLR)
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			if (faces.Length < 3)
			{
				return Vector.Zero;
			}
			double xComponent = 0.0;
			double yComponent = 0.0;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Segment segment = _hmc.Mesh.FaceSegment(faceIndex);
				bool cellAFacesTerm = _hmc.Mesh.Faces[faceIndex].cellA == cIdx;
				int ordinal = ((!cellAFacesTerm) ? 1 : 0);
				double deltaXSegmentTerm = (double)(1 - 2 * ordinal) * segment.DeltaX;
				double deltaYSegmentTerm = (double)(1 - 2 * ordinal) * segment.DeltaY;
				FaceAB faceAB = faceValsLR[faceIndex];
				double valueB = ((!cellAFacesTerm) ? faceAB.ValueB : faceAB.ValueA);
				xComponent += valueB * deltaYSegmentTerm;
				yComponent -= valueB * deltaXSegmentTerm;
			}
			double cellAInv = _hmc.CellAInv[cIdx];
			xComponent *= cellAInv;
			yComponent *= cellAInv;
			return new Vector(xComponent, yComponent);
		}

		public void GradientUVXY(Span<FaceUVAB> faceUVLR, Span<double> cellVol, Span<GradientUVXY> outCellGradUVXY)
		{
			if (outCellGradUVXY.Length != _hmc.Mesh.CellCount)
			{
				throw new ArgumentException("outCellGradUVXY wrong size.");
			}
			for (int i = 0; i < _hmc.Mesh.CellCount; i++)
			{
				if (cellVol[i] == 0.0)
				{
					outCellGradUVXY[i] = Geospatial.Vectors.GradientUVXY.Zero;
				}
				else
				{
					outCellGradUVXY[i] = GradientUVXY(i, faceUVLR);
				}
			}
		}

		public void GradientUVXY(List<int> cellIndices, Span<FaceUVAB> faceUVLR, Span<double> cellVol, Span<GradientUVXY> outCellGradUVXY)
		{
			GradientUVXY(CollectionsMarshal.AsSpan(cellIndices), faceUVLR, cellVol, outCellGradUVXY);
		}

		public void GradientUVXY(Span<int> cellIndices, Span<FaceUVAB> faceUVAB, Span<double> cellVol, Span<GradientUVXY> outCellGradUVXY)
		{
			if (outCellGradUVXY.Length != cellIndices.Length)
			{
				throw new ArgumentException("outCellGradUVXY wrong size.");
			}
			for (int i = 0; i < cellIndices.Length; i++)
			{
				int cellIndex = cellIndices[i];
				if (cellVol[cellIndex] == 0.0)
				{
					outCellGradUVXY[cellIndex] = Geospatial.Vectors.GradientUVXY.Zero;
				}
				else
				{
					outCellGradUVXY[cellIndex] = GradientUVXY(cellIndex, faceUVAB);
				}
			}
		}

		public GradientUVXY GradientUVXY(int cIdx, Span<FaceUVAB> faceUVLR)
		{
			Mesh mesh = _hmc.Mesh;
			double[] cellAInv = _hmc.CellAInv;
			Cell cell = mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			if (faces.Length < 3)
			{
				return Geospatial.Vectors.GradientUVXY.Zero;
			}
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Segment segment = mesh.FaceSegment(faceIndex);
				bool cellAFacesTerm = mesh.Faces[faceIndex].cellA == cIdx;
				int ordinal = ((!cellAFacesTerm) ? 1 : 0);
				double deltaXSegmentTerm = (double)(1 - 2 * ordinal) * segment.DeltaX;
				double deltaYSegmentTerm = (double)(1 - 2 * ordinal) * segment.DeltaY;
				FaceUVAB faceUVAB = faceUVLR[faceIndex];
				double uaFaceUVABTerm;
				double vaFaceUVABTerm;
				if (cellAFacesTerm)
				{
					uaFaceUVABTerm = faceUVAB.UA;
					vaFaceUVABTerm = faceUVAB.VA;
				}
				else
				{
					uaFaceUVABTerm = faceUVAB.UB;
					vaFaceUVABTerm = faceUVAB.VB;
				}
				computed += uaFaceUVABTerm * deltaYSegmentTerm;
				computed2 -= uaFaceUVABTerm * deltaXSegmentTerm;
				computed3 += vaFaceUVABTerm * deltaYSegmentTerm;
				computed4 -= vaFaceUVABTerm * deltaXSegmentTerm;
			}
			double cellAInv2 = cellAInv[cIdx];
			return new GradientUVXY(computed * cellAInv2, computed2 * cellAInv2, computed3 * cellAInv2, computed4 * cellAInv2);
		}

		public void StrainRate(Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates)
		{
			StrainRate(faceVelNorm, faceVelTangA, faceVelTangB, cellVol, outCellStrainRates, default);
		}

		public void StrainRate(List<int> cellIndices, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates)
		{
			StrainRate(CollectionsMarshal.AsSpan(cellIndices), faceVelNorm, faceVelTangA, faceVelTangB, cellVol, outCellStrainRates, default);
		}

		public void StrainRate(Span<int> cellIndexes, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates)
		{
			StrainRate(cellIndexes, faceVelNorm, faceVelTangA, faceVelTangB, cellVol, outCellStrainRates, default);
		}

		public void StrainRate(Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates, Span<GradientUVXY> outCellGradients)
		{
			for (int i = 0; i < _hmc.Mesh.CellCount; i++)
			{
				StrainRateOne(i, faceVelNorm, faceVelTangA, faceVelTangB, cellVol, outCellStrainRates, outCellGradients);
			}
		}

		public void StrainRate(List<int> cellIndices, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates, Span<GradientUVXY> outCellGradients)
		{
			StrainRate(CollectionsMarshal.AsSpan(cellIndices), faceVelNorm, faceVelTangA, faceVelTangB, cellVol, outCellStrainRates, outCellGradients);
		}

		public void StrainRate(Span<int> cellIndexes, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates, Span<GradientUVXY> outCellGradients)
		{
			Span<int> cellIndexesSpan = cellIndexes;
			for (int i = 0; i < cellIndexesSpan.Length; i++)
			{
				int cIdx = cellIndexesSpan[i];
				StrainRateOne(cIdx, faceVelNorm, faceVelTangA, faceVelTangB, cellVol, outCellStrainRates, outCellGradients);
			}
		}

		private void StrainRateOne(int cIdx, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB, Span<double> cellVol, Span<float> outCellStrainRates, Span<GradientUVXY> outCellGradients)
		{
			if (cellVol[cIdx] == 0.0)
			{
				if (!outCellStrainRates.IsEmpty)
				{
					outCellStrainRates[cIdx] = 0f;
				}
				if (!outCellGradients.IsEmpty)
				{
					outCellGradients[cIdx] = Geospatial.Vectors.GradientUVXY.Zero;
				}
				return;
			}
			GradientUVXY grad = VelocityGradientGG(cIdx, faceVelNorm, faceVelTangA, faceVelTangB);
			if (!outCellStrainRates.IsEmpty)
			{
				outCellStrainRates[cIdx] = (float)StrainRate(in grad);
			}
			if (!outCellGradients.IsEmpty)
			{
				outCellGradients[cIdx] = grad;
			}
		}

		public double StrainRate(int cIdx, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB)
		{
			return StrainRate(VelocityGradientGG(cIdx, faceVelNorm, faceVelTangA, faceVelTangB));
		}

		public static double StrainRate(in GradientUVXY grad)
		{
			return Math.Sqrt(2.0 * grad.DU_DX * grad.DU_DX + 2.0 * grad.DV_DY * grad.DV_DY + Math.Pow(grad.DU_DY + grad.DV_DX, 2.0));
		}

		public GradientUVXY VelocityGradientGG(int cIdx, Span<double> faceVelNorm, double[] faceVelTangA, double[] faceVelTangB)
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			if (faces.Length < 3)
			{
				return Geospatial.Vectors.GradientUVXY.Zero;
			}
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				Segment segment = _hmc.Mesh.FaceSegment(faceIndex);
				int cellAFacesTerm = ((_hmc.Mesh.Faces[faceIndex].cellA != cIdx) ? 1 : 0);
				double deltaXSegmentTerm = (double)(1 - 2 * cellAFacesTerm) * segment.DeltaX;
				double deltaYSegmentTerm = (double)(1 - 2 * cellAFacesTerm) * segment.DeltaY;
				double fVelTang = ((cellAFacesTerm == 0) ? faceVelTangA[faceIndex] : faceVelTangB[faceIndex]);
				ComputeFaceVelocityXY(in faceVelNorm[faceIndex], in fVelTang, in _hmc.FaceNormal[faceIndex], out var fVelX, out var fVelY);
				computed += fVelX * deltaYSegmentTerm;
				computed2 -= fVelX * deltaXSegmentTerm;
				computed3 += fVelY * deltaYSegmentTerm;
				computed4 -= fVelY * deltaXSegmentTerm;
			}
			double cellAInv = _hmc.CellAInv[cIdx];
			return new GradientUVXY(computed * cellAInv, computed2 * cellAInv, computed3 * cellAInv, computed4 * cellAInv);
		}

		public Vector ScalarGradientWLS(int cIdx, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes)
		{
			if (!IsWet(cellVolumes[cIdx]))
			{
				return Vector.Zero;
			}
			_hmc.EnsureScalarGradientWlsConnectivity();
			SparseMatrix<Vector> scalarGradientWlsStructure = _hmc._scalarGradientWlsStructure;
			int[] rowPointers = scalarGradientWlsStructure.RowPointers;
			int[] columnIndices = scalarGradientWlsStructure.ColumnIndices;
			int rowPointerStart = rowPointers[cIdx];
			int rowPointerEnd = rowPointers[cIdx + 1];
			Point point = _hmc.Mesh.CellCenters[cIdx];
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			int ordinal = 0;
			for (int i = rowPointerStart; i < rowPointerEnd; i++)
			{
				int columnIndex = columnIndices[i];
				if (IsWet(cellVolumes[columnIndex]))
				{
					Point point2 = _hmc.Mesh.CellCenters[columnIndex];
					double point2PointTerm = point2.X - point.X;
					double point2PointTerm2 = point2.Y - point.Y;
					double computed6 = point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2;
					if (!(computed6 <= 0.0))
					{
						double computed7 = 1.0 / computed6;
						double computed8 = computed7 * point2PointTerm;
						double computed9 = computed7 * point2PointTerm2;
						double cellValueStep = cellValues[columnIndex] - cellValues[cIdx];
						computed += computed8 * point2PointTerm;
						computed2 += computed8 * point2PointTerm2;
						computed3 += computed9 * point2PointTerm2;
						computed4 += computed8 * cellValueStep;
						computed5 += computed9 * cellValueStep;
						ordinal++;
					}
				}
			}
			double computed10 = computed + computed3;
			double computed11 = computed * computed3 - computed2 * computed2;
			double computed12 = 1E-10 * computed10 * computed10;
			if (ordinal < 2 || !double.IsFinite(computed10) || !double.IsFinite(computed11) || computed11 <= computed12)
			{
				return Vector.Zero;
			}
			double computed13 = 1.0 / computed11;
			return new Vector((computed3 * computed4 - computed2 * computed5) * computed13, (computed * computed5 - computed2 * computed4) * computed13);
		}

		private static bool IsWet(double cellVolume)
		{
			if (cellVolume > 0.0)
			{
				return double.IsFinite(cellVolume);
			}
			return false;
		}

		public double SlopeLimiter(int cIdx, Vector gradient, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, MultidimensionalSlopeLimiter limiterType)
		{
			if (limiterType == MultidimensionalSlopeLimiter.None)
			{
				return 1.0;
			}
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			double cellValue = cellValues[cIdx];
			Point point = _hmc.Mesh.CellCenters[cIdx];
			double cellValue2 = cellValue;
			double cellValue3 = cellValue;
			_hmc.EnsureScalarGradientWlsConnectivity();
			SparseMatrix<Vector> scalarGradientWlsStructure = _hmc._scalarGradientWlsStructure;
			int[] rowPointers = scalarGradientWlsStructure.RowPointers;
			int[] columnIndices = scalarGradientWlsStructure.ColumnIndices;
			int rowPointerStart = rowPointers[cIdx];
			int rowPointerEnd = rowPointers[cIdx + 1];
			for (int i = rowPointerStart; i < rowPointerEnd; i++)
			{
				int index = columnIndices[i];
				if (IsWet(cellVolumes[index]))
				{
					double cellValue4 = cellValues[index];
					if (cellValue4 < cellValue2)
					{
						cellValue2 = cellValue4;
					}
					if (cellValue4 > cellValue3)
					{
						cellValue3 = cellValue4;
					}
				}
			}
			double deltaX = gradient.DeltaX;
			double deltaY = gradient.DeltaY;
			double faceLimiterTerm = 1.0;
			Span<int> facesSpan = faces;
			for (int j = 0; j < facesSpan.Length; j++)
			{
				int fIdx = facesSpan[j];
				Point point2 = _hmc.Mesh.FaceSegment(fIdx).Midpoint();
				double deltaXPoint2Term = deltaX * (point2.X - point.X) + deltaY * (point2.Y - point.Y);
				double faceLimiter;
				if (deltaXPoint2Term > 0.0)
				{
					faceLimiter = FaceLimiter((cellValue3 - cellValue) / deltaXPoint2Term, limiterType);
				}
				else
				{
					faceLimiter = ((!(deltaXPoint2Term < 0.0)) ? 1.0 : FaceLimiter((cellValue2 - cellValue) / deltaXPoint2Term, limiterType));
				}
				if (faceLimiter < faceLimiterTerm)
				{
					faceLimiterTerm = faceLimiter;
				}
			}
			return faceLimiterTerm;
		}

		private static double FaceLimiter(double ratio, MultidimensionalSlopeLimiter limiterType)
		{
			return limiterType switch
			{
				MultidimensionalSlopeLimiter.BarthJespersen => Math.Min(1.0, ratio), 
				MultidimensionalSlopeLimiter.SmoothCubic => SmoothCubic(ratio), 
				_ => throw new ArgumentOutOfRangeException("limiterType"), 
			};
		}

		public double BarthJespersen(int cIdx, Vector gradient, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes)
		{
			return SlopeLimiter(cIdx, gradient, cellValues, cellVolumes, MultidimensionalSlopeLimiter.BarthJespersen);
		}

		public Vector ScalarGradientWLSLimited(int cIdx, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, out double limiter)
		{
			return ScalarGradientWLSLimited(cIdx, cellValues, cellVolumes, MultidimensionalSlopeLimiter.BarthJespersen, out limiter);
		}

		public Vector ScalarGradientWLSLimited(int cIdx, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, MultidimensionalSlopeLimiter limiterType, out double limiter)
		{
			Vector gradient = ScalarGradientWLS(cIdx, cellValues, cellVolumes);
			limiter = SlopeLimiter(cIdx, gradient, cellValues, cellVolumes, limiterType);
			return new Vector(gradient.DeltaX * limiter, gradient.DeltaY * limiter);
		}

		public void ScalarGradientWLS(ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, Span<Vector> outGradient, bool applyBarthJespersen)
		{
			ScalarGradientWLS(cellValues, cellVolumes, outGradient, applyBarthJespersen ? MultidimensionalSlopeLimiter.BarthJespersen : MultidimensionalSlopeLimiter.None);
		}

		public void ScalarGradientWLS(ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, Span<Vector> outGradient, MultidimensionalSlopeLimiter limiterType)
		{
			for (int i = 0; i < _hmc.CellCount; i++)
			{
				Vector vector = ScalarGradientWLS(i, cellValues, cellVolumes);
				if (limiterType != MultidimensionalSlopeLimiter.None)
				{
					double slopeLimiter = SlopeLimiter(i, vector, cellValues, cellVolumes, limiterType);
					vector = new Vector(vector.DeltaX * slopeLimiter, vector.DeltaY * slopeLimiter);
				}
				outGradient[i] = vector;
			}
		}

		public Vector VelocityGradWLS(int cIdx, Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, out GradientUVXY gradient)
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			if (faces.Length < 3)
			{
				gradient = Geospatial.Vectors.GradientUVXY.Zero;
				return Vector.Zero;
			}
			Span<double> m = stackalloc double[36];
			Span<double> b = stackalloc double[6];
			Span<double> doubleSpan = stackalloc double[6];
			Span<double> doubleSpan2 = stackalloc double[6];
			double cellAInv = _hmc.CellAInv[cIdx];
			Point point = _hmc.Mesh.CellCenters[cIdx];
			Vector[] faceNormal = _hmc.FaceNormal;
			Span<int> facesSpan = faces;
			for (int i = 0; i < facesSpan.Length; i++)
			{
				int faceIndex = facesSpan[i];
				int cellAFacesTerm = ((_hmc.Mesh.Faces[faceIndex].cellA != cIdx) ? 1 : 0);
				Vector vector = faceNormal[faceIndex];
				double deltaX = vector.DeltaX;
				double deltaY = vector.DeltaY;
				Point point2 = _hmc.Mesh.FaceSegment(faceIndex).Midpoint();
				double point2PointTerm = point2.X - point.X;
				double point2PointTerm2 = point2.Y - point.Y;
				double faceAreaFaceIndexTerm = Math.Max(faceArea[faceIndex] * cellAInv, 0.001);
				double faceVelNorm2 = faceVelNorm[faceIndex];
				double faceVelTangFaceIndexTerm = faceVelTang[faceIndex, cellAFacesTerm];
				doubleSpan[0] = deltaX;
				doubleSpan[1] = deltaY;
				doubleSpan[2] = deltaX * point2PointTerm;
				doubleSpan[3] = deltaX * point2PointTerm2;
				doubleSpan[4] = deltaY * point2PointTerm;
				doubleSpan[5] = deltaY * point2PointTerm2;
				doubleSpan2[0] = 0.0 - deltaY;
				doubleSpan2[1] = deltaX;
				doubleSpan2[2] = (0.0 - deltaY) * point2PointTerm;
				doubleSpan2[3] = (0.0 - deltaY) * point2PointTerm2;
				doubleSpan2[4] = deltaX * point2PointTerm;
				doubleSpan2[5] = deltaX * point2PointTerm2;
				for (int j = 0; j < 6; j++)
				{
					for (int k = 0; k <= j; k++)
					{
						m[j * 6 + k] += faceAreaFaceIndexTerm * (doubleSpan[j] * doubleSpan[k] + doubleSpan2[j] * doubleSpan2[k]);
					}
					b[j] += faceAreaFaceIndexTerm * (doubleSpan[j] * faceVelNorm2 + doubleSpan2[j] * faceVelTangFaceIndexTerm);
				}
			}
			Span<double> x = stackalloc double[6];
			Cholesky<double>.Solve(m, 6, x, b, 1E-10);
			gradient = new GradientUVXY(x[2], x[3], x[4], x[5]);
			return new Vector(x[0], x[1]);
		}

		public Vector VelocityGradQR(int cIdx, Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, out GradientUVXY gradient)
		{
			Cell cell = _hmc.Mesh.Cells[cIdx];
			Span<int> faces = cell.Faces.Span;
			int length = faces.Length;
			if (length < 3)
			{
				gradient = Geospatial.Vectors.GradientUVXY.Zero;
				return Vector.Zero;
			}
			int nRows = 2 * length;
			Span<double> a = stackalloc double[96];
			Span<double> b = stackalloc double[16];
			double cellAInv = _hmc.CellAInv[cIdx];
			Point point = _hmc.Mesh.CellCenters[cIdx];
			Vector[] faceNormal = _hmc.FaceNormal;
			for (int i = 0; i < length; i++)
			{
				int faceIndex = faces[i];
				int cellAFacesTerm = ((_hmc.Mesh.Faces[faceIndex].cellA != cIdx) ? 1 : 0);
				Vector vector = faceNormal[faceIndex];
				double deltaX = vector.DeltaX;
				double deltaY = vector.DeltaY;
				Point point2 = _hmc.Mesh.FaceSegment(faceIndex).Midpoint();
				double point2PointTerm = point2.X - point.X;
				double point2PointTerm2 = point2.Y - point.Y;
				double faceAreaFaceIndexTerm = Math.Sqrt(Math.Max(faceArea[faceIndex] * cellAInv, 0.001));
				int ordinal = 2 * i;
				a[ordinal * 6] = faceAreaFaceIndexTerm * deltaX;
				a[ordinal * 6 + 1] = faceAreaFaceIndexTerm * deltaY;
				a[ordinal * 6 + 2] = faceAreaFaceIndexTerm * deltaX * point2PointTerm;
				a[ordinal * 6 + 3] = faceAreaFaceIndexTerm * deltaX * point2PointTerm2;
				a[ordinal * 6 + 4] = faceAreaFaceIndexTerm * deltaY * point2PointTerm;
				a[ordinal * 6 + 5] = faceAreaFaceIndexTerm * deltaY * point2PointTerm2;
				b[ordinal] = faceAreaFaceIndexTerm * faceVelNorm[faceIndex];
				ordinal = 2 * i + 1;
				a[ordinal * 6] = faceAreaFaceIndexTerm * (0.0 - deltaY);
				a[ordinal * 6 + 1] = faceAreaFaceIndexTerm * deltaX;
				a[ordinal * 6 + 2] = faceAreaFaceIndexTerm * (0.0 - deltaY) * point2PointTerm;
				a[ordinal * 6 + 3] = faceAreaFaceIndexTerm * (0.0 - deltaY) * point2PointTerm2;
				a[ordinal * 6 + 4] = faceAreaFaceIndexTerm * deltaX * point2PointTerm;
				a[ordinal * 6 + 5] = faceAreaFaceIndexTerm * deltaX * point2PointTerm2;
				b[ordinal] = faceAreaFaceIndexTerm * faceVelTang[faceIndex, cellAFacesTerm];
			}
			Span<double> x = stackalloc double[6];
			QR<double>.Solve(a, nRows, 6, x, b, 1E-10);
			gradient = new GradientUVXY(x[2], x[3], x[4], x[5]);
			return new Vector(x[0], x[1]);
		}

		public void VelocityGradWLS(Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, Span<Vector> cellVel, Span<GradientUVXY> cellGrad)
		{
			for (int i = 0; i < _hmc.CellCount; i++)
			{
				cellVel[i] = VelocityGradWLS(i, faceArea, faceVelNorm, faceVelTang, out var gradient);
				cellGrad[i] = gradient;
			}
		}

		public void VelocityGradWLS(Span<int> cellIndexes, Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, Span<Vector> cellVel, Span<GradientUVXY> cellGrad)
		{
			Span<int> cellIndexesSpan = cellIndexes;
			for (int i = 0; i < cellIndexesSpan.Length; i++)
			{
				int cellIndex = cellIndexesSpan[i];
				cellVel[cellIndex] = VelocityGradWLS(cellIndex, faceArea, faceVelNorm, faceVelTang, out var gradient);
				cellGrad[cellIndex] = gradient;
			}
		}

		public void VelocityGradWLS(List<int> cellList, int start, int stop, Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, Span<Vector> cellVel, Span<GradientUVXY> cellGrad)
		{
			Span<int> cellListSpan = CollectionsMarshal.AsSpan(cellList);
			VelocityGradWLS(cellListSpan.Slice(start, stop + 1 - start), faceArea, faceVelNorm, faceVelTang, cellVel, cellGrad);
		}

		public void VelocityGradQR(Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, Span<Vector> cellVel, Span<GradientUVXY> cellGrad)
		{
			for (int i = 0; i < _hmc.CellCount; i++)
			{
				cellVel[i] = VelocityGradQR(i, faceArea, faceVelNorm, faceVelTang, out var gradient);
				cellGrad[i] = gradient;
			}
		}

		public void VelocityGradQR(Span<int> cellIndexes, Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, Span<Vector> cellVel, Span<GradientUVXY> cellGrad)
		{
			Span<int> cellIndexesSpan = cellIndexes;
			for (int i = 0; i < cellIndexesSpan.Length; i++)
			{
				int cellIndex = cellIndexesSpan[i];
				cellVel[cellIndex] = VelocityGradQR(cellIndex, faceArea, faceVelNorm, faceVelTang, out var gradient);
				cellGrad[cellIndex] = gradient;
			}
		}

		public void VelocityGradQR(List<int> cellList, int start, int stop, Span<double> faceArea, Span<double> faceVelNorm, double[,] faceVelTang, Span<Vector> cellVel, Span<GradientUVXY> cellGrad)
		{
			Span<int> cellListSpan = CollectionsMarshal.AsSpan(cellList);
			VelocityGradQR(cellListSpan.Slice(start, stop + 1 - start), faceArea, faceVelNorm, faceVelTang, cellVel, cellGrad);
		}
	}

	public sealed class ScalarBoundaryReconstructionWorkspace
	{
		private const double SolveTolerance = 1E-13;

		private readonly HydraulicMeshComputer _hmc;

		private readonly ScalarBoundaryConditionType[] _types;

		private readonly double[] _values;

		private readonly List<int> _activeFaces = new List<int>();

		private readonly List<int> _affectedCells = new List<int>();

		private readonly List<int> _affectedNodes = new List<int>();

		private readonly bool[] _cellAffected;

		private readonly bool[] _nodeAffected;

		public IReadOnlyList<int> ActiveFaces => _activeFaces;

		public bool HasConditions => _activeFaces.Count > 0;

		internal ScalarBoundaryReconstructionWorkspace(HydraulicMeshComputer hmc)
		{
			_hmc = hmc;
			_types = new ScalarBoundaryConditionType[hmc.Mesh.FaceCount];
			_values = new double[hmc.Mesh.FaceCount];
			_cellAffected = new bool[hmc.Mesh.CellCount];
			_nodeAffected = new bool[hmc.Mesh.NodeCount];
		}

		public void Clear()
		{
			foreach (int activeFace in _activeFaces)
			{
				_types[activeFace] = ScalarBoundaryConditionType.None;
			}
			foreach (int affectedCell in _affectedCells)
			{
				_cellAffected[affectedCell] = false;
			}
			foreach (int affectedNode in _affectedNodes)
			{
				_nodeAffected[affectedNode] = false;
			}
			_activeFaces.Clear();
			_affectedCells.Clear();
			_affectedNodes.Clear();
		}

		public void SetValue(int face, double assignedValue)
		{
			SetCondition(face, ScalarBoundaryConditionType.Value, assignedValue);
		}

		public void SetNormalGradient(int face, double outwardGradient)
		{
			SetCondition(face, ScalarBoundaryConditionType.NormalGradient, outwardGradient);
		}

		public bool TryGetCondition(int face, out ScalarBoundaryConditionType type, out double doubleValue)
		{
			type = _types[face];
			doubleValue = _values[face];
			return type != ScalarBoundaryConditionType.None;
		}

		public void CopyFrom(ScalarBoundaryReconstructionWorkspace source)
		{
			Clear();
			if (source == null)
			{
				return;
			}
			foreach (int activeFace in source._activeFaces)
			{
				SetCondition(activeFace, source._types[activeFace], source._values[activeFace]);
			}
		}

		public void ApplyCellGradientOverrides(ScalarGradientWlsWorkspace gradientWorkspace, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, Span<Vector> gradients, MultidimensionalSlopeLimiter limiterType)
		{
			foreach (int affectedCell in _affectedCells)
			{
				gradients[affectedCell] = gradientWorkspace.ScalarGradientLimitedWithBoundary(affectedCell, cellValues, cellVolumes, this, limiterType, gradients[affectedCell]);
			}
		}

		public void ApplyCellGradientAndLimiterOverrides(ScalarGradientWlsWorkspace gradientWorkspace, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, Span<Vector> gradients, Span<double> gradientLimiters, MultidimensionalSlopeLimiter limiterType)
		{
			foreach (int affectedCell in _affectedCells)
			{
				Vector vector = gradientWorkspace.ScalarGradientWithBoundary(affectedCell, cellValues, cellVolumes, this, limiterType, gradients[affectedCell], gradientLimiters[affectedCell], out var limiter);
				gradients[affectedCell] = vector;
				gradientLimiters[affectedCell] = limiter;
			}
		}

		public void ApplyNodeFaceValueOverrides(ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, NodeFaceInterpolationWorkspace interpolation, Span<double> nodeFaceValues)
		{
			foreach (int affectedNode in _affectedNodes)
			{
				NodeFanInterpolation nodeFanInterpolation = interpolation.NodeInterpolation(affectedNode);
				int nodeFaceOffset = interpolation.NodeFaceOffset(affectedNode);
				for (int i = 0; i < nodeFanInterpolation.Fans.Length; i++)
				{
					double naN = double.NaN;
					for (int j = 0; j < nodeFanInterpolation.FaceFans.Length; j++)
					{
						if (nodeFanInterpolation.FaceFans[j] == i)
						{
							naN = nodeFaceValues[nodeFaceOffset + j];
							break;
						}
					}
					if (!double.IsFinite(naN))
					{
						continue;
					}
					double nodeFaceValues2 = ConstrainedNodeFaceValue(affectedNode, i, nodeFanInterpolation, cellValues, cellVolumes, naN);
					for (int k = 0; k < nodeFanInterpolation.FaceFans.Length; k++)
					{
						if (nodeFanInterpolation.FaceFans[k] == i)
						{
							nodeFaceValues[nodeFaceOffset + k] = nodeFaceValues2;
						}
					}
				}
			}
		}

		public double FaceNormalGradient(int face, FaceNormalGradientMethod method, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
		{
			return FaceNormalGradient(face, method, cellValues, cellGradients, null, nodeFaceValues, nodeInterpolation, inversePerpendicularDistance);
		}

		public double FaceNormalGradient(int face, FaceNormalGradientMethod method, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
		{
			Face face2 = _hmc.Mesh.Faces[face];
			return _types[face] switch
			{
				ScalarBoundaryConditionType.NormalGradient => _values[face], 
				ScalarBoundaryConditionType.Value => (_values[face] - cellValues[face2.cellA]) * inversePerpendicularDistance + FaceNormalGradientCorrection(face, method, cellGradients, cellGradientLimiters, nodeFaceValues, nodeInterpolation, inversePerpendicularDistance), 
				_ => 0.0, 
			};
		}

		public double FaceNormalGradientCorrection(int face, FaceNormalGradientMethod method, Vector[] cellGradients, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
		{
			return FaceNormalGradientCorrection(face, method, cellGradients, null, nodeFaceValues, nodeInterpolation, inversePerpendicularDistance);
		}

		public double FaceNormalGradientCorrection(int face, FaceNormalGradientMethod method, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
		{
			if (_types[face] != ScalarBoundaryConditionType.Value || method == FaceNormalGradientMethod.TwoPoint)
			{
				return 0.0;
			}
			Face face2 = _hmc.Mesh.Faces[face];
			if (method == FaceNormalGradientMethod.CompactFaceBased)
			{
				if (!_hmc.FaceComputes.CompactFaceValuesAreValid(face, nodeFaceValues, nodeInterpolation))
				{
					return 0.0;
				}
				return _hmc.NormalGradientGeometry.CompactCorrectionCoefficient[face] * (nodeFaceValues[nodeInterpolation.FaceValueIndexB(face)] - nodeFaceValues[nodeInterpolation.FaceValueIndexA(face)]);
			}
			Vector vector = LimitedCellGradient(face2.cellA, cellGradients, cellGradientLimiters);
			Vector vector2 = _hmc.NormalGradientGeometry.VirtualOffsetA[face];
			return (0.0 - (vector.DeltaX * vector2.DeltaX + vector.DeltaY * vector2.DeltaY)) * inversePerpendicularDistance;
		}

		private static Vector LimitedCellGradient(int cell, Vector[] cellGradients, double[] cellGradientLimiters)
		{
			Vector result = cellGradients[cell];
			if (cellGradientLimiters == null)
			{
				return result;
			}
			double cellGradientLimiter = cellGradientLimiters[cell];
			return new Vector(result.DeltaX * cellGradientLimiter, result.DeltaY * cellGradientLimiter);
		}

		internal bool TryGetObservation(int face, int ownerCell, double ownerValue, out double deltaX, out double deltaY, out double deltaValue, out double observationValue)
		{
			deltaX = 0.0;
			deltaY = 0.0;
			deltaValue = 0.0;
			observationValue = ownerValue;
			if (_types[face] == ScalarBoundaryConditionType.None || _hmc.Mesh.Faces[face].cellA != ownerCell)
			{
				return false;
			}
			Point point = _hmc.Mesh.CellCenters[ownerCell];
			if (_types[face] == ScalarBoundaryConditionType.Value)
			{
				Point point2 = _hmc.Mesh.FaceMidPoint(face);
				deltaX = point2.X - point.X;
				deltaY = point2.Y - point.Y;
				observationValue = _values[face];
				deltaValue = observationValue - ownerValue;
				return true;
			}
			Vector vector = _hmc.FaceNormal[face];
			Point point3 = _hmc.Mesh.FaceMidPoint(face);
			double point3PointTerm = (point3.X - point.X) * vector.DeltaX + (point3.Y - point.Y) * vector.DeltaY;
			deltaX = point3PointTerm * vector.DeltaX;
			deltaY = point3PointTerm * vector.DeltaY;
			deltaValue = _values[face] * point3PointTerm;
			observationValue = ownerValue + deltaValue;
			return true;
		}

		private void SetCondition(int face, ScalarBoundaryConditionType type, double value)
		{
			if (face < 0 || face >= _hmc.Mesh.FaceCount)
			{
				throw new ArgumentOutOfRangeException("face");
			}
			Face face2 = _hmc.Mesh.Faces[face];
			if (face2.cellB >= 0)
			{
				throw new ArgumentException("Scalar boundary conditions require a boundary face.", "face");
			}
			if (!double.IsFinite(value))
			{
				throw new ArgumentOutOfRangeException("value");
			}
			if (_types[face] == ScalarBoundaryConditionType.None)
			{
				_activeFaces.Add(face);
				AddAffectedCell(face2.cellA);
				AddAffectedNode(face2.fpA);
				AddAffectedNode(face2.fpB);
			}
			_types[face] = type;
			_values[face] = value;
		}

		private void AddAffectedCell(int cell)
		{
			if (!_cellAffected[cell])
			{
				_cellAffected[cell] = true;
				_affectedCells.Add(cell);
			}
		}

		private void AddAffectedNode(int node)
		{
			if (!_nodeAffected[node])
			{
				_nodeAffected[node] = true;
				_affectedNodes.Add(node);
			}
		}

		private double ConstrainedNodeFaceValue(int node, int fan, NodeFanInterpolation nodeInterpolation, ReadOnlySpan<double> cellValues, ReadOnlySpan<double> cellVolumes, double fallback)
		{
			Span<double> rows = stackalloc double[9];
			Span<double> values = stackalloc double[3];
			int independentNodeConstraints = BuildIndependentNodeConstraints(node, fan, nodeInterpolation, rows, values, out var inconsistent);
			if (independentNodeConstraints == 0)
			{
				return fallback;
			}
			Span<double> matrix = stackalloc double[9];
			Span<double> rhs = stackalloc double[3];
			Point point = _hmc.Mesh.FacePoints[node].Point;
			int[] cells = nodeInterpolation.Fans[fan].Cells;
			foreach (int cellIndex in cells)
			{
				if (cellIndex >= 0 && !_hmc.Mesh.CellIsVirtual(cellIndex) && cellVolumes[cellIndex] > 0.0 && double.IsFinite(cellVolumes[cellIndex]))
				{
					Point point2 = _hmc.Mesh.CellCenters[cellIndex];
					double point2PointTerm = point2.X - point.X;
					double point2PointTerm2 = point2.Y - point.Y;
					double computed = point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2;
					if (computed > 0.0)
					{
						double weight = 1.0 / computed;
						AddPlaneObjective(1.0, point2PointTerm, point2PointTerm2, cellValues[cellIndex], weight, matrix, rhs);
					}
				}
			}
			double matrixTerm = Math.Max(matrix[0] + matrix[4] + matrix[8], 1.0);
			if (inconsistent)
			{
				AddWeightedNodeConstraints(node, fan, nodeInterpolation, point, matrixTerm * 100000000.0, matrix, rhs);
				double computed2 = matrixTerm * 1E-12;
				matrix[0] += computed2;
				matrix[4] += computed2;
				matrix[8] += computed2;
				rhs[0] += computed2 * fallback;
				if (!SolveLinearSystem(matrix, rhs, 3) || !double.IsFinite(rhs[0]))
				{
					return fallback;
				}
				return rhs[0];
			}
			double computed3 = matrixTerm * 1E-12;
			matrix[0] += computed3;
			matrix[4] += computed3;
			matrix[8] += computed3;
			rhs[0] += computed3 * fallback;
			int independentNodeConstraintsOffset = 3 + independentNodeConstraints;
			Span<double> matrix2 = stackalloc double[36];
			Span<double> rhs2 = stackalloc double[6];
			for (int j = 0; j < 3; j++)
			{
				rhs2[j] = rhs[j];
				for (int k = 0; k < 3; k++)
				{
					matrix2[j * independentNodeConstraintsOffset + k] = matrix[j * 3 + k];
				}
			}
			for (int l = 0; l < independentNodeConstraints; l++)
			{
				int ordinal = 3 + l;
				rhs2[ordinal] = values[l];
				for (int m = 0; m < 3; m++)
				{
					double row = rows[l * 3 + m];
					matrix2[m * independentNodeConstraintsOffset + ordinal] = row;
					matrix2[ordinal * independentNodeConstraintsOffset + m] = row;
				}
			}
			if (!SolveLinearSystem(matrix2, rhs2, independentNodeConstraintsOffset) || !double.IsFinite(rhs2[0]))
			{
				return fallback;
			}
			return rhs2[0];
		}

		private int BuildIndependentNodeConstraints(int node, int fan, NodeFanInterpolation nodeInterpolation, Span<double> rows, Span<double> values, out bool inconsistent)
		{
			int independentNodeConstraints = 0;
			inconsistent = false;
			Point point = _hmc.Mesh.FacePoints[node].Point;
			Span<double> doubleSpan = stackalloc double[3];
			FacePoint facePoint = _hmc.Mesh.FacePoints[node];
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				if (nodeInterpolation.FaceFans[i] != fan)
				{
					continue;
				}
				int faceIndex = facePoint.Faces[i];
				ScalarBoundaryConditionType scalarBoundaryConditionType = _types[faceIndex];
				if (scalarBoundaryConditionType == ScalarBoundaryConditionType.None)
				{
					continue;
				}
				doubleSpan.Clear();
				double faceIndexTerm = _values[faceIndex];
				if (scalarBoundaryConditionType == ScalarBoundaryConditionType.Value)
				{
					Point point2 = _hmc.Mesh.FaceMidPoint(faceIndex);
					doubleSpan[0] = 1.0;
					doubleSpan[1] = point2.X - point.X;
					doubleSpan[2] = point2.Y - point.Y;
				}
				else
				{
					Vector vector = _hmc.FaceNormal[faceIndex];
					doubleSpan[1] = vector.DeltaX;
					doubleSpan[2] = vector.DeltaY;
				}
				for (int j = 0; j < independentNodeConstraints; j++)
				{
					int ordinal = j * 3;
					double rowsTerm = doubleSpan[0] * rows[ordinal] + doubleSpan[1] * rows[ordinal + 1] + doubleSpan[2] * rows[ordinal + 2];
					doubleSpan[0] -= rowsTerm * rows[ordinal];
					doubleSpan[1] -= rowsTerm * rows[ordinal + 1];
					doubleSpan[2] -= rowsTerm * rows[ordinal + 2];
					faceIndexTerm -= rowsTerm * values[j];
				}
				double computed = Math.Sqrt(doubleSpan[0] * doubleSpan[0] + doubleSpan[1] * doubleSpan[1] + doubleSpan[2] * doubleSpan[2]);
				if (!(computed > 1E-13))
				{
					if (Math.Abs(faceIndexTerm) > 1E-13 * (Math.Abs(_values[faceIndex]) + 1.0))
					{
						inconsistent = true;
					}
					continue;
				}
				int scaledIndependentNodeConstraints = independentNodeConstraints * 3;
				rows[scaledIndependentNodeConstraints] = doubleSpan[0] / computed;
				rows[scaledIndependentNodeConstraints + 1] = doubleSpan[1] / computed;
				rows[scaledIndependentNodeConstraints + 2] = doubleSpan[2] / computed;
				values[independentNodeConstraints] = faceIndexTerm / computed;
				independentNodeConstraints++;
				if (independentNodeConstraints == 3)
				{
					break;
				}
			}
			return independentNodeConstraints;
		}

		private void AddWeightedNodeConstraints(int node, int fan, NodeFanInterpolation nodeInterpolation, Point nodePoint, double weight, Span<double> matrix, Span<double> rhs)
		{
			FacePoint facePoint = _hmc.Mesh.FacePoints[node];
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				if (nodeInterpolation.FaceFans[i] == fan)
				{
					int faceIndex = facePoint.Faces[i];
					switch (_types[faceIndex])
					{
					case ScalarBoundaryConditionType.Value:
					{
						Point point = _hmc.Mesh.FaceMidPoint(faceIndex);
						AddPlaneObjective(1.0, point.X - nodePoint.X, point.Y - nodePoint.Y, _values[faceIndex], weight, matrix, rhs);
						break;
					}
					default:
					{
						Vector vector = _hmc.FaceNormal[faceIndex];
						AddPlaneObjective(0.0, vector.DeltaX, vector.DeltaY, _values[faceIndex], weight, matrix, rhs);
						break;
					}
					case ScalarBoundaryConditionType.None:
						break;
					}
				}
			}
		}

		private static void AddPlaneObjective(double a, double b, double c, double computed, double weight, Span<double> matrix, Span<double> rhs)
		{
			matrix[0] += weight * a * a;
			matrix[1] += weight * a * b;
			matrix[2] += weight * a * c;
			matrix[3] += weight * b * a;
			matrix[4] += weight * b * b;
			matrix[5] += weight * b * c;
			matrix[6] += weight * c * a;
			matrix[7] += weight * c * b;
			matrix[8] += weight * c * c;
			rhs[0] += weight * a * computed;
			rhs[1] += weight * b * computed;
			rhs[2] += weight * c * computed;
		}

		private static bool SolveLinearSystem(Span<double> matrix, Span<double> rhs, int size)
		{
			for (int i = 0; i < size; i++)
			{
				int ordinal = i;
				double absoluteMatrix = Math.Abs(matrix[i * size + i]);
				for (int j = i + 1; j < size; j++)
				{
					double absoluteMatrix2 = Math.Abs(matrix[j * size + i]);
					if (!(absoluteMatrix2 <= absoluteMatrix))
					{
						ordinal = j;
						absoluteMatrix = absoluteMatrix2;
					}
				}
				if (!(absoluteMatrix > 1E-13) || !double.IsFinite(absoluteMatrix))
				{
					return false;
				}
				if (ordinal != i)
				{
					for (int k = i; k < size; k++)
					{
						double matrix2 = matrix[i * size + k];
						matrix[i * size + k] = matrix[ordinal * size + k];
						matrix[ordinal * size + k] = matrix2;
					}
					double rhs2 = rhs[i];
					rhs[i] = rhs[ordinal];
					rhs[ordinal] = rhs2;
				}
				double matrix3 = matrix[i * size + i];
				for (int l = i + 1; l < size; l++)
				{
					double matrixOverMatrix3 = matrix[l * size + i] / matrix3;
					if (matrixOverMatrix3 != 0.0)
					{
						for (int m = i; m < size; m++)
						{
							matrix[l * size + m] -= matrixOverMatrix3 * matrix[i * size + m];
						}
						rhs[l] -= matrixOverMatrix3 * rhs[i];
					}
				}
			}
			for (int sizeOffset = size - 1; sizeOffset >= 0; sizeOffset--)
			{
				double rhs3 = rhs[sizeOffset];
				for (int n = sizeOffset + 1; n < size; n++)
				{
					rhs3 -= matrix[sizeOffset * size + n] * rhs[n];
				}
				rhs[sizeOffset] = rhs3 / matrix[sizeOffset * size + sizeOffset];
			}
			return true;
		}
	}

	public struct FaceCompute(HydraulicMeshComputer hmc)
	{
		public const float IsDryTolerance = 0.001f;

		private readonly HydraulicMeshComputer _hmc = hmc;

		public double FaceNormalGradientCorrection(FaceNormalGradientMethod method, int fIdx, Vector[] cellGradients, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inverseOrthogonalDistance)
		{
			return FaceNormalGradientCorrection(method, fIdx, cellGradients, null, nodeFaceValues, nodeInterpolation, inverseOrthogonalDistance);
		}

		public double FaceNormalGradientCorrection(FaceNormalGradientMethod method, int fIdx, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inverseOrthogonalDistance)
		{
			switch (method)
			{
			case FaceNormalGradientMethod.OverRelaxation:
				return OverRelaxationCorrection(fIdx, cellGradients, cellGradientLimiters, inverseOrthogonalDistance);
			case FaceNormalGradientMethod.VirtualNode:
				return VirtualNodeCorrection(fIdx, cellGradients, cellGradientLimiters, inverseOrthogonalDistance);
			case FaceNormalGradientMethod.CompactFaceBased:
				if (!CompactFaceValuesAreValid(fIdx, nodeFaceValues, nodeInterpolation))
				{
					return 0.0;
				}
				return _hmc.NormalGradientGeometry.CompactCorrectionCoefficient[fIdx] * (nodeFaceValues[nodeInterpolation.FaceValueIndexB(fIdx)] - nodeFaceValues[nodeInterpolation.FaceValueIndexA(fIdx)]);
			case FaceNormalGradientMethod.TwoPoint:
				return 0.0;
			default:
				throw new ArgumentOutOfRangeException("method");
			}
		}

		public double FaceNormalGradient(FaceNormalGradientMethod method, int fIdx, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation)
		{
			return FaceNormalGradient(method, fIdx, cellValues, cellGradients, null, nodeFaceValues, nodeInterpolation);
		}

		public double FaceNormalGradient(FaceNormalGradientMethod method, int fIdx, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation)
		{
			if (method == FaceNormalGradientMethod.OverRelaxation)
			{
				Vector vector = InterpolateFaceGradient(fIdx, cellGradients, cellGradientLimiters);
				Vector vector2 = _hmc.FaceNormal[fIdx];
				return vector.DeltaX * vector2.DeltaX + vector.DeltaY * vector2.DeltaY;
			}
			Face face = _hmc.Mesh.Faces[fIdx];
			double faceInvPerpDistance = _hmc.FaceInvPerpDistances[fIdx];
			return (cellValues[face.cellB] - cellValues[face.cellA]) * faceInvPerpDistance + FaceNormalGradientCorrection(method, fIdx, cellGradients, cellGradientLimiters, nodeFaceValues, nodeInterpolation, faceInvPerpDistance);
		}

		public double FaceNormalGradientDeferredCorrection(FaceNormalGradientMethod method, int fIdx, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
		{
			return FaceNormalGradientDeferredCorrection(method, fIdx, cellValues, cellGradients, null, nodeFaceValues, nodeInterpolation, inversePerpendicularDistance);
		}

		public double FaceNormalGradientDeferredCorrection(FaceNormalGradientMethod method, int fIdx, ReadOnlySpan<double> cellValues, Vector[] cellGradients, double[] cellGradientLimiters, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation, double inversePerpendicularDistance)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			double cellValuesCellBTerm = (cellValues[face.cellB] - cellValues[face.cellA]) * inversePerpendicularDistance;
			return FaceNormalGradient(method, fIdx, cellValues, cellGradients, cellGradientLimiters, nodeFaceValues, nodeInterpolation) - cellValuesCellBTerm;
		}

		public double FaceTangentialGradient(FaceNormalGradientMethod method, int fIdx, Vector[] cellGradients, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation)
		{
			if (method == FaceNormalGradientMethod.CompactFaceBased)
			{
				if (!CompactFaceValuesAreValid(fIdx, nodeFaceValues, nodeInterpolation))
				{
					return 0.0;
				}
				return (nodeFaceValues[nodeInterpolation.FaceValueIndexB(fIdx)] - nodeFaceValues[nodeInterpolation.FaceValueIndexA(fIdx)]) * _hmc.NormalGradientGeometry.FaceInverseLengths[fIdx];
			}
			Face face = _hmc.Mesh.Faces[fIdx];
			Vector vector = ((face.cellB < 0) ? cellGradients[face.cellA] : InterpolateFaceGradient(fIdx, cellGradients));
			Vector vector2 = _hmc.FaceNormal[fIdx];
			Vector vector3 = new Vector(0.0 - vector2.DeltaY, vector2.DeltaX);
			return vector.DeltaX * vector3.DeltaX + vector.DeltaY * vector3.DeltaY;
		}

		public bool CompactFaceValuesAreValid(int face, double[] nodeFaceValues, NodeFaceInterpolationWorkspace nodeInterpolation)
		{
			if (nodeFaceValues == null || nodeInterpolation == null || !nodeInterpolation.FaceIsWet(face))
			{
				return false;
			}
			int faceValueIndexA = nodeInterpolation.FaceValueIndexA(face);
			int faceValueIndexB = nodeInterpolation.FaceValueIndexB(face);
			if (double.IsFinite(nodeFaceValues[faceValueIndexA]))
			{
				return double.IsFinite(nodeFaceValues[faceValueIndexB]);
			}
			return false;
		}

		private double OverRelaxationCorrection(int fIdx, Vector[] cellGradients, double[] cellGradientLimiters, double inverseOrthogonalDistance)
		{
			Vector vector = InterpolateFaceGradient(fIdx, cellGradients, cellGradientLimiters);
			Vector vector2 = _hmc.FaceNormal[fIdx];
			Face face = _hmc.Mesh.Faces[fIdx];
			Point point = _hmc.Mesh.CellCenters[face.cellA];
			Point point2 = _hmc.Mesh.CellCenters[face.cellB];
			double deltaXVectorTerm = vector.DeltaX * vector2.DeltaX + vector.DeltaY * vector2.DeltaY;
			double deltaXVectorTerm2 = (vector.DeltaX * (point2.X - point.X) + vector.DeltaY * (point2.Y - point.Y)) * inverseOrthogonalDistance;
			return deltaXVectorTerm - deltaXVectorTerm2;
		}

		private double VirtualNodeCorrection(int fIdx, Vector[] cellGradients, double[] cellGradientLimiters, double inversePerpendicularDistance)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			Vector vector = LimitedCellGradient(face.cellA, cellGradients, cellGradientLimiters);
			Vector vector2 = LimitedCellGradient(face.cellB, cellGradients, cellGradientLimiters);
			Vector vector3 = _hmc.NormalGradientGeometry.VirtualOffsetA[fIdx];
			Vector vector4 = _hmc.NormalGradientGeometry.VirtualOffsetB[fIdx];
			double deltaXVectorTerm = vector.DeltaX * vector3.DeltaX + vector.DeltaY * vector3.DeltaY;
			return (vector2.DeltaX * vector4.DeltaX + vector2.DeltaY * vector4.DeltaY - deltaXVectorTerm) * inversePerpendicularDistance;
		}

		private Vector InterpolateFaceGradient(int fIdx, Vector[] cellGradients, double[] cellGradientLimiters = null)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			Vector vector = LimitedCellGradient(face.cellA, cellGradients, cellGradientLimiters);
			Vector vector2 = LimitedCellGradient(face.cellB, cellGradients, cellGradientLimiters);
			FaceAB faceAB = _hmc.FaceAlpha[fIdx];
			return new Vector(faceAB.ValueA * vector.DeltaX + faceAB.ValueB * vector2.DeltaX, faceAB.ValueA * vector.DeltaY + faceAB.ValueB * vector2.DeltaY);
		}

		private static Vector LimitedCellGradient(int cell, Vector[] cellGradients, double[] cellGradientLimiters)
		{
			Vector result = cellGradients[cell];
			if (cellGradientLimiters == null)
			{
				return result;
			}
			double cellGradientLimiter = cellGradientLimiters[cell];
			return new Vector(result.DeltaX * cellGradientLimiter, result.DeltaY * cellGradientLimiter);
		}

		public double WSEL_BenPure(int fIdx, double cellWSA, double cellWSB, float minZCellA, float minZCellB, float minZFace, bool average)
		{
			Face face = _hmc.Faces[fIdx];
			int cellA = face.cellA;
			int cellB = face.cellB;
			if (cellB == -1)
			{
				return cellWSA;
			}
			double cellWSATerm = 0.0;
			double cellWSBTerm = 0.0;
			double minZCellATerm;
			if (cellWSA > cellWSB)
			{
				cellWSATerm = cellWSA;
				cellWSBTerm = cellWSB;
				minZCellATerm = minZCellA;
			}
			else
			{
				cellWSATerm = cellWSB;
				cellWSBTerm = cellWSA;
				minZCellATerm = minZCellB;
			}
			double minZCellBAboveMinZCellA = minZCellB - minZCellA;
			if ((double)minZFace > cellWSATerm)
			{
				return minZFace;
			}
			if ((cellWSB - cellWSA) * minZCellBAboveMinZCellA <= 0.0)
			{
				return cellWSATerm;
			}
			minZCellBAboveMinZCellA = Math.Abs(minZCellBAboveMinZCellA);
			double computed = cellWSATerm - minZCellATerm;
			if (computed >= 2.0 * minZCellBAboveMinZCellA)
			{
				if (!average)
				{
					return cellWSATerm;
				}
				return Math.Max((cellWSB + cellWSA) * 0.5, minZFace);
			}
			double minZFaceTerm = Math.Max(minZFace, cellWSBTerm);
			double computed2 = minZFaceTerm - minZCellATerm;
			double computed3 = minZFaceTerm + (computed * computed - computed2 * computed2) / (2.0 * minZCellBAboveMinZCellA);
			if (computed > minZCellBAboveMinZCellA)
			{
				computed3 = ((2.0 * minZCellBAboveMinZCellA - computed) * computed3 + (computed - minZCellBAboveMinZCellA) * cellWSATerm) / minZCellBAboveMinZCellA;
			}
			computed3 = Math.Min(computed3, cellWSATerm);
			return Math.Max(computed3, minZFace);
		}

		public FaceValues WSEL_BenPrev(int fIdx, float cellWSA, float cellWSB, float minZCellA, float minZCellB, float minZFace, float isDryTolerance = 0.001f)
		{
			Mesh mesh = _hmc.Mesh;
			Face face = mesh.Faces[fIdx];
			int cellA = face.cellA;
			int cellB = face.cellB;
			bool cellIsVirtual = mesh.CellIsVirtual(cellA);
			bool cellIsVirtual2 = mesh.CellIsVirtual(cellB);
			if (cellIsVirtual)
			{
				minZCellA = minZFace;
			}
			else if (cellIsVirtual2)
			{
				minZCellB = minZFace;
			}
			bool cellWSAMinZCellATerm = cellWSA <= minZCellA + isDryTolerance;
			bool cellWSBMinZCellBTerm = cellWSB <= minZCellB + isDryTolerance;
			FaceValues result = default;
			if ((cellWSAMinZCellATerm | cellWSBMinZCellBTerm) || mesh.FaceIsPerimeter(fIdx))
			{
				if (cellWSAMinZCellATerm)
				{
					result.ValueA = -9999f;
				}
				else
				{
					result.ValueA = cellWSA;
				}
				if (cellWSBMinZCellBTerm)
				{
					result.ValueB = -9999f;
				}
				else
				{
					result.ValueB = cellWSB;
				}
				result.HydraulicConnection = HydraulicConnection.None;
			}
			else if (cellWSA <= minZFace && cellWSB <= minZFace)
			{
				result.ValueA = cellWSA;
				result.ValueB = cellWSB;
				result.HydraulicConnection = HydraulicConnection.None;
			}
			else
			{
				float cellWSATerm = 0f;
				float minZCellATerm;
				if (cellWSA > cellWSB)
				{
					cellWSATerm = cellWSA;
					minZCellATerm = minZCellA;
				}
				else
				{
					cellWSATerm = cellWSB;
					minZCellATerm = minZCellB;
				}
				float minZCellBAboveMinZCellA = minZCellB - minZCellA;
				if ((cellWSB - cellWSA) * minZCellBAboveMinZCellA <= 0f)
				{
					result.ValueA = cellWSATerm;
					result.ValueB = cellWSATerm;
					result.HydraulicConnection = HydraulicConnection.Backfill;
				}
				else
				{
					minZCellBAboveMinZCellA = Math.Abs(minZCellBAboveMinZCellA);
					float computed = cellWSATerm - minZCellATerm;
					if (computed >= 2f * minZCellBAboveMinZCellA)
					{
						result.ValueA = cellWSATerm;
						result.ValueB = cellWSATerm;
						result.HydraulicConnection = HydraulicConnection.DownhillDeep;
					}
					else
					{
						float cellWSACellWSBTerm = Math.Min(cellWSA, cellWSB);
						float minZFaceTerm = Math.Max(minZFace, cellWSACellWSBTerm);
						float computed2 = minZFaceTerm - minZCellATerm;
						float computed3 = minZFaceTerm + (computed * computed - computed2 * computed2) / (2f * minZCellBAboveMinZCellA);
						if (computed > minZCellBAboveMinZCellA)
						{
							computed3 = ((2f * minZCellBAboveMinZCellA - computed) * computed3 + (computed - minZCellBAboveMinZCellA) * cellWSATerm) / minZCellBAboveMinZCellA;
							result.HydraulicConnection = HydraulicConnection.DownhillIntermediate;
						}
						else
						{
							result.HydraulicConnection = HydraulicConnection.DownhillShallow;
						}
						result.ValueA = computed3;
						result.ValueB = computed3;
					}
				}
			}
			return result;
		}

		public FaceValues WSEL_Adjusted(int fIdx, Span<float> cellWSEL, MeshPropertyTables props)
		{
			Face face = _hmc.Faces[fIdx];
			float minZCellA = props.CellMinElevation(face.cellA);
			float minZCellB = ((face.cellB == -1) ? (-9999f) : props.CellMinElevation(face.cellB));
			float minZFace = props.FaceMinElevation(fIdx);
			float cellWSA = cellWSEL[face.cellA];
			float cellWSB = ((face.cellB == -1) ? (-9999f) : cellWSEL[face.cellB]);
			return WSEL_Adjusted(fIdx, cellWSA, cellWSB, minZCellA, minZCellB, minZFace);
		}

		public FaceValues WSEL_Adjusted(int fIdx, float cellWSA, float cellWSB, MeshPropertyTables props)
		{
			Face face = _hmc.Faces[fIdx];
			float minZCellA = props.CellMinElevation(face.cellA);
			float minZCellB = ((face.cellB == -1) ? (-9999f) : props.CellMinElevation(face.cellB));
			float minZFace = props.FaceMinElevation(fIdx);
			return WSEL_Adjusted(fIdx, cellWSA, cellWSB, minZCellA, minZCellB, minZFace);
		}

		public FaceValues WSEL_Adjusted(int fIdx, float cellWSA, float cellWSB, float minZCellA, float minZCellB, float minZFace, float isDryTolerance = 0.001f)
		{
			Mesh mesh = _hmc.Mesh;
			Face face = mesh.Faces[fIdx];
			int cellA = face.cellA;
			int cellB = face.cellB;
			bool cellWSAMinZCellATerm = cellWSA <= minZCellA + isDryTolerance;
			bool cellWSBMinZCellBTerm = cellWSB <= minZCellB + isDryTolerance;
			FaceValues result = default;
			if ((cellWSAMinZCellATerm | cellWSBMinZCellBTerm) || mesh.FaceIsPerimeter(fIdx))
			{
				if (cellWSAMinZCellATerm || mesh.CellIsVirtual(cellA))
				{
					result.ValueA = -9999f;
				}
				else
				{
					result.ValueA = cellWSA;
				}
				if (cellWSBMinZCellBTerm || mesh.CellIsVirtual(cellB))
				{
					result.ValueB = -9999f;
				}
				else
				{
					result.ValueB = cellWSB;
				}
				result.HydraulicConnection = HydraulicConnection.None;
			}
			else if (cellWSA <= minZFace && cellWSB <= minZFace)
			{
				result.ValueA = cellWSA;
				result.ValueB = cellWSB;
				result.HydraulicConnection = HydraulicConnection.None;
			}
			else
			{
				int neighborCell;
				float cellWSATerm;
				float cellWSBTerm;
				float minZCellATerm;
				if (cellWSA > cellWSB)
				{
					neighborCell = cellA;
					cellWSATerm = cellWSA;
					cellWSBTerm = cellWSB;
					minZCellATerm = minZCellA;
				}
				else
				{
					neighborCell = cellB;
					cellWSATerm = cellWSB;
					cellWSBTerm = cellWSA;
					minZCellATerm = minZCellB;
				}
				float minZCellBAboveMinZCellA = minZCellB - minZCellA;
				bool cellWSBCellWSATerm = (cellWSB - cellWSA) * minZCellBAboveMinZCellA <= 0f;
				minZCellBAboveMinZCellA = Math.Abs(minZCellBAboveMinZCellA);
				float computed = cellWSATerm - minZCellATerm;
				float minZFaceTerm = Math.Max(minZFace, cellWSBTerm);
				float computed2 = minZFaceTerm - minZCellATerm;
				float minZFaceTerm2 = cellWSATerm - minZFace;
				AvgWaterSurfaceWithCritCheck(cellWSA, cellWSB, cellWSATerm, minZFace, out var wasCritCapUsed);
				float computed3 = cellWSATerm;
				bool wasCritCapUsed2 = wasCritCapUsed && (double)computed / (double)minZFaceTerm2 > 2.0;
				if (wasCritCapUsed2 | cellWSBCellWSATerm)
				{
					if (wasCritCapUsed2)
					{
						if (neighborCell == cellA)
						{
							result.ValueA = computed3;
							result.ValueB = cellWSB;
						}
						else
						{
							result.ValueA = cellWSA;
							result.ValueB = computed3;
						}
						result.HydraulicConnection = HydraulicConnection.Levee;
					}
					else
					{
						result.ValueA = computed3;
						result.ValueB = computed3;
						result.HydraulicConnection = HydraulicConnection.Backfill;
					}
				}
				else if (computed >= 2f * minZCellBAboveMinZCellA)
				{
					result.ValueB = (result.ValueA = (cellWSA + cellWSB) / 2f);
					result.HydraulicConnection = HydraulicConnection.DownhillDeep;
				}
				else
				{
					float computed4 = minZFaceTerm + (computed * computed - computed2 * computed2) / (2f * minZCellBAboveMinZCellA);
					if (computed > minZCellBAboveMinZCellA)
					{
						computed4 = ((2f * minZCellBAboveMinZCellA - computed) * computed4 + (computed - minZCellBAboveMinZCellA) * computed3) / minZCellBAboveMinZCellA;
						result.HydraulicConnection = HydraulicConnection.DownhillIntermediate;
					}
					else
					{
						result.HydraulicConnection = HydraulicConnection.DownhillShallow;
					}
					result.ValueA = computed4;
					result.ValueB = computed4;
				}
			}
			return result;
		}

		public FaceValues WSEL(int fIdx, Span<float> cellWSEL, MeshPropertyTables props, FaceWaterSurfaceMode wsMode = FaceWaterSurfaceMode.Adjusted)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			if (face.cellB == -1)
			{
				float valueA = cellWSEL[face.cellA];
				return new FaceValues
				{
					ValueA = valueA,
					ValueB = -9999f,
					HydraulicConnection = HydraulicConnection.None
				};
			}
			float minZCellA = props.CellMinimumElevations[face.cellA];
			float minZCellB = props.CellMinimumElevations[face.cellB];
			float minZFace = props.FaceMinimumElevations[fIdx];
			if (wsMode == FaceWaterSurfaceMode.Adjusted)
			{
				return WSEL_Adjusted(fIdx, cellWSEL[face.cellA], cellWSEL[face.cellB], minZCellA, minZCellB, minZFace);
			}
			return WSEL_BenPrev(fIdx, cellWSEL[face.cellA], cellWSEL[face.cellB], minZCellA, minZCellB, minZFace);
		}

		public FaceValues WSEL(int fIdx, float cellAWSEL, float cellBWSEL, MeshPropertyTables props, FaceWaterSurfaceMode wsMode = FaceWaterSurfaceMode.Adjusted)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			if (face.cellB == -1)
			{
				return new FaceValues
				{
					ValueA = cellAWSEL,
					ValueB = -9999f,
					HydraulicConnection = HydraulicConnection.None
				};
			}
			float minZCellA = props.CellMinimumElevations[face.cellA];
			float minZCellB = props.CellMinimumElevations[face.cellB];
			float minZFace = props.FaceMinimumElevations[fIdx];
			if (wsMode == FaceWaterSurfaceMode.Adjusted)
			{
				return WSEL_Adjusted(fIdx, cellAWSEL, cellBWSEL, minZCellA, minZCellB, minZFace);
			}
			return WSEL_BenPrev(fIdx, cellAWSEL, cellBWSEL, minZCellA, minZCellB, minZFace);
		}

		public void WSELTimeSeries(int fIdx, Memory<float> cellAWSEL, Memory<float> cellBWSEL, MeshPropertyTables props, Memory<FaceValues> outFaceWSEL, FaceWaterSurfaceMode wsMode = FaceWaterSurfaceMode.Adjusted, float isDryTolerance = 0.001f)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			float minZCellA = props.CellMinimumElevations[face.cellA];
			float minZCellB = ((face.cellB == -1) ? (-9999f) : props.CellMinimumElevations[face.cellB]);
			float minZFace = props.FaceMinimumElevations[fIdx];
			WSELTimeSeries(fIdx, cellAWSEL, cellBWSEL, minZFace, minZCellA, minZCellB, outFaceWSEL, wsMode, isDryTolerance);
		}

		public void WSELTimeSeries(int fIdx, Memory<float> cellAWSEL, Memory<float> cellBWSEL, float minZFace, float minZCellA, float minZCellB, Memory<FaceValues> outFaceWSEL, FaceWaterSurfaceMode wsMode = FaceWaterSurfaceMode.Adjusted, float isDryTolerance = 0.001f)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			int length = cellAWSEL.Length;
			if (outFaceWSEL.Length != length)
			{
				throw new ArgumentException("Input array size mismatch in WSELTimeSeries");
			}
			if (face.cellB == -1)
			{
				Span<FaceValues> outFaceWSELSpan = outFaceWSEL.Span;
				Span<float> cellAWSELSpan = cellAWSEL.Span;
				for (int i = 0; i < length; i++)
				{
					outFaceWSELSpan[i] = new FaceValues
					{
						ValueA = cellAWSELSpan[i],
						ValueB = -9999f,
						HydraulicConnection = HydraulicConnection.None
					};
				}
				return;
			}
			if (cellBWSEL.Length != length)
			{
				throw new ArgumentException("Input array size mismatch in WSELTimeSeries");
			}
			Span<float> cellAWSEL2 = cellAWSEL.Span;
			Span<float> cellBWSELSpan = cellBWSEL.Span;
			Span<FaceValues> outFaceWSEL2 = outFaceWSEL.Span;
			for (int j = 0; j < length; j++)
			{
				if (wsMode == FaceWaterSurfaceMode.Adjusted)
				{
					outFaceWSEL2[j] = WSEL_Adjusted(fIdx, cellAWSEL2[j], cellBWSELSpan[j], minZCellA, minZCellB, minZFace, isDryTolerance);
				}
				else
				{
					outFaceWSEL2[j] = WSEL_BenPrev(fIdx, cellAWSEL2[j], cellBWSELSpan[j], minZCellA, minZCellB, minZFace, isDryTolerance);
				}
			}
		}

		private float AvgWaterSurfaceWithCritCheck(float cellWSA, float cellWSB, float maxWSEL, float minZFace, out bool wasCritCapUsed)
		{
			float cellWSACellWSBTerm = (cellWSA + cellWSB) * 0.5f;
			float maxWSELMinZFaceTerm = (maxWSEL - minZFace) * (2f / 3f) + minZFace;
			if (cellWSACellWSBTerm > maxWSELMinZFaceTerm)
			{
				wasCritCapUsed = false;
				return cellWSACellWSBTerm;
			}
			wasCritCapUsed = true;
			return maxWSELMinZFaceTerm;
		}

		public float WSEL_Upwind(int fIdx, float faceVelocity, float cellAWsel, float cellBWsel)
		{
			_ = ref _hmc.Mesh.Faces[fIdx];
			if ((double)faceVelocity > 0.0)
			{
				return cellAWsel;
			}
			if ((double)faceVelocity < 0.0)
			{
				return cellBWsel;
			}
			return Math.Max(cellAWsel, cellBWsel);
		}

		public double WSEL_Engine(int fIdx, Span<double> cellElevations, Span<double> facePinned, Span<double> faceVelocity, FaceElevationMethod method, MeshPropertyTables props)
		{
			Face face = _hmc.Mesh.Faces[fIdx];
			double cellElevation = cellElevations[face.cellA];
			if (face.cellB != -1)
			{
				switch (method)
				{
				case FaceElevationMethod.Upwind:
				{
					if (faceVelocity[fIdx] > 0.0)
					{
						return cellElevation;
					}
					if (faceVelocity[fIdx] < 0.0)
					{
						return cellElevations[face.cellB];
					}
					double cellElevation2 = cellElevations[face.cellB];
					return Math.Max(cellElevation, cellElevation2);
				}
				case FaceElevationMethod.Ben:
				{
					double cellElevation3 = cellElevations[face.cellB];
					float minZCellA3 = props.CellMinimumElevations[face.cellA];
					float minZCellB3 = props.CellMinimumElevations[face.cellB];
					float minZFace3 = props.FaceMinimumElevations[fIdx];
					return WSEL_BenPure(fIdx, cellElevation, cellElevation3, minZCellA3, minZCellB3, minZFace3, average: false);
				}
				case FaceElevationMethod.BenOrUpwind:
				{
					double cellElevationTerm;
					double cellElevation4;
					if (faceVelocity[fIdx] > 0.0)
					{
						cellElevationTerm = cellElevation;
					}
					else if (faceVelocity[fIdx] < 0.0)
					{
						cellElevation4 = cellElevations[face.cellB];
						cellElevationTerm = cellElevation4;
					}
					else
					{
						cellElevation4 = cellElevations[face.cellB];
						cellElevationTerm = Math.Max(cellElevation, cellElevation4);
					}
					cellElevation4 = cellElevations[face.cellB];
					float minZCellA2 = props.CellMinimumElevations[face.cellA];
					float minZCellB2 = props.CellMinimumElevations[face.cellB];
					float minZFace2 = props.FaceMinimumElevations[fIdx];
					return Math.Min(cellElevationTerm, WSEL_BenPure(fIdx, cellElevation, cellElevation4, minZCellA2, minZCellB2, minZFace2, average: false));
				}
				case FaceElevationMethod.BenAverage:
				{
					double cellElevation5 = cellElevations[face.cellB];
					float minZCellA = props.CellMinimumElevations[face.cellA];
					float minZCellB = props.CellMinimumElevations[face.cellB];
					float minZFace = props.FaceMinimumElevations[fIdx];
					return WSEL_BenPure(fIdx, cellElevation, cellElevation5, minZCellA, minZCellB, minZFace, average: true);
				}
				case FaceElevationMethod.Max:
				{
					double cellElevation6 = cellElevations[face.cellB];
					return Math.Max(cellElevation, cellElevation6);
				}
				case FaceElevationMethod.Average:
				{
					double cellElevation7 = cellElevations[face.cellB];
					return 0.5 * (cellElevation + cellElevation7);
				}
				default:
					throw new Exception("Bad FaceElevationMethod");
				}
			}
			if (!facePinned.IsEmpty && facePinned[fIdx] != -9999.0)
			{
				return 0.5 * (facePinned[fIdx] + cellElevation);
			}
			return cellElevation;
		}

		public float Scalar_FromCells(int fIdx, Span<float> cellValues)
		{
			Face face = _hmc.Faces[fIdx];
			float cellValue = cellValues[face.cellA];
			float cellValue2 = cellValues[face.cellB];
			if ((double)cellValue == -9999.0)
			{
				return cellValue2;
			}
			if ((double)cellValue2 == -9999.0)
			{
				return cellValue;
			}
			return (cellValue + cellValue2) / 2f;
		}

		public void ComputeStencils(int fidx, out FaceVelocityCoef stencilA, out FaceVelocityCoef stencilB)
		{
			stencilA = default;
			stencilB = default;
			Face face = _hmc.Faces[fidx];
			Cell cell = _hmc.Cells[face.cellA];
			int nextCWFaceByGlobalIndex = cell.NextCWFaceByGlobalIndex(fidx);
			int nextCCWFaceByGlobalIndex = cell.NextCCWFaceByGlobalIndex(fidx);
			stencilA.AddFaceNormal(_hmc.FaceNormal[nextCWFaceByGlobalIndex]);
			stencilA.AddFaceNormal(_hmc.FaceNormal[nextCCWFaceByGlobalIndex]);
			stencilA.AddFaceNormal(_hmc.FaceNormal[fidx]);
			stencilA.Complete();
			if (face.cellB != -1)
			{
				Cell cell2 = _hmc.Cells[face.cellB];
				int nextCWFaceByGlobalIndex2 = cell2.NextCWFaceByGlobalIndex(fidx);
				int nextCCWFaceByGlobalIndex2 = cell2.NextCCWFaceByGlobalIndex(fidx);
				stencilB.AddFaceNormal(_hmc.FaceNormal[nextCWFaceByGlobalIndex2]);
				stencilB.AddFaceNormal(_hmc.FaceNormal[nextCCWFaceByGlobalIndex2]);
				stencilB.AddFaceNormal(_hmc.FaceNormal[fidx]);
				stencilB.Complete();
			}
			else
			{
				stencilB.AddFaceNormal(_hmc.FaceNormal[fidx]);
				stencilB.Complete();
			}
		}

		public (Vector, Vector) Vector_CAveragedStencil(int fIdx, Span<float> perpendicularFaceVelocities, Span<FaceValues> faceWSs)
		{
			Span<float> perpendicularFaceVelocitiesSpan = perpendicularFaceVelocities;
			Face face = _hmc.Faces[fIdx];
			Vector faceNormal = _hmc.FaceNormal[fIdx];
			float faceVelocity = perpendicularFaceVelocitiesSpan[fIdx];
			ComputeStencils(fIdx, out var stencilA, out var stencilB);
			Cell cell = _hmc.Cells[face.cellA];
			int nextCWFaceByGlobalIndex = cell.NextCWFaceByGlobalIndex(fIdx);
			int nextCCWFaceByGlobalIndex = cell.NextCCWFaceByGlobalIndex(fIdx);
			Vector cwFaceNormal = _hmc.FaceNormal[nextCWFaceByGlobalIndex];
			Vector ccwFaceNormal = _hmc.FaceNormal[nextCCWFaceByGlobalIndex];
			float cwFaceVelocity = perpendicularFaceVelocitiesSpan[nextCWFaceByGlobalIndex];
			float ccwFaceVelocity = perpendicularFaceVelocitiesSpan[nextCCWFaceByGlobalIndex];
			bool isHydraulicallyConnected = faceWSs[nextCWFaceByGlobalIndex].IsHydraulicallyConnected;
			bool isHydraulicallyConnected2 = faceWSs[nextCCWFaceByGlobalIndex].IsHydraulicallyConnected;
			Vector vector_CStencil = Vector_CStencil(stencilA, faceNormal, faceVelocity, cwFaceNormal, cwFaceVelocity, isHydraulicallyConnected, ccwFaceNormal, ccwFaceVelocity, isHydraulicallyConnected2);
			int cellB = face.cellB;
			if (cellB == -1)
			{
				return (vector_CStencil, Vector.Zero);
			}
			Cell cell2 = _hmc.Cells[cellB];
			int nextCWFaceByGlobalIndex2 = cell2.NextCWFaceByGlobalIndex(fIdx);
			int nextCCWFaceByGlobalIndex2 = cell2.NextCCWFaceByGlobalIndex(fIdx);
			Vector cwFaceNormal2 = _hmc.FaceNormal[nextCWFaceByGlobalIndex2];
			Vector ccwFaceNormal2 = _hmc.FaceNormal[nextCCWFaceByGlobalIndex2];
			float cwFaceVelocity2 = perpendicularFaceVelocitiesSpan[nextCWFaceByGlobalIndex2];
			float ccwFaceVelocity2 = perpendicularFaceVelocitiesSpan[nextCCWFaceByGlobalIndex2];
			bool isHydraulicallyConnected3 = faceWSs[nextCWFaceByGlobalIndex2].IsHydraulicallyConnected;
			bool isHydraulicallyConnected4 = faceWSs[nextCCWFaceByGlobalIndex2].IsHydraulicallyConnected;
			Vector vector_CStencil2 = Vector_CStencil(stencilB, faceNormal, faceVelocity, cwFaceNormal2, cwFaceVelocity2, isHydraulicallyConnected3, ccwFaceNormal2, ccwFaceVelocity2, isHydraulicallyConnected4);
			if (faceWSs[fIdx].IsHydraulicallyConnected)
			{
				Vector vector = new Vector((vector_CStencil.X + vector_CStencil2.X) / 2.0, (vector_CStencil.Y + vector_CStencil2.Y) / 2.0);
				return (vector, vector);
			}
			return (vector_CStencil, vector_CStencil2);
		}

		public Vector Vector_CStencil(FaceVelocityCoef coefMatrix, Vector faceNormal, float faceVelocity, Vector cwFaceNormal, float cwFaceVelocity, bool cwFaceConnected, Vector ccwFaceNormal, float ccwFaceVelocity, bool ccwFaceConnected)
		{
			Vector vector = new Vector(faceNormal.Y, 0.0 - faceNormal.X);
			double vectorTerm = 0.0;
			if (cwFaceConnected | ccwFaceConnected)
			{
				double cwFaceNormalCwFaceVelocityTerm = cwFaceNormal.X * (double)cwFaceVelocity;
				double cwFaceNormalCwFaceVelocityTerm2 = cwFaceNormal.Y * (double)cwFaceVelocity;
				cwFaceNormalCwFaceVelocityTerm += ccwFaceNormal.X * (double)ccwFaceVelocity;
				cwFaceNormalCwFaceVelocityTerm2 += ccwFaceNormal.Y * (double)ccwFaceVelocity;
				cwFaceNormalCwFaceVelocityTerm += faceNormal.X * (double)faceVelocity;
				cwFaceNormalCwFaceVelocityTerm2 += faceNormal.Y * (double)faceVelocity;
				double a22CoefMatrixTerm = coefMatrix.A22 * cwFaceNormalCwFaceVelocityTerm - coefMatrix.A12 * cwFaceNormalCwFaceVelocityTerm2;
				double a12CoefMatrixTerm = (0.0 - coefMatrix.A12) * cwFaceNormalCwFaceVelocityTerm + coefMatrix.A11 * cwFaceNormalCwFaceVelocityTerm2;
				vectorTerm = a22CoefMatrixTerm * vector.X + a12CoefMatrixTerm * vector.Y;
			}
			Vector vector2 = new Vector(vectorTerm * vector.X, vectorTerm * vector.Y);
			Vector vector3 = new Vector((double)faceVelocity * faceNormal.X, (double)faceVelocity * faceNormal.Y);
			return new Vector(vector3.X + vector2.X, vector3.Y + vector2.Y);
		}

		public Vector[] TrueShear(Span<Vector> faceTrueVels, Span<float> faceMann, Span<float> faceHydrRad, UnitSystem units)
		{
			Vector[] vectors = new Vector[_hmc.FaceCount];
			TrueShear(faceTrueVels, faceMann, faceHydrRad, units, vectors);
			return vectors;
		}

		public void TrueShear(Span<Vector> faceTrueVels, Span<float> faceMann, Span<float> faceHydrRad, UnitSystem units, Span<Vector> outShear)
		{
			double shearGamma = GetShearGamma(units);
			for (int i = 0; i < _hmc.FaceCount; i++)
			{
				outShear[i] = TrueShear(faceTrueVels[i], faceMann[i], faceHydrRad[i], shearGamma);
			}
		}

		public Vector TrueShear(Vector faceTrueVel, float n, float faceHydRadius, UnitSystem units)
		{
			double shearGamma = GetShearGamma(units);
			return TrueShear(faceTrueVel, n, faceHydRadius, shearGamma);
		}

		private Vector TrueShear(Vector faceTrueVel, float n, float faceHydRadius, double gammaFactor)
		{
			double x = faceTrueVel.X;
			double y = faceTrueVel.Y;
			if ((x == 0.0 && y == 0.0) || faceHydRadius == 0f)
			{
				return Vector.Zero;
			}
			double dx = gammaFactor * x * x * (double)n * (double)n / Math.Pow(faceHydRadius, 1.0 / 3.0);
			double dy = gammaFactor * y * y * (double)n * (double)n / Math.Pow(faceHydRadius, 1.0 / 3.0);
			return new Vector(dx, dy);
		}

		private static double GetShearGamma(UnitSystem units)
		{
			return units switch
			{
				UnitSystem.SI => 9810.0, 
				UnitSystem.USCustomary => 28.258361123740826, 
				_ => throw new NotImplementedException(), 
			};
		}

		public float[] NormalShear(Span<float> faceNormalVels, Span<float> faceMann, Span<float> faceHydrRad, UnitSystem units)
		{
			float[] result = new float[_hmc.FaceCount];
			NormalShear(faceNormalVels, faceMann, faceHydrRad, units);
			return result;
		}

		public void NormalShear(Span<float> faceNormalVels, Span<float> faceMann, Span<float> faceHydrRad, Span<float> outShear, UnitSystem units)
		{
			double shearGamma = GetShearGamma(units);
			for (int i = 0; i < _hmc.FaceCount; i++)
			{
				outShear[i] = NormalShear(faceNormalVels[i], faceMann[i], faceHydrRad[i], shearGamma);
			}
		}

		public float NormalShear(float faceNormalVel, float n, float faceHydRadius, UnitSystem units)
		{
			double shearGamma = GetShearGamma(units);
			return NormalShear(faceNormalVel, n, faceHydRadius, shearGamma);
		}

		private static float NormalShear(float vel, float n, float hRad, double gammaFactor)
		{
			if (vel == 0f || hRad == 0f)
			{
				return 0f;
			}
			return (float)(gammaFactor * (double)vel * (double)vel * (double)n * (double)n / Math.Pow(hRad, 1.0 / 3.0));
		}

		public void Tangential(Memory<double> faceNorm, Memory<double> faceTang)
		{
			SparseMatrix<double>.Product(_hmc.FaceTangential.H, faceNorm, faceTang);
		}

		public double Tangential(int fIdx, Span<double> faceNorm)
		{
			return SparseMatrix<double>.ProductRow(_hmc.FaceTangential.H, faceNorm, fIdx);
		}

		public double Tangential(int fIdx, Span<float> faceNorm)
		{
			SparseMatrix<double> h = _hmc.FaceTangential.H;
			double computed = 0.0;
			int[] rowPointers = h.RowPointers;
			int[] columnIndices = h.ColumnIndices;
			double[] values = h.Values;
			for (int i = rowPointers[fIdx]; i < rowPointers[fIdx + 1]; i++)
			{
				computed += values[i] * (double)faceNorm[columnIndices[i]];
			}
			return computed;
		}

		public (double, double) TangentialSides(int fIdx, Span<double> faceAreas, Span<double> faceNorm)
		{
			return TangentialSides(fIdx, faceAreas[fIdx], faceNorm);
		}

		public (double, double) TangentialSides(int fIdx, double faceArea, Span<double> faceNorm)
		{
			FaceTangential faceTangential = _hmc.FaceTangential;
			if (faceArea > 0.0)
			{
				double productRow = SparseMatrix<double>.ProductRow(faceTangential.H, faceNorm, fIdx);
				return (productRow, productRow);
			}
			double productRow2 = SparseMatrix<double>.ProductRow(faceTangential.A, faceNorm, fIdx);
			double productRow3 = SparseMatrix<double>.ProductRow(faceTangential.B, faceNorm, fIdx);
			return (productRow2, productRow3);
		}

		public double TangentialSideA(int fIdx, Span<double> faceNorm)
		{
			return SparseMatrix<double>.ProductRow(_hmc.FaceTangential.A, faceNorm, fIdx);
		}

		public double TangentialSideB(int fIdx, Span<double> faceNorm)
		{
			return SparseMatrix<double>.ProductRow(_hmc.FaceTangential.B, faceNorm, fIdx);
		}

		public double TangentialSideA(int fIdx, Span<float> faceNorm)
		{
			SparseMatrix<double> a = _hmc.FaceTangential.A;
			double computed = 0.0;
			int[] rowPointers = a.RowPointers;
			int[] columnIndices = a.ColumnIndices;
			double[] values = a.Values;
			for (int i = rowPointers[fIdx]; i < rowPointers[fIdx + 1]; i++)
			{
				computed += values[i] * (double)faceNorm[columnIndices[i]];
			}
			return computed;
		}

		public double TangentialSideB(int fIdx, Span<float> faceNorm)
		{
			SparseMatrix<double> b = _hmc.FaceTangential.B;
			double computed = 0.0;
			int[] rowPointers = b.RowPointers;
			int[] columnIndices = b.ColumnIndices;
			double[] values = b.Values;
			for (int i = rowPointers[fIdx]; i < rowPointers[fIdx + 1]; i++)
			{
				computed += values[i] * (double)faceNorm[columnIndices[i]];
			}
			return computed;
		}
	}

	private sealed class FaceNormalGradientGeometry
	{
		private readonly Mesh _mesh;

		public Vector[] VirtualOffsetA { get; }

		public Vector[] VirtualOffsetB { get; }

		public double[] CompactCorrectionCoefficient { get; }

		public double[] FaceInverseLengths { get; }

		public FaceNormalGradientGeometry(HydraulicMeshComputer hmesh)
		{
			_mesh = hmesh.Mesh;
			int faceCount = _mesh.FaceCount;
			VirtualOffsetA = new Vector[faceCount];
			VirtualOffsetB = new Vector[faceCount];
			CompactCorrectionCoefficient = new double[faceCount];
			FaceInverseLengths = new double[faceCount];
			for (int i = 0; i < faceCount; i++)
			{
				PrepareFaceGeometry(hmesh, i);
			}
		}

		private void PrepareFaceGeometry(HydraulicMeshComputer hmesh, int fIdx)
		{
			Face face = _mesh.Faces[fIdx];
			Point point = _mesh.FacePoints[face.fpA].Point;
			Point point2 = _mesh.FacePoints[face.fpB].Point;
			double point2PointTerm = point2.X - point.X;
			double point2PointTerm2 = point2.Y - point.Y;
			double computed = Math.Sqrt(point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2);
			if (computed > 0.0 && double.IsFinite(computed))
			{
				double faceInverseLengths = 1.0 / computed;
				Vector vector = new Vector(point2PointTerm * faceInverseLengths, point2PointTerm2 * faceInverseLengths);
				FaceInverseLengths[fIdx] = faceInverseLengths;
				Vector vector2 = hmesh.FaceNormal[fIdx];
				Point point3 = _mesh.CellCenters[face.cellA];
				Point point4 = _mesh.FaceMidPoint(fIdx);
				double faceInvPerpDistance = hmesh.FaceInvPerpDistances[fIdx];
				double point3Point4Term = (point3.X - point4.X) * vector2.DeltaX + (point3.Y - point4.Y) * vector2.DeltaY;
				Point point5 = new Point(point4.X + point3Point4Term * vector2.DeltaX, point4.Y + point3Point4Term * vector2.DeltaY);
				VirtualOffsetA[fIdx] = point5 - point3;
				Point point6 = ((face.cellB < 0) ? point4 : _mesh.CellCenters[face.cellB]);
				if (face.cellB >= 0)
				{
					double point6Point4Term = (point6.X - point4.X) * vector2.DeltaX + (point6.Y - point4.Y) * vector2.DeltaY;
					Point point7 = new Point(point4.X + point6Point4Term * vector2.DeltaX, point4.Y + point6Point4Term * vector2.DeltaY);
					VirtualOffsetB[fIdx] = point7 - point6;
				}
				double point6Point3Term = point6.X - point3.X;
				double point6Point3Term2 = point6.Y - point3.Y;
				Vector vector3 = new Vector(vector2.DeltaX - point6Point3Term * faceInvPerpDistance, vector2.DeltaY - point6Point3Term2 * faceInvPerpDistance);
				CompactCorrectionCoefficient[fIdx] = (vector3.DeltaX * vector.DeltaX + vector3.DeltaY * vector.DeltaY) * faceInverseLengths;
			}
		}
	}

	public struct NodeCompute
	{
		private readonly HydraulicMeshComputer _hmc;

		public readonly Mesh Mesh => _hmc.Mesh;

		public int NodeFaceValueCount => _hmc.NodeFaceGeometry.ValueCount;

		public NodeCompute(HydraulicMeshComputer hmc)
		{
			_hmc = hmc;
		}

		public NodeFaceInterpolationWorkspace CreateNodeFaceInterpolationWorkspace()
		{
			return new NodeFaceInterpolationWorkspace(_hmc.NodeFaceGeometry);
		}

		public void InterpolateNodeFaceValues(NodeFaceInterpolationWorkspace workspace, ReadOnlySpan<double> cellValues, Span<double> nodeFaceValues)
		{
			ValidateWorkspace(workspace);
			workspace.Interpolate(cellValues, nodeFaceValues);
		}

		public void InterpolateNodeFaceValues(int nodeIndex, NodeFaceInterpolationWorkspace workspace, ReadOnlySpan<double> cellValues, Span<double> nodeFaceValues)
		{
			ValidateWorkspace(workspace);
			workspace.InterpolateNode(nodeIndex, cellValues, nodeFaceValues);
		}

		private void ValidateWorkspace(NodeFaceInterpolationWorkspace workspace)
		{
			ArgumentNullException.ThrowIfNull(workspace, "workspace");
			if (workspace.Geometry != _hmc.NodeFaceGeometry)
			{
				throw new ArgumentException("The interpolation workspace belongs to a different mesh.", "workspace");
			}
		}

		public float Scalar_FromFaces(int nIdx, Span<float> faceValues, Func<int, Point> getFaceApplicationPoint)
		{
			FacePoint facePoint = _hmc.Mesh.FacePoints[nIdx];
			PlanarRegression planarRegression = new PlanarRegression(facePoint.Point);
			foreach (int face in facePoint.Faces)
			{
				float faceValue = faceValues[face];
				if ((double)faceValue != -9999.0)
				{
					planarRegression.Add(getFaceApplicationPoint(face), faceValue);
				}
			}
			if (planarRegression.Count == 0)
			{
				return -9999f;
			}
			return (float)planarRegression.SolveZ();
		}

		public float[] Scalar_FromFaces(Span<float> faceScalarValues, SpatialAveragingMode mode)
		{
			float[] floatValues = new float[_hmc.Mesh.NodeCount];
			Scalar_FromFaces(faceScalarValues, mode, floatValues);
			return floatValues;
		}

		public void Scalar_FromFaces<T>(Span<T> faceScalarValues, SpatialAveragingMode mode, Span<T> outNodeValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			if (outNodeValues.Length != _hmc.Mesh.FacePointCount)
			{
				throw new ArgumentException("outNodeValues wrong size.");
			}
			switch (mode)
			{
			case SpatialAveragingMode.SimpleAverage:
				Scalar_FromFaces_SpatialAverage(faceScalarValues, outNodeValues);
				break;
			case SpatialAveragingMode.PlanarRegression:
				Scalar_FromFaces_PlanarZ(faceScalarValues, outNodeValues);
				break;
			default:
				throw new NotSupportedException();
			}
		}

		public void Scalar_FromFaces_SpatialAverage<T>(Span<T> faceScalarValues, Span<T> outNodeValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			for (int i = 0; i < _hmc.Mesh.NodeCount; i++)
			{
				outNodeValues[i] = Scalar_FromFaces_SpatialAverage(i, faceScalarValues);
			}
		}

		public T Scalar_FromFaces_SpatialAverage<T>(int nIdx, Span<T> faceScalarValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			int count = facePoint.Faces.Count;
			T zero = T.Zero;
			for (int i = 0; i < count; i++)
			{
				int index = facePoint.Faces[i];
				zero += faceScalarValues[index];
			}
			return zero / T.CreateTruncating(count);
		}

		public void Scalar_FromFaces_PlanarZ<T>(Span<T> faceScalarValues, Span<T> outNodeValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			PlanarRegression pz = new PlanarRegression();
			for (int i = 0; i < _hmc.Mesh.NodeCount; i++)
			{
				outNodeValues[i] = Scalar_FromFaces_PlanarZ(i, faceScalarValues, pz);
			}
		}

		public T Scalar_FromFaces_PlanarZ<T>(int nIdx, Span<T> faceScalarValues) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			PlanarRegression planarRegression = new PlanarRegression(facePoint.Point);
			foreach (int face in facePoint.Faces)
			{
				Point pt = Mesh.FaceMidPoint(face);
				planarRegression.Add(pt, double.CreateTruncating(faceScalarValues[face]));
			}
			return T.CreateTruncating(planarRegression.SolveZ());
		}

		private T Scalar_FromFaces_PlanarZ<T>(int nIdx, Span<T> faceScalarValues, PlanarRegression pz) where T : unmanaged, IBinaryFloatingPointIeee754<T>
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			Point point = facePoint.Point;
			pz.Clear(point);
			foreach (int face in facePoint.Faces)
			{
				Point pt = Mesh.FaceMidPoint(face);
				pz.Add(pt, double.CreateTruncating(faceScalarValues[face]));
			}
			return T.CreateTruncating(pz.SolveZ());
		}

		public float[] WSEL_Global(int nIdx, Span<FaceValues> globalFaceWSEL)
		{
			float[] floatValues = new float[_hmc.Mesh.FacePoints[nIdx].Faces.Count];
			WSEL_Global(nIdx, globalFaceWSEL, floatValues);
			return floatValues;
		}

		public void WSEL_Global(int nIdx, Span<FaceValues> globalFaceWSEL, Span<float> outNodeWSEL_LOCAL)
		{
			FacePoint facePoint = _hmc.Mesh.FacePoints[nIdx];
			bool condition = true;
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				outNodeWSEL_LOCAL[i] = -9999f;
				int index = facePoint.Faces[i];
				if ((double)globalFaceWSEL[index].ValueA != -9999.0)
				{
					condition = false;
				}
				if ((double)globalFaceWSEL[index].ValueB != -9999.0)
				{
					condition = false;
				}
			}
			if (condition)
			{
				return;
			}
			ValuePlanarRegression valuePlanarRegression = new ValuePlanarRegression(facePoint.Point);
			for (int j = 0; j < facePoint.Faces.Count; j++)
			{
				if ((double)outNodeWSEL_LOCAL[j] != -9999.0)
				{
					continue;
				}
				valuePlanarRegression.Clear();
				int ccwFaceByLocalIndexFacePointTerm = j;
				do
				{
					ccwFaceByLocalIndexFacePointTerm = facePoint.CCWFaceByLocalIndex(ccwFaceByLocalIndexFacePointTerm);
				}
				while (globalFaceWSEL[facePoint.Faces[ccwFaceByLocalIndexFacePointTerm]].IsHydraulicallyConnected && ccwFaceByLocalIndexFacePointTerm != j);
				int cwFaceByLocalIndexFacePointTerm = j;
				if (cwFaceByLocalIndexFacePointTerm != ccwFaceByLocalIndexFacePointTerm)
				{
					while (globalFaceWSEL[facePoint.Faces[cwFaceByLocalIndexFacePointTerm]].IsHydraulicallyConnected)
					{
						cwFaceByLocalIndexFacePointTerm = facePoint.CWFaceByLocalIndex(cwFaceByLocalIndexFacePointTerm);
					}
				}
				int ordinal = cwFaceByLocalIndexFacePointTerm;
				do
				{
					int faceIndex = facePoint.Faces[ordinal];
					double valueB = ((_hmc.Mesh.Faces[faceIndex].fpA != nIdx) ? ((double)globalFaceWSEL[faceIndex].ValueB) : ((double)globalFaceWSEL[faceIndex].ValueA));
					if (valueB != -9999.0)
					{
						Point pt = Mesh.FaceSegment(faceIndex).Midpoint();
						valuePlanarRegression.Add(pt, valueB);
					}
					outNodeWSEL_LOCAL[ordinal] = (float)valueB;
					ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
				}
				while (ordinal != ccwFaceByLocalIndexFacePointTerm);
				int faceIndex2 = facePoint.Faces[ccwFaceByLocalIndexFacePointTerm];
				if (!globalFaceWSEL[faceIndex2].IsHydraulicallyConnected)
				{
					double valueA = ((_hmc.Mesh.Faces[faceIndex2].fpA != nIdx) ? ((double)globalFaceWSEL[faceIndex2].ValueA) : ((double)globalFaceWSEL[faceIndex2].ValueB));
					if (valueA != -9999.0)
					{
						Point pt2 = _hmc.Mesh.FaceSegment(faceIndex2).Midpoint();
						valuePlanarRegression.Add(pt2, valueA);
					}
				}
				if (valuePlanarRegression.Count > 0)
				{
					float outNodeWSEL_LOCAL2 = (float)valuePlanarRegression.SolveZ();
					ordinal = cwFaceByLocalIndexFacePointTerm;
					do
					{
						outNodeWSEL_LOCAL[ordinal] = outNodeWSEL_LOCAL2;
						ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
					}
					while (ordinal != ccwFaceByLocalIndexFacePointTerm);
				}
				if (cwFaceByLocalIndexFacePointTerm == ccwFaceByLocalIndexFacePointTerm)
				{
					break;
				}
			}
		}

		public void WSEL_Local(int nIdx, Span<FaceValues> localFaceWSEL, Span<float> outNodeWSEL)
		{
			FacePoint facePoint = _hmc.Mesh.FacePoints[nIdx];
			bool condition = true;
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				outNodeWSEL[i] = -9999f;
				if ((double)localFaceWSEL[i].ValueA != -9999.0)
				{
					condition = false;
				}
				if ((double)localFaceWSEL[i].ValueB != -9999.0)
				{
					condition = false;
				}
			}
			if (condition)
			{
				return;
			}
			PlanarRegression planarRegression = new PlanarRegression(facePoint.Point);
			for (int j = 0; j < facePoint.Faces.Count; j++)
			{
				if ((double)outNodeWSEL[j] != -9999.0)
				{
					continue;
				}
				planarRegression.Clear();
				int ccwFaceByLocalIndexFacePointTerm = j;
				do
				{
					ccwFaceByLocalIndexFacePointTerm = facePoint.CCWFaceByLocalIndex(ccwFaceByLocalIndexFacePointTerm);
				}
				while (localFaceWSEL[ccwFaceByLocalIndexFacePointTerm].IsHydraulicallyConnected && ccwFaceByLocalIndexFacePointTerm != j);
				int cwFaceByLocalIndexFacePointTerm = j;
				if (cwFaceByLocalIndexFacePointTerm != ccwFaceByLocalIndexFacePointTerm)
				{
					while (localFaceWSEL[cwFaceByLocalIndexFacePointTerm].IsHydraulicallyConnected)
					{
						cwFaceByLocalIndexFacePointTerm = facePoint.CWFaceByLocalIndex(cwFaceByLocalIndexFacePointTerm);
					}
				}
				int ordinal = cwFaceByLocalIndexFacePointTerm;
				do
				{
					int faceIndex = facePoint.Faces[ordinal];
					double valueB = ((_hmc.Mesh.Faces[faceIndex].fpA != nIdx) ? ((double)localFaceWSEL[ordinal].ValueB) : ((double)localFaceWSEL[ordinal].ValueA));
					if (valueB != -9999.0)
					{
						Point pt = Mesh.FaceSegment(faceIndex).Midpoint();
						planarRegression.Add(pt, valueB);
					}
					outNodeWSEL[ordinal] = (float)valueB;
					ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
				}
				while (ordinal != ccwFaceByLocalIndexFacePointTerm);
				int faceIndex2 = facePoint.Faces[ccwFaceByLocalIndexFacePointTerm];
				if (!localFaceWSEL[ccwFaceByLocalIndexFacePointTerm].IsHydraulicallyConnected)
				{
					double valueA = ((_hmc.Mesh.Faces[faceIndex2].fpA != nIdx) ? ((double)localFaceWSEL[ccwFaceByLocalIndexFacePointTerm].ValueA) : ((double)localFaceWSEL[ccwFaceByLocalIndexFacePointTerm].ValueB));
					if (valueA != -9999.0)
					{
						Point pt2 = Mesh.FaceSegment(faceIndex2).Midpoint();
						planarRegression.Add(pt2, valueA);
					}
				}
				if (planarRegression.Count > 0)
				{
					float outNodeWSEL2 = (float)planarRegression.SolveZ();
					ordinal = cwFaceByLocalIndexFacePointTerm;
					do
					{
						outNodeWSEL[ordinal] = outNodeWSEL2;
						ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
					}
					while (ordinal != ccwFaceByLocalIndexFacePointTerm);
				}
				if (cwFaceByLocalIndexFacePointTerm == ccwFaceByLocalIndexFacePointTerm)
				{
					break;
				}
			}
		}

		public void Vector_FromFaces(Span<(Vector, Vector)> faceTrueVels, Span<FaceValues> faceWSEL, Span<FacePointVels> nodeVelocities)
		{
			if (nodeVelocities.Length != _hmc.FacePointCount)
			{
				throw new ArgumentException("nodeVelocities");
			}
			for (int i = 0; i < _hmc.FacePointCount; i++)
			{
				nodeVelocities[i] = Vector_FaceTrueVectorWeightedAverage(i, faceTrueVels, faceWSEL);
			}
		}

		public FacePointVels Vector_FaceTrueVectorWeightedAverage(int nIdx, Span<(Vector, Vector)> faceTrueVelocities, Span<FaceValues> faceWSEL)
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			Vector[] vectors = new Vector[facePoint.Faces.Count];
			bool condition = true;
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				int index = facePoint.Faces[i];
				if ((double)faceWSEL[index].ValueA != -9999.0)
				{
					condition = false;
					break;
				}
				if ((double)faceWSEL[index].ValueB != -9999.0)
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				return new FacePointVels(vectors);
			}
			for (int j = 0; j < facePoint.Faces.Count; j++)
			{
				GetHydraulicallyConnectedArc(nIdx, j, faceWSEL, out var startLocalFaceIdx, out var endLocalFaceIdx);
				int startLocalFaceIdx2 = startLocalFaceIdx;
				double computed = 0.0;
				double computed2 = 0.0;
				double faceInvLengthSum = 0.0;
				do
				{
					int faceIndex = facePoint.Faces[startLocalFaceIdx2];
					(Vector, Vector) tuple = faceTrueVelocities[faceIndex];
					float faceInvLength = (float)_hmc.FaceInvLengths[faceIndex];
					if (startLocalFaceIdx2 == startLocalFaceIdx)
					{
						if (Mesh.Faces[facePoint.Faces[startLocalFaceIdx2]].fpA == nIdx)
						{
							computed += tuple.Item1.X * (double)faceInvLength;
							computed2 += tuple.Item1.Y * (double)faceInvLength;
						}
						else
						{
							computed += tuple.Item2.X * (double)faceInvLength;
							computed2 += tuple.Item2.Y * (double)faceInvLength;
						}
					}
					else
					{
						computed += tuple.Item1.X * (double)faceInvLength;
						computed2 += tuple.Item1.Y * (double)faceInvLength;
					}
					faceInvLengthSum += (double)faceInvLength;
					startLocalFaceIdx2 = facePoint.CCWFaceByLocalIndex(startLocalFaceIdx2);
				}
				while (startLocalFaceIdx2 != endLocalFaceIdx);
				int faceIndex2 = facePoint.Faces[endLocalFaceIdx];
				if (!faceWSEL[faceIndex2].IsHydraulicallyConnected)
				{
					(Vector, Vector) tuple2 = faceTrueVelocities[faceIndex2];
					float faceInvLength2 = (float)_hmc.FaceInvLengths[faceIndex2];
					if (Mesh.Faces[faceIndex2].fpA == nIdx)
					{
						computed += tuple2.Item2.X * (double)faceInvLength2;
						computed2 += tuple2.Item2.Y * (double)faceInvLength2;
					}
					else
					{
						computed += tuple2.Item1.X * (double)faceInvLength2;
						computed2 += tuple2.Item1.Y * (double)faceInvLength2;
					}
					faceInvLengthSum += (double)faceInvLength2;
				}
				vectors[j] = new Vector(computed / faceInvLengthSum, computed2 / faceInvLengthSum);
			}
			return new FacePointVels(vectors);
		}

		public Vector[] Vector_FromFaces_SpatialAverage(Span<Vector> faceVectors)
		{
			Vector[] vectors = new Vector[_hmc.NodeCount];
			Vector_FromFaces_SpatialAverage(faceVectors, vectors);
			return vectors;
		}

		private void Vector_FromFaces_SpatialAverage(Span<Vector> faceVectors, Span<Vector> outFpVals)
		{
			if (outFpVals.Length != _hmc.NodeCount)
			{
				throw new ArgumentException("outFpVals wrong size.");
			}
			for (int i = 0; i < _hmc.Mesh.NodeCount; i++)
			{
				outFpVals[i] = Vector_FromFaces_SpatialAverage(i, faceVectors);
			}
		}

		public Vector Vector_FromFaces_SpatialAverage(int nIdx, Span<Vector> faceVectors)
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			int count = facePoint.Faces.Count;
			double computed = 0.0;
			double computed2 = 0.0;
			for (int i = 0; i < count; i++)
			{
				int index = facePoint.Faces[i];
				computed += faceVectors[index].X;
				computed2 += faceVectors[index].Y;
			}
			return new Vector(computed / (double)count, computed2 / (double)count);
		}

		private void VelocityCoefficients_Weighted()
		{
			FaceVelocityCoef[] facePointVelocityCoef = new FaceVelocityCoef[Mesh.FacePointCount];
			Mesh mesh = _hmc.Mesh;
			Vector[] fn = _hmc.FaceNormal;
			double[] finvl = _hmc.FaceInvLengths;
			Parallel.SmartFor(Mesh.FacePointCount, (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					foreach (int face in mesh.FacePoints[i].Faces)
					{
						facePointVelocityCoef[i].AddWeightedFaceNormal(fn[face], (float)finvl[face]);
					}
					facePointVelocityCoef[i].Complete();
				}
			});
		}

		public void Velocity_FacePerpLeastSquares_Weighted(int nIdx, Span<float> perpendicularFaceVelocities_GLOBAL, Span<FaceValues> faceWSEL_GLOBAL, Span<Vector> facePointVelocity)
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			bool condition = true;
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				int index = facePoint.Faces[i];
				if ((double)faceWSEL_GLOBAL[index].ValueA != -9999.0)
				{
					condition = false;
					break;
				}
				if ((double)faceWSEL_GLOBAL[index].ValueB != -9999.0)
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				facePointVelocity.Fill(Vector.Zero);
				return;
			}
			for (int j = 0; j < facePoint.Faces.Count; j++)
			{
				FaceVelocityCoef faceVelocityCoef = default;
				double computed = 0.0;
				double computed2 = 0.0;
				GetHydraulicallyConnectedArc(nIdx, j, faceWSEL_GLOBAL, out var startLocalFaceIdx, out var endLocalFaceIdx);
				int startLocalFaceIdx2 = startLocalFaceIdx;
				do
				{
					int faceIndex = facePoint.Faces[startLocalFaceIdx2];
					Vector norm = _hmc.FaceNormal[faceIndex];
					float perpendicularFaceVelocities_GLOBAL2 = perpendicularFaceVelocities_GLOBAL[faceIndex];
					computed += norm.X * (double)perpendicularFaceVelocities_GLOBAL2 * _hmc.FaceInvLengths[faceIndex];
					computed2 += norm.Y * (double)perpendicularFaceVelocities_GLOBAL2 * _hmc.FaceInvLengths[faceIndex];
					faceVelocityCoef.AddWeightedFaceNormal(norm, (float)_hmc.FaceInvLengths[faceIndex]);
					startLocalFaceIdx2 = facePoint.CCWFaceByLocalIndex(startLocalFaceIdx2);
				}
				while (startLocalFaceIdx2 != endLocalFaceIdx);
				int faceIndex2 = facePoint.Faces[endLocalFaceIdx];
				if (!faceWSEL_GLOBAL[faceIndex2].IsHydraulicallyConnected)
				{
					Vector norm2 = _hmc.FaceNormal[faceIndex2];
					float perpendicularFaceVelocities_GLOBAL3 = perpendicularFaceVelocities_GLOBAL[faceIndex2];
					computed += norm2.X * (double)perpendicularFaceVelocities_GLOBAL3 * _hmc.FaceInvLengths[faceIndex2];
					computed2 += norm2.Y * (double)perpendicularFaceVelocities_GLOBAL3 * _hmc.FaceInvLengths[faceIndex2];
					faceVelocityCoef.AddWeightedFaceNormal(norm2, (float)_hmc.FaceInvLengths[faceIndex2]);
				}
				faceVelocityCoef.Complete();
				facePointVelocity[j] = new Vector(faceVelocityCoef.A22 * computed - faceVelocityCoef.A12 * computed2, (0.0 - faceVelocityCoef.A12) * computed + faceVelocityCoef.A11 * computed2);
			}
		}

		public Vector Velocity_FacePerpLeastSquares_Weighted_Local(int nIdx, Span<float> perpendicularFaceVelocities_LOCAL)
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			FaceVelocityCoef faceVelocityCoef = default;
			double computed = 0.0;
			double computed2 = 0.0;
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				int faceIndex = facePoint.Faces[i];
				Vector norm = _hmc.FaceNormal[faceIndex];
				float perpendicularFaceVelocities_LOCAL2 = perpendicularFaceVelocities_LOCAL[i];
				computed += norm.X * (double)perpendicularFaceVelocities_LOCAL2 * _hmc.FaceInvLengths[faceIndex];
				computed2 += norm.Y * (double)perpendicularFaceVelocities_LOCAL2 * _hmc.FaceInvLengths[faceIndex];
				faceVelocityCoef.AddWeightedFaceNormal(norm, (float)_hmc.FaceInvLengths[faceIndex]);
			}
			faceVelocityCoef.Complete();
			return faceVelocityCoef.Compute(computed, computed2);
		}

		private void GetHydraulicallyConnectedArc(int fpIdx, int localFaceIdx, Span<FaceValues> faceWSs, out int startLocalFaceIdx, out int endLocalFaceIdx)
		{
			FacePoint facePoint = Mesh.FacePoints[fpIdx];
			endLocalFaceIdx = localFaceIdx;
			do
			{
				endLocalFaceIdx = facePoint.CCWFaceByLocalIndex(endLocalFaceIdx);
			}
			while (faceWSs[facePoint.Faces[endLocalFaceIdx]].IsHydraulicallyConnected && endLocalFaceIdx != localFaceIdx);
			startLocalFaceIdx = localFaceIdx;
			if (startLocalFaceIdx != endLocalFaceIdx)
			{
				while (faceWSs[facePoint.Faces[startLocalFaceIdx]].IsHydraulicallyConnected)
				{
					startLocalFaceIdx = facePoint.CWFaceByLocalIndex(startLocalFaceIdx);
				}
			}
		}

		public float[] HydraulicDepth(Span<float> faceHydrDepths, Func<int, Point> getFaceLoc)
		{
			float[] floatValues = new float[_hmc.Mesh.NodeCount];
			HydraulicDepth(faceHydrDepths, getFaceLoc, floatValues);
			return floatValues;
		}

		public void HydraulicDepth(Span<float> faceHydrDepths, Func<int, Point> getFaceLoc, Span<float> outNodeHydDepth)
		{
			PlanarRegression pz = new PlanarRegression();
			for (int i = 0; i < _hmc.NodeCount; i++)
			{
				outNodeHydDepth[i] = HydraulicDepth(i, faceHydrDepths, getFaceLoc, pz);
			}
		}

		public float HydraulicDepth(int nIdx, Span<float> faceHydrDepths, Func<int, Point> getFaceLoc)
		{
			return HydraulicDepth(nIdx, faceHydrDepths, getFaceLoc, new PlanarRegression());
		}

		private float HydraulicDepth(int nIdx, Span<float> faceHydrDepths, Func<int, Point> getFaceLoc, PlanarRegression pz)
		{
			FacePoint facePoint = _hmc.Mesh.FacePoints[nIdx];
			pz.Clear(facePoint.Point);
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				int faceIndex = facePoint.Faces[i];
				float faceHydrDepth = faceHydrDepths[faceIndex];
				if (faceHydrDepth != 0f)
				{
					pz.Add(getFaceLoc(faceIndex), faceHydrDepth);
				}
			}
			if (pz.Count == 0)
			{
				return 0f;
			}
			return (float)pz.SolveZ();
		}

		public float[] DV_FacePerpLeastSquares_Weighted(int nIdx, Span<float> faceDVs, Span<FaceValues> faceWSs, Func<int, Point> getFaceApplicationPoint)
		{
			FacePoint facePoint = Mesh.FacePoints[nIdx];
			float[] floatValues = new float[facePoint.Faces.Count];
			bool condition = true;
			for (int i = 0; i < facePoint.Faces.Count; i++)
			{
				int index = facePoint.Faces[i];
				if ((double)faceWSs[index].ValueA != -9999.0)
				{
					condition = false;
				}
				if ((double)faceWSs[index].ValueB != -9999.0)
				{
					condition = false;
				}
			}
			if (condition)
			{
				return floatValues;
			}
			PlanarRegression planarRegression = new PlanarRegression(facePoint.Point);
			for (int j = 0; j < facePoint.Faces.Count; j++)
			{
				planarRegression.Clear();
				GetHydraulicallyConnectedArc(nIdx, j, faceWSs, out var startLocalFaceIdx, out var endLocalFaceIdx);
				int startLocalFaceIdx2 = startLocalFaceIdx;
				do
				{
					int faceIndex = facePoint.Faces[startLocalFaceIdx2];
					float faceDV = faceDVs[faceIndex];
					planarRegression.Add(getFaceApplicationPoint(faceIndex), faceDV);
					startLocalFaceIdx2 = facePoint.CCWFaceByLocalIndex(startLocalFaceIdx2);
				}
				while (startLocalFaceIdx2 != endLocalFaceIdx);
				int faceIndex2 = facePoint.Faces[endLocalFaceIdx];
				if (!faceWSs[faceIndex2].IsHydraulicallyConnected)
				{
					_ = ref _hmc.FaceNormal[faceIndex2];
					float faceDV2 = faceDVs[faceIndex2];
					planarRegression.Add(getFaceApplicationPoint(faceIndex2), faceDV2);
				}
				floatValues[j] = (float)planarRegression.SolveZ();
			}
			return floatValues;
		}
	}

	public sealed class NodeFaceInterpolationWorkspace
	{
		private readonly NodeFaceInterpolationGeometry _geometry;

		private readonly bool[] _faceWet;

		private readonly bool[] _nodeDirty;

		private readonly List<int> _dirtyNodes = new List<int>();

		private NodeFanInterpolation[] _nodeInterpolations;

		private bool _prepared;

		internal NodeFaceInterpolationGeometry Geometry => _geometry;

		public int ValueCount => _geometry.ValueCount;

		public bool IsPrepared => _prepared;

		internal NodeFaceInterpolationWorkspace(NodeFaceInterpolationGeometry geometry)
		{
			_geometry = geometry;
			_faceWet = new bool[geometry.Mesh.FaceCount];
			_nodeDirty = new bool[geometry.Mesh.NodeCount];
			_nodeInterpolations = new NodeFanInterpolation[geometry.Mesh.NodeCount];
		}

		public int Prepare(ReadOnlySpan<double> faceAreas)
		{
			if (faceAreas.Length != _geometry.Mesh.FaceCount)
			{
				throw new ArgumentException("Face areas must match the mesh face count.", "faceAreas");
			}
			_dirtyNodes.Clear();
			if (!_prepared)
			{
				for (int i = 0; i < faceAreas.Length; i++)
				{
					_faceWet[i] = IsWet(faceAreas[i]);
				}
				for (int j = 0; j < _geometry.Mesh.NodeCount; j++)
				{
					MarkNodeDirty(j);
				}
			}
			else
			{
				for (int k = 0; k < faceAreas.Length; k++)
				{
					bool isWet = IsWet(faceAreas[k]);
					if (isWet != _faceWet[k])
					{
						_faceWet[k] = isWet;
						Face face = _geometry.Mesh.Faces[k];
						MarkNodeDirty(face.fpA);
						MarkNodeDirty(face.fpB);
					}
				}
			}
			foreach (int dirtyNode in _dirtyNodes)
			{
				_nodeInterpolations[dirtyNode] = _geometry.BuildNodeInterpolation(dirtyNode, _faceWet);
				_nodeDirty[dirtyNode] = false;
			}
			_prepared = true;
			return _dirtyNodes.Count;
		}

		public bool WetStatusMatches(ReadOnlySpan<double> faceAreas)
		{
			if (!_prepared || faceAreas.Length != _faceWet.Length)
			{
				return false;
			}
			for (int i = 0; i < faceAreas.Length; i++)
			{
				if (_faceWet[i] != IsWet(faceAreas[i]))
				{
					return false;
				}
			}
			return true;
		}

		public bool FaceIsWet(int face)
		{
			if (!_prepared)
			{
				throw new InvalidOperationException("Node-face interpolation topology is not prepared.");
			}
			return _faceWet[face];
		}

		public int FaceValueIndexA(int face)
		{
			return _geometry.FaceValueIndexA[face];
		}

		public int FaceValueIndexB(int face)
		{
			return _geometry.FaceValueIndexB[face];
		}

		public int NodeFaceValueIndex(int node, int localFace)
		{
			if (node < 0 || node >= _geometry.Mesh.NodeCount)
			{
				throw new ArgumentOutOfRangeException("node");
			}
			if (localFace < 0 || localFace >= _geometry.Mesh.FacePoints[node].Faces.Count)
			{
				throw new ArgumentOutOfRangeException("localFace");
			}
			return _geometry.NodeFaceOffsets[node] + localFace;
		}

		public void Interpolate(ReadOnlySpan<double> cellValues, Span<double> nodeFaceValues)
		{
			ValidateInterpolationBuffers(cellValues, nodeFaceValues);
			for (int i = 0; i < _geometry.Mesh.NodeCount; i++)
			{
				InterpolateNodeCore(i, cellValues, nodeFaceValues);
			}
		}

		public void InterpolateNode(int node, ReadOnlySpan<double> cellValues, Span<double> nodeFaceValues)
		{
			ValidateInterpolationBuffers(cellValues, nodeFaceValues);
			if (node < 0 || node >= _geometry.Mesh.NodeCount)
			{
				throw new ArgumentOutOfRangeException("node");
			}
			InterpolateNodeCore(node, cellValues, nodeFaceValues);
		}

		public NodeFaceInterpolationWorkspace Clone()
		{
			NodeFaceInterpolationWorkspace nodeFaceInterpolationWorkspace = new NodeFaceInterpolationWorkspace(_geometry)
			{
				_prepared = _prepared,
				_nodeInterpolations = (NodeFanInterpolation[])_nodeInterpolations.Clone()
			};
			Array.Copy(_faceWet, nodeFaceInterpolationWorkspace._faceWet, _faceWet.Length);
			return nodeFaceInterpolationWorkspace;
		}

		public void CopyFrom(NodeFaceInterpolationWorkspace source)
		{
			ArgumentNullException.ThrowIfNull(source, "source");
			if (source._geometry != _geometry)
			{
				throw new ArgumentException("The interpolation workspace belongs to a different mesh.", "source");
			}
			_prepared = source._prepared;
			Array.Copy(source._faceWet, _faceWet, _faceWet.Length);
			Array.Copy(source._nodeInterpolations, _nodeInterpolations, _nodeInterpolations.Length);
			_dirtyNodes.Clear();
			Array.Clear(_nodeDirty);
		}

		private static bool IsWet(double faceArea)
		{
			if (faceArea > 0.0)
			{
				return double.IsFinite(faceArea);
			}
			return false;
		}

		private void MarkNodeDirty(int node)
		{
			if (node >= 0 && !_nodeDirty[node])
			{
				_nodeDirty[node] = true;
				_dirtyNodes.Add(node);
			}
		}

		private void ValidateInterpolationBuffers(ReadOnlySpan<double> cellValues, Span<double> nodeFaceValues)
		{
			if (!_prepared)
			{
				throw new InvalidOperationException("Node-face interpolation topology is not prepared.");
			}
			if (cellValues.Length != _geometry.Mesh.CellCount)
			{
				throw new ArgumentException("Cell values must match the mesh cell count.", "cellValues");
			}
			if (nodeFaceValues.Length != ValueCount)
			{
				throw new ArgumentException("Node-face values must match the mesh incidence count.", "nodeFaceValues");
			}
		}

		private void InterpolateNodeCore(int node, ReadOnlySpan<double> cellValues, Span<double> nodeFaceValues)
		{
			NodeFanInterpolation nodeFanInterpolation = _nodeInterpolations[node];
			int nodeFaceOffset = _geometry.NodeFaceOffsets[node];
			int faceFanLength = nodeFanInterpolation.FaceFans.Length;
			nodeFaceValues.Slice(nodeFaceOffset, faceFanLength).Fill(double.NaN);
			if (nodeFanInterpolation.Fans.Length == 0)
			{
				return;
			}
			Span<double> doubleSpan = ((nodeFanInterpolation.Fans.Length > 64) ? ((Span<double>)new double[nodeFanInterpolation.Fans.Length]) : stackalloc double[nodeFanInterpolation.Fans.Length]);
			Span<double> doubleSpan2 = doubleSpan;
			for (int i = 0; i < nodeFanInterpolation.Fans.Length; i++)
			{
				FanInterpolation fanInterpolation = nodeFanInterpolation.Fans[i];
				double computed = 0.0;
				for (int j = 0; j < fanInterpolation.Cells.Length; j++)
				{
					computed += fanInterpolation.Weights[j] * cellValues[fanInterpolation.Cells[j]];
				}
				doubleSpan2[i] = computed;
			}
			for (int k = 0; k < faceFanLength; k++)
			{
				int faceFan = nodeFanInterpolation.FaceFans[k];
				if (faceFan >= 0)
				{
					nodeFaceValues[nodeFaceOffset + k] = doubleSpan2[faceFan];
				}
			}
		}

		internal NodeFanInterpolation NodeInterpolation(int node)
		{
			return _nodeInterpolations[node];
		}

		internal int NodeFaceOffset(int node)
		{
			return _geometry.NodeFaceOffsets[node];
		}
	}

	internal sealed class NodeFaceInterpolationGeometry
	{
		private const double SingularTolerance = 1E-14;

		private readonly NodeInterpolationGeometry[] _nodes;

		private readonly NodeFanInterpolation[] _fullyWetInterpolations;

		public Mesh Mesh { get; }

		public int[] NodeFaceOffsets { get; }

		public int[] FaceValueIndexA { get; }

		public int[] FaceValueIndexB { get; }

		public int ValueCount => NodeFaceOffsets[^1];

		public NodeFaceInterpolationGeometry(HydraulicMeshComputer hmesh)
		{
			Mesh = hmesh.Mesh;
			NodeFaceOffsets = new int[Mesh.NodeCount + 1];
			FaceValueIndexA = new int[Mesh.FaceCount];
			FaceValueIndexB = new int[Mesh.FaceCount];
			Array.Fill(FaceValueIndexA, -1);
			Array.Fill(FaceValueIndexB, -1);
			_nodes = new NodeInterpolationGeometry[Mesh.NodeCount];
			_fullyWetInterpolations = new NodeFanInterpolation[Mesh.NodeCount];
			List<int> intList = new List<int>();
			for (int i = 0; i < Mesh.NodeCount; i++)
			{
				FacePoint facePoint = Mesh.FacePoints[i];
				NodeFaceOffsets[i] = ((i != 0) ? (NodeFaceOffsets[i - 1] + Mesh.FacePoints[i - 1].Faces.Count) : 0);
				int[] faces = facePoint.Faces.ToArray();
				Mesh.GetCellsAroundFacepoint(i, intList);
				List<int> intListList = new List<int>();
				foreach (int ordinal in intList)
				{
					if (IsRealCell(ordinal) && !intListList.Contains(ordinal))
					{
						intListList.Add(ordinal);
					}
				}
				int[] intValues = new int[faces.Length];
				int[] intValuesArray = new int[faces.Length];
				Array.Fill(intValues, -1);
				Array.Fill(intValuesArray, -1);
				for (int j = 0; j < faces.Length; j++)
				{
					int faceIndex = faces[j];
					Face face = Mesh.Faces[faceIndex];
					int nodeFaceOffsetsTerm = NodeFaceOffsets[i] + j;
					if (face.fpA == i)
					{
						FaceValueIndexA[faceIndex] = nodeFaceOffsetsTerm;
					}
					else
					{
						if (face.fpB != i)
						{
							throw new InvalidOperationException("A node contains a face that does not reference it.");
						}
						FaceValueIndexB[faceIndex] = nodeFaceOffsetsTerm;
					}
					intValues[j] = intListList.IndexOf(face.cellA);
					intValuesArray[j] = intListList.IndexOf(face.cellB);
				}
				NodeInterpolationGeometry nodeInterpolationGeometry = new NodeInterpolationGeometry(faces, intListList.ToArray(), intValues, intValuesArray);
				_nodes[i] = nodeInterpolationGeometry;
				_fullyWetInterpolations[i] = BuildFullyWetInterpolation(i, nodeInterpolationGeometry);
			}
			NodeFaceOffsets[Mesh.NodeCount] = ((Mesh.NodeCount != 0) ? (NodeFaceOffsets[Mesh.NodeCount - 1] + Mesh.FacePoints[^1].Faces.Count) : 0);
			for (int k = 0; k < Mesh.FaceCount; k++)
			{
				if (FaceValueIndexA[k] < 0 || FaceValueIndexB[k] < 0)
				{
					throw new InvalidOperationException("Every face must have one value slot at each endpoint.");
				}
			}
		}

		public NodeFanInterpolation BuildNodeInterpolation(int node, bool[] faceWet)
		{
			NodeInterpolationGeometry nodeInterpolationGeometry = _nodes[node];
			bool faceLengthIsNonZero = nodeInterpolationGeometry.Faces.Length != 0;
			for (int i = 0; i < nodeInterpolationGeometry.Faces.Length; i++)
			{
				faceLengthIsNonZero &= faceWet[nodeInterpolationGeometry.Faces[i]];
			}
			if (faceLengthIsNonZero)
			{
				return _fullyWetInterpolations[node];
			}
			int cellLength = nodeInterpolationGeometry.Cells.Length;
			int[] intValues = new int[cellLength];
			for (int j = 0; j < cellLength; j++)
			{
				intValues[j] = j;
			}
			for (int k = 0; k < nodeInterpolationGeometry.Faces.Length; k++)
			{
				if (faceWet[nodeInterpolationGeometry.Faces[k]])
				{
					int faceCellA = nodeInterpolationGeometry.FaceCellA[k];
					int faceCellB = nodeInterpolationGeometry.FaceCellB[k];
					if (faceCellA >= 0 && faceCellB >= 0)
					{
						Union(intValues, faceCellA, faceCellB);
					}
				}
			}
			int[] intValuesArray = new int[nodeInterpolationGeometry.Faces.Length];
			Array.Fill(intValuesArray, -1);
			List<int> intList = new List<int>();
			for (int l = 0; l < nodeInterpolationGeometry.Faces.Length; l++)
			{
				if (!faceWet[nodeInterpolationGeometry.Faces[l]])
				{
					continue;
				}
				int faceCellA2 = ((nodeInterpolationGeometry.FaceCellA[l] >= 0) ? nodeInterpolationGeometry.FaceCellA[l] : nodeInterpolationGeometry.FaceCellB[l]);
				if (faceCellA2 >= 0)
				{
					int find = Find(intValues, faceCellA2);
					int indexOf = intList.IndexOf(find);
					if (indexOf < 0)
					{
						indexOf = intList.Count;
						intList.Add(find);
					}
					intValuesArray[l] = indexOf;
				}
			}
			FanInterpolation[] fanInterpolations = new FanInterpolation[intList.Count];
			for (int m = 0; m < intList.Count; m++)
			{
				List<int> intListList = new List<int>();
				for (int n = 0; n < nodeInterpolationGeometry.Cells.Length; n++)
				{
					if (Find(intValues, n) == intList[m])
					{
						intListList.Add(nodeInterpolationGeometry.Cells[n]);
					}
				}
				fanInterpolations[m] = new FanInterpolation(intListList.ToArray(), ComputeNodeWeights(node, intListList));
			}
			return new NodeFanInterpolation(intValuesArray, fanInterpolations);
		}

		private NodeFanInterpolation BuildFullyWetInterpolation(int node, NodeInterpolationGeometry geometry)
		{
			int[] intValues = new int[geometry.Faces.Length];
			if (geometry.Cells.Length == 0)
			{
				Array.Fill(intValues, -1);
				return new NodeFanInterpolation(intValues, Array.Empty<FanInterpolation>());
			}
			List<int> cells = geometry.Cells.ToList();
			FanInterpolation fanInterpolation = new FanInterpolation(geometry.Cells, ComputeNodeWeights(node, cells));
			return new NodeFanInterpolation(intValues, new FanInterpolation[1] { fanInterpolation });
		}

		private double[] ComputeNodeWeights(int nodeIndex, List<int> cells)
		{
			int count = cells.Count;
			double[] doubleValues = new double[count];
			if (count == 0)
			{
				return doubleValues;
			}
			Point point = Mesh.FacePoints[nodeIndex].Point;
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			double computed6 = 0.0;
			for (int i = 0; i < count; i++)
			{
				Point point2 = Mesh.CellCenters[cells[i]];
				double point2PointTerm = point2.X - point.X;
				double point2PointTerm2 = point2.Y - point.Y;
				double computed7 = Math.Sqrt(point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2);
				if (!(computed7 > 0.0) || !double.IsFinite(computed7))
				{
					double[] doubleValuesArray = new double[count];
					doubleValuesArray[i] = 1.0;
					return doubleValuesArray;
				}
				double computed8 = (doubleValues[i] = 1.0 / computed7);
				computed += computed8;
				computed2 += computed8 * point2PointTerm;
				computed3 += computed8 * point2PointTerm2;
				computed4 += computed8 * point2PointTerm * point2PointTerm;
				computed5 += computed8 * point2PointTerm * point2PointTerm2;
				computed6 += computed8 * point2PointTerm2 * point2PointTerm2;
			}
			double computed9 = computed4 * computed6 - computed5 * computed5;
			double computed10 = computed4 + computed6;
			if (!(Math.Abs(computed9) > 1E-14 * computed10 * computed10))
			{
				return NormalizeInverseDistance(doubleValues, computed);
			}
			double computed11 = ((0.0 - computed2) * computed6 + computed3 * computed5) / computed9;
			double computed12 = ((0.0 - computed3) * computed4 + computed2 * computed5) / computed9;
			double[] doubleValues2 = new double[count];
			double computed13 = 0.0;
			for (int j = 0; j < count; j++)
			{
				Point point3 = Mesh.CellCenters[cells[j]];
				double point3PointTerm = point3.X - point.X;
				double point3PointTerm2 = point3.Y - point.Y;
				double computed14 = doubleValues[j] * (1.0 + computed11 * point3PointTerm + computed12 * point3PointTerm2);
				if (!double.IsFinite(computed14))
				{
					return NormalizeInverseDistance(doubleValues, computed);
				}
				doubleValues2[j] = computed14;
				computed13 += computed14;
			}
			if (!(Math.Abs(computed13) > 1E-14) || !double.IsFinite(computed13))
			{
				return NormalizeInverseDistance(doubleValues, computed);
			}
			for (int k = 0; k < count; k++)
			{
				doubleValues2[k] /= computed13;
			}
			return doubleValues2;
		}

		private bool IsRealCell(int cell)
		{
			if (cell >= 0 && cell < Mesh.CellCount)
			{
				return !Mesh.CellIsVirtual(cell);
			}
			return false;
		}

		private static double[] NormalizeInverseDistance(double[] weights, double sum)
		{
			if (!(sum > 0.0) || !double.IsFinite(sum))
			{
				return weights;
			}
			for (int i = 0; i < weights.Length; i++)
			{
				weights[i] /= sum;
			}
			return weights;
		}

		private static int Find(int[] parents, int cell)
		{
			int cell2 = cell;
			while (parents[cell2] != cell2)
			{
				cell2 = parents[cell2];
			}
			while (parents[cell] != cell)
			{
				int parent = parents[cell];
				parents[cell] = cell2;
				cell = parent;
			}
			return cell2;
		}

		private static void Union(int[] parents, int cellA, int cellB)
		{
			int parents2 = Find(parents, cellA);
			int find = Find(parents, cellB);
			if (parents2 != find)
			{
				parents[find] = parents2;
			}
		}
	}

	private sealed class NodeInterpolationGeometry
	{
		public readonly int[] Faces;

		public readonly int[] Cells;

		public readonly int[] FaceCellA;

		public readonly int[] FaceCellB;

		public NodeInterpolationGeometry(int[] faces, int[] cells, int[] faceCellA, int[] faceCellB)
		{
			Faces = faces;
			Cells = cells;
			FaceCellA = faceCellA;
			FaceCellB = faceCellB;
		}
	}

	internal sealed class NodeFanInterpolation
	{
		public readonly int[] FaceFans;

		public readonly FanInterpolation[] Fans;

		public NodeFanInterpolation(int[] faceFans, FanInterpolation[] fans)
		{
			FaceFans = faceFans;
			Fans = fans;
		}
	}

	internal sealed class FanInterpolation
	{
		public readonly int[] Cells;

		public readonly double[] Weights;

		public FanInterpolation(int[] cells, double[] weights)
		{
			Cells = cells;
			Weights = weights;
		}
	}

	public sealed class TVDVirtualPointInterpolationWorkspace
	{
		private const double SingularTolerance = 1E-14;

		private const double LineInterpolationTolerance = 1E-11;

		private const byte InvalidTVDRow = 0;

		private const byte FullyWetTVDRow = 1;

		private const byte PartiallyWetTVDRow = 2;

		private readonly TVDVirtualPointInterpolationGeometry _geometry;

		private readonly bool[] _cellWet;

		private readonly bool[] _faceWet;

		private readonly bool[] _rowDirty;

		private readonly List<int> _dirtyRows = new List<int>();

		private readonly int[] _visitStamp;

		private readonly int[] _queue;

		private double[] _TVDStencilCoefficientOverrides;

		private readonly byte[] _TVDRowModes;

		private int _stamp;

		private bool _prepared;

		private int _fullyWetTVDRowCount;

		private int _partiallyWetTVDRowCount;

		internal TVDVirtualPointInterpolationGeometry Geometry => _geometry;

		public bool IsPrepared => _prepared;

		public int TVDRowCount => _geometry.RowCount;

		public int FullyWetTVDRowCount => _fullyWetTVDRowCount;

		public int PartiallyWetTVDRowCount => _partiallyWetTVDRowCount;

		public int InvalidTVDRowCount => TVDRowCount - _fullyWetTVDRowCount - _partiallyWetTVDRowCount;

		public ReadOnlySpan<int> TVDStencilRowPointers => _geometry.TVDStencilRowPointers;

		public ReadOnlySpan<int> TVDStencilCellIndices => _geometry.TVDStencilCellIndices;

		public ReadOnlySpan<int> TVDPathCellRowPointers => _geometry.TVDPathCellRowPointers;

		public ReadOnlySpan<int> TVDPathCellIndices => _geometry.TVDPathCellIndices;

		public ReadOnlySpan<int> TVDPathFaceRowPointers => _geometry.TVDPathFaceRowPointers;

		public ReadOnlySpan<int> TVDPathFaceIndices => _geometry.TVDPathFaceIndices;

		internal TVDVirtualPointInterpolationWorkspace(TVDVirtualPointInterpolationGeometry geometry)
		{
			_geometry = geometry;
			_cellWet = new bool[geometry.Mesh.CellCount];
			_faceWet = new bool[geometry.Mesh.FaceCount];
			_rowDirty = new bool[geometry.RowCount];
			_visitStamp = new int[geometry.Mesh.CellCount];
			_queue = new int[Math.Max(geometry.MaxTVDStencilSize, 1)];
			_TVDRowModes = new byte[geometry.RowCount];
		}

		public int Prepare(ReadOnlySpan<double> cellVolumes, ReadOnlySpan<double> faceAreas, bool[] cellActive = null, bool[] faceActive = null)
		{
			ValidateState(cellVolumes, faceAreas, cellActive, faceActive);
			_dirtyRows.Clear();
			bool notPrepared = !_prepared;
			if (!_prepared)
			{
				for (int i = 0; i < _cellWet.Length; i++)
				{
					_cellWet[i] = IsWet(cellVolumes[i], cellActive, i);
					notPrepared &= _cellWet[i];
				}
				for (int j = 0; j < _faceWet.Length; j++)
				{
					_faceWet[j] = IsWet(faceAreas[j], faceActive, j);
					notPrepared &= _faceWet[j];
				}
				for (int k = 0; k < _geometry.RowCount; k++)
				{
					MarkRowDirty(k);
				}
			}
			else
			{
				for (int l = 0; l < _cellWet.Length; l++)
				{
					bool isWet = IsWet(cellVolumes[l], cellActive, l);
					if (isWet != _cellWet[l])
					{
						_cellWet[l] = isWet;
						MarkDependentRows(l, _geometry.TVDCellRowPointers, _geometry.TVDCellRows);
					}
				}
				for (int m = 0; m < _faceWet.Length; m++)
				{
					bool isWet2 = IsWet(faceAreas[m], faceActive, m);
					if (isWet2 != _faceWet[m])
					{
						_faceWet[m] = isWet2;
						MarkDependentRows(m, _geometry.TVDFaceRowPointers, _geometry.TVDFaceRows);
					}
				}
			}
			foreach (int dirtyRow in _dirtyRows)
			{
				if (notPrepared && _geometry.TVDContainingCells[dirtyRow] >= 0)
				{
					SetTVDRowMode(dirtyRow, 1);
				}
				else
				{
					BuildTVDRow(dirtyRow);
				}
				_rowDirty[dirtyRow] = false;
			}
			_prepared = true;
			return _dirtyRows.Count;
		}

		public bool WetStatusMatches(ReadOnlySpan<double> cellVolumes, ReadOnlySpan<double> faceAreas, bool[] cellActive = null, bool[] faceActive = null)
		{
			if (!_prepared || cellVolumes.Length != _cellWet.Length || faceAreas.Length != _faceWet.Length)
			{
				return false;
			}
			if (cellActive != null && cellActive.Length != _cellWet.Length)
			{
				return false;
			}
			if (faceActive != null && faceActive.Length != _faceWet.Length)
			{
				return false;
			}
			for (int i = 0; i < _cellWet.Length; i++)
			{
				if (_cellWet[i] != IsWet(cellVolumes[i], cellActive, i))
				{
					return false;
				}
			}
			for (int j = 0; j < _faceWet.Length; j++)
			{
				if (_faceWet[j] != IsWet(faceAreas[j], faceActive, j))
				{
					return false;
				}
			}
			return true;
		}

		public bool TryInterpolateTVDVirtualPoint(int face, bool aIsUpwind, ReadOnlySpan<double> cellValues, out double TVDVirtualPointValue)
		{
			if (!_prepared)
			{
				throw new InvalidOperationException("TVD virtual-point interpolation is not prepared.");
			}
			if (face < 0 || face >= _geometry.Mesh.FaceCount)
			{
				throw new ArgumentOutOfRangeException("face");
			}
			if (cellValues.Length != _geometry.Mesh.CellCount)
			{
				throw new ArgumentException("Cell values must match the mesh cell count.", "cellValues");
			}
			return TryInterpolateTVDVirtualPointUnchecked(face, aIsUpwind, cellValues, out TVDVirtualPointValue);
		}

		internal bool TryInterpolateTVDVirtualPointUnchecked(int face, bool aIsUpwind, ReadOnlySpan<double> cellValues, out double TVDVirtualPointValue)
		{
			int tvdRow = TVDRow(face, aIsUpwind);
			byte b = _TVDRowModes[tvdRow];
			if (b == 0)
			{
				TVDVirtualPointValue = 0.0;
				return false;
			}
			double tvdFullyWetCoefficientTimesCellValueSum = 0.0;
			double positiveInfinity = double.PositiveInfinity;
			double negativeInfinity = double.NegativeInfinity;
			int tvdStencilRowPointerStart = _geometry.TVDStencilRowPointers[tvdRow];
			int tvdStencilRowPointerEnd = _geometry.TVDStencilRowPointers[tvdRow + 1];
			for (int i = tvdStencilRowPointerStart; i < tvdStencilRowPointerEnd; i++)
			{
				double tvdFullyWetCoefficient = ((b == 1) ? _geometry.TVDFullyWetCoefficients[i] : _TVDStencilCoefficientOverrides[i]);
				if (tvdFullyWetCoefficient != 0.0)
				{
					double cellValue = cellValues[_geometry.TVDStencilCellIndices[i]];
					if (!double.IsFinite(cellValue))
					{
						TVDVirtualPointValue = 0.0;
						return false;
					}
					tvdFullyWetCoefficientTimesCellValueSum += tvdFullyWetCoefficient * cellValue;
					positiveInfinity = Math.Min(positiveInfinity, cellValue);
					negativeInfinity = Math.Max(negativeInfinity, cellValue);
				}
			}
			if (!double.IsFinite(tvdFullyWetCoefficientTimesCellValueSum) || !double.IsFinite(positiveInfinity) || !double.IsFinite(negativeInfinity))
			{
				TVDVirtualPointValue = 0.0;
				return false;
			}
			TVDVirtualPointValue = Math.Clamp(tvdFullyWetCoefficientTimesCellValueSum, positiveInfinity, negativeInfinity);
			return true;
		}

		public int CopyTVDStencilCoefficients(int face, bool aIsUpwind, Span<double> destination)
		{
			if (!_prepared)
			{
				throw new InvalidOperationException("TVD virtual-point interpolation is not prepared.");
			}
			if (face < 0 || face >= _geometry.Mesh.FaceCount)
			{
				throw new ArgumentOutOfRangeException("face");
			}
			int tvdRow = TVDRow(face, aIsUpwind);
			int tvdStencilRowPointer = _geometry.TVDStencilRowPointers[tvdRow];
			int tvdStencilRowPointersGeometryTerm = _geometry.TVDStencilRowPointers[tvdRow + 1] - tvdStencilRowPointer;
			if (destination.Length < tvdStencilRowPointersGeometryTerm)
			{
				throw new ArgumentException("Destination is shorter than the TVD stencil row.", "destination");
			}
			switch (_TVDRowModes[tvdRow])
			{
			case 0:
				destination.Slice(0, tvdStencilRowPointersGeometryTerm).Clear();
				break;
			case 1:
				_geometry.TVDFullyWetCoefficients.AsSpan(tvdStencilRowPointer, tvdStencilRowPointersGeometryTerm).CopyTo(destination);
				break;
			default:
				_TVDStencilCoefficientOverrides.AsSpan(tvdStencilRowPointer, tvdStencilRowPointersGeometryTerm).CopyTo(destination);
				break;
			}
			return tvdStencilRowPointersGeometryTerm;
		}

		public bool TryGetTVDVirtualPointGeometry(int face, bool aIsUpwind, out Point TVDVirtualPoint, out int containingCell)
		{
			if (face < 0 || face >= _geometry.Mesh.FaceCount)
			{
				throw new ArgumentOutOfRangeException("face");
			}
			int tvdRow = TVDRow(face, aIsUpwind);
			containingCell = _geometry.TVDContainingCells[tvdRow];
			TVDVirtualPoint = _geometry.TVDVirtualPoints[tvdRow];
			return containingCell >= 0;
		}

		private static int TVDRow(int face, bool aIsUpwind)
		{
			return 2 * face + ((!aIsUpwind) ? 1 : 0);
		}

		private void BuildTVDRow(int row)
		{
			int tvdStencilRowPointer = _geometry.TVDStencilRowPointers[row];
			int tvdStencilRowPointer2 = _geometry.TVDStencilRowPointers[row + 1];
			SetTVDRowMode(row, 0);
			int scaledRow = row / 2;
			Face face = _geometry.Mesh.Faces[scaledRow];
			int cellA = (((row & 1) == 0) ? face.cellA : face.cellB);
			int cellB = (((row & 1) == 0) ? face.cellB : face.cellA);
			if (cellA < 0 || cellB < 0 || !_faceWet[scaledRow] || !_cellWet[cellA] || !_cellWet[cellB])
			{
				return;
			}
			int tvdContainingCellIndex = _geometry.TVDContainingCells[row];
			if (tvdContainingCellIndex < 0 || !_cellWet[tvdContainingCellIndex])
			{
				return;
			}
			int tvdPathCellRowPointerStart = _geometry.TVDPathCellRowPointers[row];
			int tvdPathCellRowPointerEnd = _geometry.TVDPathCellRowPointers[row + 1];
			for (int i = tvdPathCellRowPointerStart; i < tvdPathCellRowPointerEnd; i++)
			{
				if (!_cellWet[_geometry.TVDPathCellIndices[i]])
				{
					return;
				}
			}
			int tvdPathFaceRowPointerStart = _geometry.TVDPathFaceRowPointers[row];
			int tvdPathFaceRowPointerEnd = _geometry.TVDPathFaceRowPointers[row + 1];
			for (int j = tvdPathFaceRowPointerStart; j < tvdPathFaceRowPointerEnd; j++)
			{
				if (!_faceWet[_geometry.TVDPathFaceIndices[j]])
				{
					return;
				}
			}
			int visitStamp = NextStamp();
			int ordinal = 0;
			int ordinal2 = 0;
			_queue[ordinal2++] = tvdContainingCellIndex;
			_visitStamp[tvdContainingCellIndex] = visitStamp;
			while (ordinal < ordinal2)
			{
				int ordinal3 = _queue[ordinal++];
				ReadOnlySpan<int> faces = _geometry.Mesh.Cells[ordinal3].Faces.Span;
				for (int k = 0; k < faces.Length; k++)
				{
					int faceIndex = faces[k];
					if (_faceWet[faceIndex])
					{
						Face face2 = _geometry.Mesh.Faces[faceIndex];
						int cellB2 = ((face2.cellA == ordinal3) ? face2.cellB : face2.cellA);
						if (cellB2 >= 0 && _cellWet[cellB2] && _visitStamp[cellB2] != visitStamp && TVDRowContainsCell(tvdStencilRowPointer, tvdStencilRowPointer2, cellB2))
						{
							_visitStamp[cellB2] = visitStamp;
							_queue[ordinal2++] = cellB2;
						}
					}
				}
			}
			if (ordinal2 == tvdStencilRowPointer2 - tvdStencilRowPointer)
			{
				SetTVDRowMode(row, 1);
				return;
			}
			EnsureTVDStencilCoefficientOverrides();
			Array.Clear(_TVDStencilCoefficientOverrides, tvdStencilRowPointer, tvdStencilRowPointer2 - tvdStencilRowPointer);
			if (ComputeTVDWeights(row, tvdStencilRowPointer, tvdStencilRowPointer2, visitStamp))
			{
				SetTVDRowMode(row, 2);
			}
		}

		private bool ComputeTVDWeights(int row, int start, int end, int stamp)
		{
			Point target = _geometry.TVDVirtualPoints[row];
			int ordinal = 0;
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			for (int i = start; i < end; i++)
			{
				int tvdStencilCellIndex = _geometry.TVDStencilCellIndices[i];
				if (_visitStamp[tvdStencilCellIndex] == stamp)
				{
					ordinal++;
					Point point = _geometry.Mesh.CellCenters[tvdStencilCellIndex];
					double pointTargetTerm = point.X - target.X;
					double pointTargetTerm2 = point.Y - target.Y;
					double computed6 = pointTargetTerm * pointTargetTerm + pointTargetTerm2 * pointTargetTerm2;
					if (computed6 == 0.0)
					{
						_TVDStencilCoefficientOverrides[i] = 1.0;
						return true;
					}
					double computed7 = Math.Sqrt(computed6);
					if (!double.IsFinite(computed7))
					{
						return false;
					}
					double computed8 = 1.0 / computed7;
					computed += computed8 * pointTargetTerm;
					computed2 += computed8 * pointTargetTerm2;
					computed3 += computed8 * pointTargetTerm * pointTargetTerm;
					computed4 += computed8 * pointTargetTerm * pointTargetTerm2;
					computed5 += computed8 * pointTargetTerm2 * pointTargetTerm2;
				}
			}
			double computed9 = computed3 * computed5 - computed4 * computed4;
			double computed10 = computed3 + computed5;
			if (ordinal < 3 || !(Math.Abs(computed9) > 1E-14 * computed10 * computed10))
			{
				return TrySetTVDLineWeights(target, start, end, stamp);
			}
			double computed11 = ((0.0 - computed) * computed5 + computed2 * computed4) / computed9;
			double computed12 = ((0.0 - computed2) * computed3 + computed * computed4) / computed9;
			double tvdStencilCoefficientOverridesSum = 0.0;
			for (int j = start; j < end; j++)
			{
				int tvdStencilCellIndex2 = _geometry.TVDStencilCellIndices[j];
				if (_visitStamp[tvdStencilCellIndex2] == stamp)
				{
					Point point2 = _geometry.Mesh.CellCenters[tvdStencilCellIndex2];
					double point2TargetTerm = point2.X - target.X;
					double point2TargetTerm2 = point2.Y - target.Y;
					double computed13 = Math.Sqrt(point2TargetTerm * point2TargetTerm + point2TargetTerm2 * point2TargetTerm2);
					double tvdStencilCoefficientOverrides = 1.0 / computed13 * (1.0 + computed11 * point2TargetTerm + computed12 * point2TargetTerm2);
					if (!double.IsFinite(tvdStencilCoefficientOverrides))
					{
						return false;
					}
					_TVDStencilCoefficientOverrides[j] = tvdStencilCoefficientOverrides;
					tvdStencilCoefficientOverridesSum += tvdStencilCoefficientOverrides;
				}
			}
			if (!(Math.Abs(tvdStencilCoefficientOverridesSum) > 1E-14) || !double.IsFinite(tvdStencilCoefficientOverridesSum))
			{
				return false;
			}
			for (int k = start; k < end; k++)
			{
				_TVDStencilCoefficientOverrides[k] /= tvdStencilCoefficientOverridesSum;
			}
			return true;
		}

		private bool TrySetTVDLineWeights(Point target, int start, int end, int stamp)
		{
			for (int i = start; i < end; i++)
			{
				int tvdStencilCellIndex = _geometry.TVDStencilCellIndices[i];
				if (_visitStamp[tvdStencilCellIndex] != stamp)
				{
					continue;
				}
				Point point = _geometry.Mesh.CellCenters[tvdStencilCellIndex];
				for (int j = i + 1; j < end; j++)
				{
					int tvdStencilCellIndex2 = _geometry.TVDStencilCellIndices[j];
					if (_visitStamp[tvdStencilCellIndex2] != stamp)
					{
						continue;
					}
					Point point2 = _geometry.Mesh.CellCenters[tvdStencilCellIndex2];
					double point2PointTerm = point2.X - point.X;
					double point2PointTerm2 = point2.Y - point.Y;
					double computed = point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2;
					if (!(computed > 0.0) || !double.IsFinite(computed))
					{
						continue;
					}
					double targetPointTerm = target.X - point.X;
					double targetPointTerm2 = target.Y - point.Y;
					double tvdStencilCoefficientOverrides = (targetPointTerm * point2PointTerm + targetPointTerm2 * point2PointTerm2) / computed;
					if (!(tvdStencilCoefficientOverrides < -1E-11) && !(tvdStencilCoefficientOverrides > 1.00000000001))
					{
						double computed2 = targetPointTerm * point2PointTerm2 - targetPointTerm2 * point2PointTerm;
						if (!(computed2 * computed2 > 1E-20 * computed * computed))
						{
							tvdStencilCoefficientOverrides = Math.Clamp(tvdStencilCoefficientOverrides, 0.0, 1.0);
							_TVDStencilCoefficientOverrides[i] = 1.0 - tvdStencilCoefficientOverrides;
							_TVDStencilCoefficientOverrides[j] = tvdStencilCoefficientOverrides;
							return true;
						}
					}
				}
			}
			return false;
		}

		private void EnsureTVDStencilCoefficientOverrides()
		{
			if (_TVDStencilCoefficientOverrides == null)
			{
				_TVDStencilCoefficientOverrides = new double[_geometry.TVDStencilCellIndices.Length];
			}
		}

		private void SetTVDRowMode(int row, byte mode)
		{
			byte b = _TVDRowModes[row];
			if (b != mode)
			{
				switch (b)
				{
				case 1:
					_fullyWetTVDRowCount--;
					break;
				case 2:
					_partiallyWetTVDRowCount--;
					break;
				}
				switch (mode)
				{
				case 1:
					_fullyWetTVDRowCount++;
					break;
				case 2:
					_partiallyWetTVDRowCount++;
					break;
				}
				_TVDRowModes[row] = mode;
			}
		}

		private bool TVDRowContainsCell(int start, int end, int cell)
		{
			for (int i = start; i < end; i++)
			{
				if (_geometry.TVDStencilCellIndices[i] == cell)
				{
					return true;
				}
			}
			return false;
		}

		private int NextStamp()
		{
			if (_stamp < int.MaxValue)
			{
				return ++_stamp;
			}
			Array.Clear(_visitStamp);
			_stamp = 1;
			return _stamp;
		}

		private void MarkDependentRows(int dependency, int[] rowPointers, int[] rows)
		{
			for (int i = rowPointers[dependency]; i < rowPointers[dependency + 1]; i++)
			{
				MarkRowDirty(rows[i]);
			}
		}

		private void MarkRowDirty(int row)
		{
			if (!_rowDirty[row])
			{
				_rowDirty[row] = true;
				_dirtyRows.Add(row);
			}
		}

		private static bool IsWet(double magnitude, bool[] active, int index)
		{
			if (magnitude > 0.0 && double.IsFinite(magnitude))
			{
				if (active != null)
				{
					return active[index];
				}
				return true;
			}
			return false;
		}

		private void ValidateState(ReadOnlySpan<double> cellVolumes, ReadOnlySpan<double> faceAreas, bool[] cellActive, bool[] faceActive)
		{
			if (cellVolumes.Length != _geometry.Mesh.CellCount)
			{
				throw new ArgumentException("Cell volumes must match the mesh cell count.", "cellVolumes");
			}
			if (faceAreas.Length != _geometry.Mesh.FaceCount)
			{
				throw new ArgumentException("Face areas must match the mesh face count.", "faceAreas");
			}
			if (cellActive != null && cellActive.Length != _geometry.Mesh.CellCount)
			{
				throw new ArgumentException("Cell active flags must match the mesh cell count.", "cellActive");
			}
			if (faceActive != null && faceActive.Length != _geometry.Mesh.FaceCount)
			{
				throw new ArgumentException("Face active flags must match the mesh face count.", "faceActive");
			}
		}
	}

	internal sealed class TVDVirtualPointInterpolationGeometry
	{
		private sealed class TVDRowGeometry
		{
			public Point TVDVirtualPoint;

			public int TVDContainingCell = -1;

			public readonly List<int> TVDStencilCells = new List<int>();

			public readonly List<int> TVDPathCells = new List<int>();

			public readonly List<int> TVDPathFaces = new List<int>();

			public readonly HashSet<int> TVDDependencyCells = new HashSet<int>();

			public readonly HashSet<int> TVDDependencyFaces = new HashSet<int>();

			public readonly List<double> TVDFullyWetCoefficients = new List<double>();

			public readonly List<double> TVDBaseWeights = new List<double>();

			public void Clear()
			{
				TVDVirtualPoint = default;
				TVDContainingCell = -1;
				TVDStencilCells.Clear();
				TVDPathCells.Clear();
				TVDPathFaces.Clear();
				TVDDependencyCells.Clear();
				TVDDependencyFaces.Clear();
				TVDFullyWetCoefficients.Clear();
				TVDBaseWeights.Clear();
			}
		}

		private const double MachineEpsilon = 2.220446049250313E-16;

		private const double PathFractionTolerance = 1E-11;

		private readonly List<Segment> _TVDPathFaceSegments = new List<Segment>();

		private readonly List<int> _TVDPathCandidateFaces = new List<int>();

		private readonly HashSet<int> _TVDStencilNodes = new HashSet<int>();

		private readonly HashSet<int> _TVDStencilCells = new HashSet<int>();

		private readonly List<int> _TVDCellsAroundNode = new List<int>();

		public Mesh Mesh { get; }

		public int RowCount => 2 * Mesh.FaceCount;

		public int MaxTVDStencilSize { get; }

		public int[] TVDStencilRowPointers { get; }

		public int[] TVDStencilCellIndices { get; }

		public double[] TVDFullyWetCoefficients { get; }

		public int[] TVDPathCellRowPointers { get; }

		public int[] TVDPathCellIndices { get; }

		public int[] TVDPathFaceRowPointers { get; }

		public int[] TVDPathFaceIndices { get; }

		public int[] TVDContainingCells { get; }

		public Point[] TVDVirtualPoints { get; }

		public int[] TVDCellRowPointers { get; }

		public int[] TVDCellRows { get; }

		public int[] TVDFaceRowPointers { get; }

		public int[] TVDFaceRows { get; }

		public TVDVirtualPointInterpolationGeometry(HydraulicMeshComputer hmesh)
		{
			Mesh = hmesh.Mesh;
			int[] tvdStencilRowPointers = new int[RowCount + 1];
			int[] tvdPathCellRowPointers = new int[RowCount + 1];
			int[] tvdPathFaceRowPointers = new int[RowCount + 1];
			int[] intValues = new int[RowCount + 1];
			int[] intValuesArray = new int[RowCount + 1];
			List<int> scaledRowCount = new List<int>(RowCount * 2);
			List<double> scaledRowCountList = new List<double>(RowCount * 2);
			List<int> intList = new List<int>(RowCount);
			List<int> scaledRowCount2 = new List<int>(RowCount / 2);
			List<int> scaledRowCount3 = new List<int>(RowCount * 2);
			List<int> scaledRowCount4 = new List<int>(RowCount * 2);
			TVDContainingCells = new int[RowCount];
			Array.Fill(TVDContainingCells, -1);
			TVDVirtualPoints = new Point[RowCount];
			TVDRowGeometry tVDRowGeometry = new TVDRowGeometry();
			int maxTVDStencilSize = 0;
			for (int i = 0; i < RowCount; i++)
			{
				int ordinal = i / 2;
				Face face = Mesh.Faces[ordinal];
				tVDRowGeometry.Clear();
				if ((i & 1) == 0)
				{
					BuildTVDRow(ordinal, face.cellA, face.cellB, tVDRowGeometry);
				}
				else
				{
					BuildTVDRow(ordinal, face.cellB, face.cellA, tVDRowGeometry);
				}
				maxTVDStencilSize = Math.Max(maxTVDStencilSize, tVDRowGeometry.TVDStencilCells.Count);
				scaledRowCount.AddRange(tVDRowGeometry.TVDStencilCells);
				scaledRowCountList.AddRange(tVDRowGeometry.TVDFullyWetCoefficients);
				intList.AddRange(tVDRowGeometry.TVDPathCells);
				scaledRowCount2.AddRange(tVDRowGeometry.TVDPathFaces);
				scaledRowCount3.AddRange(tVDRowGeometry.TVDDependencyCells);
				scaledRowCount4.AddRange(tVDRowGeometry.TVDDependencyFaces);
				tvdStencilRowPointers[i + 1] = scaledRowCount.Count;
				tvdPathCellRowPointers[i + 1] = intList.Count;
				tvdPathFaceRowPointers[i + 1] = scaledRowCount2.Count;
				intValues[i + 1] = scaledRowCount3.Count;
				intValuesArray[i + 1] = scaledRowCount4.Count;
				TVDContainingCells[i] = tVDRowGeometry.TVDContainingCell;
				TVDVirtualPoints[i] = tVDRowGeometry.TVDVirtualPoint;
			}
			MaxTVDStencilSize = maxTVDStencilSize;
			TVDStencilRowPointers = tvdStencilRowPointers;
			TVDStencilCellIndices = scaledRowCount.ToArray();
			TVDFullyWetCoefficients = scaledRowCountList.ToArray();
			TVDPathCellRowPointers = tvdPathCellRowPointers;
			TVDPathCellIndices = intList.ToArray();
			TVDPathFaceRowPointers = tvdPathFaceRowPointers;
			TVDPathFaceIndices = scaledRowCount2.ToArray();
			TransposeDependencies(Mesh.CellCount, intValues, scaledRowCount3, out var dependencyRowPointers, out var dependencyRows);
			TVDCellRowPointers = dependencyRowPointers;
			TVDCellRows = dependencyRows;
			TransposeDependencies(Mesh.FaceCount, intValuesArray, scaledRowCount4, out var dependencyRowPointers2, out var dependencyRows2);
			TVDFaceRowPointers = dependencyRowPointers2;
			TVDFaceRows = dependencyRows2;
		}

		private void BuildTVDRow(int transportFace, int upstreamCell, int downwindCell, TVDRowGeometry row)
		{
			if (!IsRealCell(upstreamCell) || !IsRealCell(downwindCell))
			{
				return;
			}
			Point upstreamPoint = Mesh.CellCenters[upstreamCell];
			Point point = Mesh.CellCenters[downwindCell];
			Point virtualPoint = (row.TVDVirtualPoint = new Point(2.0 * upstreamPoint.X - point.X, 2.0 * upstreamPoint.Y - point.Y));
			int tvdContainingCell = FindTVDContainingCell(upstreamPoint, virtualPoint);
			if (tvdContainingCell < 0)
			{
				return;
			}
			if (!TryBuildTVDPath(upstreamCell, tvdContainingCell, virtualPoint, row.TVDPathCells, row.TVDPathFaces))
			{
				row.TVDPathCells.Clear();
				row.TVDPathFaces.Clear();
				return;
			}
			row.TVDContainingCell = tvdContainingCell;
			BuildTVDStencil(tvdContainingCell, row.TVDStencilCells);
			if (row.TVDStencilCells.Count == 0)
			{
				row.TVDContainingCell = -1;
				return;
			}
			ComputeFullyWetTVDWeights(row);
			if (row.TVDFullyWetCoefficients.Count == 0)
			{
				row.TVDContainingCell = -1;
				row.TVDStencilCells.Clear();
				return;
			}
			foreach (int tVDStencilCell in row.TVDStencilCells)
			{
				row.TVDDependencyCells.Add(tVDStencilCell);
			}
			foreach (int tVDPathCell in row.TVDPathCells)
			{
				row.TVDDependencyCells.Add(tVDPathCell);
			}
			foreach (int tVDPathFace in row.TVDPathFaces)
			{
				row.TVDDependencyFaces.Add(tVDPathFace);
			}
			row.TVDDependencyCells.Add(upstreamCell);
			row.TVDDependencyCells.Add(downwindCell);
			row.TVDDependencyFaces.Add(transportFace);
			AddTVDLocalFaceDependencies(row);
		}

		private void ComputeFullyWetTVDWeights(TVDRowGeometry row)
		{
			int count = row.TVDStencilCells.Count;
			List<double> tVDFullyWetCoefficients = row.TVDFullyWetCoefficients;
			List<double> tVDBaseWeights = row.TVDBaseWeights;
			for (int i = 0; i < count; i++)
			{
				tVDFullyWetCoefficients.Add(0.0);
				tVDBaseWeights.Add(0.0);
			}
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			for (int j = 0; j < count; j++)
			{
				Point point = Mesh.CellCenters[row.TVDStencilCells[j]];
				double pointTvdVirtualPointTerm = point.X - row.TVDVirtualPoint.X;
				double pointTvdVirtualPointTerm2 = point.Y - row.TVDVirtualPoint.Y;
				double computed6 = Math.Sqrt(pointTvdVirtualPointTerm * pointTvdVirtualPointTerm + pointTvdVirtualPointTerm2 * pointTvdVirtualPointTerm2);
				if (!(computed6 > 0.0) || !double.IsFinite(computed6))
				{
					tVDFullyWetCoefficients[j] = 1.0;
					return;
				}
				double tVDBaseWeightsTerm = (tVDBaseWeights[j] = 1.0 / computed6);
				computed += tVDBaseWeightsTerm * pointTvdVirtualPointTerm;
				computed2 += tVDBaseWeightsTerm * pointTvdVirtualPointTerm2;
				computed3 += tVDBaseWeightsTerm * pointTvdVirtualPointTerm * pointTvdVirtualPointTerm;
				computed4 += tVDBaseWeightsTerm * pointTvdVirtualPointTerm * pointTvdVirtualPointTerm2;
				computed5 += tVDBaseWeightsTerm * pointTvdVirtualPointTerm2 * pointTvdVirtualPointTerm2;
			}
			double computed7 = computed3 * computed5 - computed4 * computed4;
			double computed8 = computed3 + computed5;
			if (count < 3 || !(Math.Abs(computed7) > 1E-14 * computed8 * computed8))
			{
				if (!TryComputeFullyWetTVDLineWeights(row))
				{
					tVDFullyWetCoefficients.Clear();
				}
				return;
			}
			double computed9 = ((0.0 - computed) * computed5 + computed2 * computed4) / computed7;
			double computed10 = ((0.0 - computed2) * computed3 + computed * computed4) / computed7;
			double tVDFullyWetCoefficients2Sum = 0.0;
			for (int k = 0; k < count; k++)
			{
				Point point2 = Mesh.CellCenters[row.TVDStencilCells[k]];
				double point2TvdVirtualPointTerm = point2.X - row.TVDVirtualPoint.X;
				double point2TvdVirtualPointTerm2 = point2.Y - row.TVDVirtualPoint.Y;
				double tVDFullyWetCoefficients2 = tVDBaseWeights[k] * (1.0 + computed9 * point2TvdVirtualPointTerm + computed10 * point2TvdVirtualPointTerm2);
				if (!double.IsFinite(tVDFullyWetCoefficients2))
				{
					tVDFullyWetCoefficients.Clear();
					return;
				}
				tVDFullyWetCoefficients[k] = tVDFullyWetCoefficients2;
				tVDFullyWetCoefficients2Sum += tVDFullyWetCoefficients2;
			}
			if (!(Math.Abs(tVDFullyWetCoefficients2Sum) > 1E-14) || !double.IsFinite(tVDFullyWetCoefficients2Sum))
			{
				tVDFullyWetCoefficients.Clear();
				return;
			}
			for (int l = 0; l < count; l++)
			{
				tVDFullyWetCoefficients[l] /= tVDFullyWetCoefficients2Sum;
			}
		}

		private bool TryComputeFullyWetTVDLineWeights(TVDRowGeometry row)
		{
			Point tVDVirtualPoint = row.TVDVirtualPoint;
			for (int i = 0; i < row.TVDStencilCells.Count; i++)
			{
				Point point = Mesh.CellCenters[row.TVDStencilCells[i]];
				for (int j = i + 1; j < row.TVDStencilCells.Count; j++)
				{
					Point point2 = Mesh.CellCenters[row.TVDStencilCells[j]];
					double point2PointTerm = point2.X - point.X;
					double point2PointTerm2 = point2.Y - point.Y;
					double computed = point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2;
					if (!(computed > 0.0) || !double.IsFinite(computed))
					{
						continue;
					}
					double tVDVirtualPointPointTerm = tVDVirtualPoint.X - point.X;
					double tVDVirtualPointPointTerm2 = tVDVirtualPoint.Y - point.Y;
					double tvdFullyWetCoefficients = (tVDVirtualPointPointTerm * point2PointTerm + tVDVirtualPointPointTerm2 * point2PointTerm2) / computed;
					if (!(tvdFullyWetCoefficients < -1E-11) && !(tvdFullyWetCoefficients > 1.00000000001))
					{
						double computed2 = tVDVirtualPointPointTerm * point2PointTerm2 - tVDVirtualPointPointTerm2 * point2PointTerm;
						if (!(computed2 * computed2 > 1E-20 * computed * computed))
						{
							tvdFullyWetCoefficients = Math.Clamp(tvdFullyWetCoefficients, 0.0, 1.0);
							row.TVDFullyWetCoefficients[i] = 1.0 - tvdFullyWetCoefficients;
							row.TVDFullyWetCoefficients[j] = tvdFullyWetCoefficients;
							return true;
						}
					}
				}
			}
			return false;
		}

		private int FindTVDContainingCell(Point upstreamPoint, Point virtualPoint)
		{
			SpatialIndex<int> cellSpatialIndex = Mesh.GetCellSpatialIndex();
			List<int> elements = cellSpatialIndex.GetElements(virtualPoint);
			int uniqueTVDContainingCell = FindUniqueTVDContainingCell(elements, virtualPoint);
			double distanceTo = upstreamPoint.DistanceTo(virtualPoint);
			if (!(distanceTo > 0.0) || !double.IsFinite(distanceTo))
			{
				return -1;
			}
			double distanceToUpstreamPointTerm = Math.Max(distanceTo, Math.Max(Math.Max(Math.Abs(upstreamPoint.X), Math.Abs(upstreamPoint.Y)), Math.Max(Math.Abs(virtualPoint.X), Math.Abs(virtualPoint.Y))));
			double distanceToTerm = Math.Max(distanceTo * 1E-10, 1.4210854715202004E-14 * distanceToUpstreamPointTerm);
			if (uniqueTVDContainingCell >= 0 && Mesh.CellPolygon(uniqueTVDContainingCell).SignedPointToPolygonDistance(virtualPoint) > distanceToTerm)
			{
				return uniqueTVDContainingCell;
			}
			double tvdApproachFraction = TVDApproachFraction(upstreamPoint, virtualPoint, distanceTo);
			Point point = new Point(virtualPoint.X + tvdApproachFraction * (upstreamPoint.X - virtualPoint.X), virtualPoint.Y + tvdApproachFraction * (upstreamPoint.Y - virtualPoint.Y));
			elements = cellSpatialIndex.GetElements(point);
			return FindUniqueTVDContainingCell(elements, point);
		}

		private int FindUniqueTVDContainingCell(List<int> candidates, Point point)
		{
			int candidateTerm = -1;
			foreach (int candidate in candidates)
			{
				if (IsRealCell(candidate) && Mesh.CellPolygon(candidate).Contains(point))
				{
					if (candidateTerm >= 0 && candidateTerm != candidate)
					{
						return -1;
					}
					candidateTerm = candidate;
				}
			}
			return candidateTerm;
		}

		private bool TryBuildTVDPath(int upstreamCell, int containingCell, Point virtualPoint, List<int> pathCells, List<int> pathFaces)
		{
			pathCells.Add(upstreamCell);
			Point point = Mesh.CellCenters[upstreamCell];
			double distanceTo = point.DistanceTo(virtualPoint);
			if (!(distanceTo > 0.0) || !double.IsFinite(distanceTo))
			{
				return false;
			}
			double tvdApproachFraction = TVDApproachFraction(point, virtualPoint, distanceTo);
			Segment path = new Segment(ptB: new Point(virtualPoint.X + tvdApproachFraction * (point.X - virtualPoint.X), virtualPoint.Y + tvdApproachFraction * (point.Y - virtualPoint.Y)), ptA: point);
			int upstreamCell2 = upstreamCell;
			double currentFraction = 0.0;
			int count = Math.Max(Mesh.FaceCount, Mesh.CellCount) + 1;
			for (int i = 0; i < count; i++)
			{
				double positiveInfinity = double.PositiveInfinity;
				bool condition = false;
				_TVDPathCandidateFaces.Clear();
				Span<int> faces = Mesh.Cells[upstreamCell2].Faces.Span;
				for (int j = 0; j < faces.Length; j++)
				{
					int faceIndex = faces[j];
					_TVDPathFaceSegments.Clear();
					Mesh.AppendFaceSegments(faceIndex, _TVDPathFaceSegments);
					foreach (Segment tVDPathFaceSegment in _TVDPathFaceSegments)
					{
						if (!TryTVDPathIntersection(path, tVDPathFaceSegment, currentFraction, out var intersectionFraction, out var collinearOverlap))
						{
							condition |= collinearOverlap;
						}
						else if (intersectionFraction < positiveInfinity - 1E-11)
						{
							positiveInfinity = intersectionFraction;
							_TVDPathCandidateFaces.Clear();
							_TVDPathCandidateFaces.Add(faceIndex);
						}
						else if (Math.Abs(intersectionFraction - positiveInfinity) <= 1E-11 && !_TVDPathCandidateFaces.Contains(faceIndex))
						{
							_TVDPathCandidateFaces.Add(faceIndex);
						}
					}
				}
				if (condition)
				{
					return false;
				}
				if (!double.IsFinite(positiveInfinity) || positiveInfinity >= 0.99999999999)
				{
					return upstreamCell2 == containingCell;
				}
				int upstreamCell22 = -1;
				int tVDPathCandidateFaceTerm = -1;
				double t = Math.Min(1.0, positiveInfinity + Math.Max(1E-11, 1E-09 / distanceTo));
				Point point2 = path.PointAtFraction(t);
				foreach (int tVDPathCandidateFace in _TVDPathCandidateFaces)
				{
					Face face = Mesh.Faces[tVDPathCandidateFace];
					int cellB = ((face.cellA == upstreamCell2) ? face.cellB : face.cellA);
					if (IsRealCell(cellB) && Mesh.CellPolygon(cellB).Contains(point2))
					{
						if (upstreamCell22 >= 0 && (upstreamCell22 != cellB || tVDPathCandidateFaceTerm != tVDPathCandidateFace))
						{
							return false;
						}
						upstreamCell22 = cellB;
						tVDPathCandidateFaceTerm = tVDPathCandidateFace;
					}
				}
				if (upstreamCell22 < 0)
				{
					return false;
				}
				pathFaces.Add(tVDPathCandidateFaceTerm);
				pathCells.Add(upstreamCell22);
				upstreamCell2 = upstreamCell22;
				currentFraction = positiveInfinity;
			}
			return false;
		}

		private static bool TryTVDPathIntersection(Segment path, Segment boundary, double currentFraction, out double intersectionFraction, out bool collinearOverlap)
		{
			double deltaX = path.DeltaX;
			double deltaY = path.DeltaY;
			double deltaX2 = boundary.DeltaX;
			double deltaY2 = boundary.DeltaY;
			double boundaryPathTerm = boundary.A.X - path.A.X;
			double boundaryPathTerm2 = boundary.A.Y - path.A.Y;
			double deltaXDeltaY2Term = deltaX * deltaY2 - deltaY * deltaX2;
			double deltaXDeltaYTerm = Math.Sqrt((deltaX * deltaX + deltaY * deltaY) * (deltaX2 * deltaX2 + deltaY2 * deltaY2));
			double computed = 2.842170943040401E-14 * deltaXDeltaYTerm;
			if (Math.Abs(deltaXDeltaY2Term) <= computed)
			{
				double deltaYDeltaXTerm = boundaryPathTerm * deltaY - boundaryPathTerm2 * deltaX;
				collinearOverlap = Math.Abs(deltaYDeltaXTerm) <= computed && SegmentFractionsOverlap(path, boundary, currentFraction);
				intersectionFraction = double.NaN;
				return false;
			}
			double intersectionFraction2 = (boundaryPathTerm * deltaY2 - boundaryPathTerm2 * deltaX2) / deltaXDeltaY2Term;
			double deltaYDeltaXTerm2 = (boundaryPathTerm * deltaY - boundaryPathTerm2 * deltaX) / deltaXDeltaY2Term;
			collinearOverlap = false;
			intersectionFraction = intersectionFraction2;
			if (intersectionFraction2 > currentFraction + 1E-11 && intersectionFraction2 <= 1.00000000001 && deltaYDeltaXTerm2 >= -1E-11)
			{
				return deltaYDeltaXTerm2 <= 1.00000000001;
			}
			return false;
		}

		private static bool SegmentFractionsOverlap(Segment path, Segment boundary, double currentFraction)
		{
			double deltaXPathTerm = path.DeltaX * path.DeltaX + path.DeltaY * path.DeltaY;
			if (!(deltaXPathTerm > 0.0))
			{
				return false;
			}
			double boundaryPathTerm = ((boundary.A.X - path.A.X) * path.DeltaX + (boundary.A.Y - path.A.Y) * path.DeltaY) / deltaXPathTerm;
			double boundaryPathTerm2 = ((boundary.B.X - path.A.X) * path.DeltaX + (boundary.B.Y - path.A.Y) * path.DeltaY) / deltaXPathTerm;
			double computed = Math.Max(boundaryPathTerm, boundaryPathTerm2);
			double computed2 = Math.Min(boundaryPathTerm, boundaryPathTerm2);
			if (computed > currentFraction + 1E-11)
			{
				return computed2 < 0.99999999999;
			}
			return false;
		}

		private static double TVDApproachFraction(Point upstreamPoint, Point virtualPoint, double length)
		{
			double lengthUpstreamPointTerm = Math.Max(length, Math.Max(Math.Max(Math.Abs(upstreamPoint.X), Math.Abs(upstreamPoint.Y)), Math.Max(Math.Abs(virtualPoint.X), Math.Abs(virtualPoint.Y))));
			double lengthTerm = 1.4210854715202004E-14 * lengthUpstreamPointTerm / length;
			return Math.Min(1E-07, Math.Max(1E-11, lengthTerm));
		}

		private void BuildTVDStencil(int containingCell, List<int> stencilCells)
		{
			_TVDStencilNodes.Clear();
			ReadOnlySpan<int> faces = Mesh.Cells[containingCell].Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				Face face = Mesh.Faces[faces[i]];
				_TVDStencilNodes.Add(face.fpA);
				_TVDStencilNodes.Add(face.fpB);
			}
			_TVDStencilCells.Clear();
			_TVDStencilCells.Add(containingCell);
			foreach (int tVDStencilNode in _TVDStencilNodes)
			{
				Mesh.GetCellsAroundFacepoint(tVDStencilNode, _TVDCellsAroundNode);
				foreach (int tvdCellsAroundNode in _TVDCellsAroundNode)
				{
					if (IsRealCell(tvdCellsAroundNode))
					{
						_TVDStencilCells.Add(tvdCellsAroundNode);
					}
				}
			}
			stencilCells.AddRange(_TVDStencilCells);
			stencilCells.Sort();
		}

		private void AddTVDLocalFaceDependencies(TVDRowGeometry row)
		{
			foreach (int tVDStencilCell in row.TVDStencilCells)
			{
				ReadOnlySpan<int> faces = Mesh.Cells[tVDStencilCell].Faces.Span;
				for (int i = 0; i < faces.Length; i++)
				{
					int faceIndex = faces[i];
					Face face = Mesh.Faces[faceIndex];
					int cellB = ((face.cellA == tVDStencilCell) ? face.cellB : face.cellA);
					if (cellB >= 0 && row.TVDStencilCells.BinarySearch(cellB) >= 0)
					{
						row.TVDDependencyFaces.Add(faceIndex);
					}
				}
			}
		}

		private bool IsRealCell(int cell)
		{
			if (cell >= 0 && cell < Mesh.CellCount)
			{
				return !Mesh.CellIsVirtual(cell);
			}
			return false;
		}

		private static void TransposeDependencies(int dependencyCount, int[] rowPointers, List<int> dependencies, out int[] dependencyRowPointers, out int[] dependencyRows)
		{
			dependencyRowPointers = new int[dependencyCount + 1];
			foreach (int dependency in dependencies)
			{
				dependencyRowPointers[dependency + 1]++;
			}
			for (int i = 0; i < dependencyCount; i++)
			{
				dependencyRowPointers[i + 1] += dependencyRowPointers[i];
			}
			dependencyRows = new int[dependencies.Count];
			int[] intValues = new int[dependencyCount];
			Array.Copy(dependencyRowPointers, intValues, dependencyCount);
			for (int j = 0; j < rowPointers.Length - 1; j++)
			{
				for (int k = rowPointers[j]; k < rowPointers[j + 1]; k++)
				{
					int dependency2 = dependencies[k];
					dependencyRows[intValues[dependency2]++] = j;
				}
			}
		}
	}

	private const double siGamma = 9810.0;

	private const double uscGamma = 62.4;

	private const double MinFaceAreaTolerance = 1E-20;

	private const double DeterminantInverseTolerance = 1E-10;

	public readonly CellCompute CellComputes;

	public readonly NodeCompute NodeComputes;

	public readonly FaceCompute FaceComputes;

	private readonly Lock _lock = new Lock();

	private double[] _faceInvLengths;

	private double[] _cellPgonAreas;

	private double[] _cellPgonAreaInv;

	private double[] _cellNodeAreas;

	private float[] _faceSimpleLengths;

	private double[] _faceRepDist;

	private double[] _faceRepDistSq;

	private double[] _faceRepDistInv;

	private double[] _faceInvPerpDistances;

	private FaceAB[] _faceAlphaCellBathAInv;

	private FaceAB[] _faceAlphaCellAInv;

	private FaceAB[] _faceAlpha;

	private double[] _cellAInv;

	private int[] _cellFaceStartIndex;

	private readonly Lock _perotLock = new Lock();

	private Vector2[] _perotUV;

	private Vector[] _faceNormal;

	private Vector2[] _faceNormalF;

	private readonly Mesh _mesh;

	private int? _maxFaceCount;

	private FaceTangential _faceTangential;

	private float[] _cellSkewness;

	private float[] _cellSpacing;

	private float[] _cellCircumcenter;

	private float[] _cellAreaRatio;

	private float[] _cellOrthogonality;

	private float[] _cellDegree;

	private float[] _cellCompositeMetric;

	private SpatialIndex<int> _cellSpIdx;

	private SpatialIndex<int> _faceSpIdx;

	private SpatialIndex<int> _facePtSpIdx;

	private double[] _cellAreasSorted;

	private double[,] _benFpWeights;

	public const double SmoothCubicTransitionRatio = 1.5;

	private SparseMatrix<Vector> _scalarGradientWlsStructure;

	private int[] _scalarGradientWlsNeighborFaceIndices;

	private FaceNormalGradientGeometry _faceNormalGradientGeometry;

	private NodeFaceInterpolationGeometry _nodeFaceInterpolationGeometry;

	private TVDVirtualPointInterpolationGeometry _TVDVirtualPointInterpolationGeometry;

	public double[] FaceInvLengths => _faceInvLengths ?? LazyThreadsafe<double[]>.AllocateOnce(ref _faceInvLengths, _lock, Mesh, (Mesh mesh) => mesh.ComputeFaceInvertLengths());

	public double[] CellPolygonAreas => _cellPgonAreas ?? LazyThreadsafe<double[]>.AllocateOnce(ref _cellPgonAreas, _lock, Mesh, (Mesh mesh) => mesh.ComputeCellPolygonAreas());

	public double[] CellPolygonAreaInv => _cellPgonAreaInv ?? LazyThreadsafe<double[]>.AllocateOnce(ref _cellPgonAreaInv, _lock, Mesh, (Mesh mesh) => mesh.ComputeCellPolygonAreaInv());

	public double[] CellAreas => _cellNodeAreas ?? LazyThreadsafe<double[]>.AllocateOnce(ref _cellNodeAreas, _lock, Mesh, (Mesh mesh) => mesh.ComputeCellSimpleAreas());

	public float[] FaceSimpleLengths => _faceSimpleLengths ?? LazyThreadsafe<float[]>.AllocateOnce(ref _faceSimpleLengths, _lock, Mesh, (Mesh mesh) =>
	{
		float[] floatValues = new float[mesh.FaceCount];
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			floatValues[i] = (float)mesh.FaceSimpleLength(i);
		}
		return floatValues;
	});

	public double[] FaceRepresentativeDistances
	{
		get
		{
			if (_faceRepDist == null)
			{
				EnsureAllFaceRepresentativeDistances();
			}
			return _faceRepDist;
		}
	}

	public double[] FaceRepresentativeDistancesSquared
	{
		get
		{
			if (_faceRepDistSq == null)
			{
				EnsureAllFaceRepresentativeDistances();
			}
			return _faceRepDistSq;
		}
	}

	public double[] FaceRepresentativeInverseDistances
	{
		get
		{
			if (_faceRepDistInv == null)
			{
				EnsureAllFaceRepresentativeDistances();
			}
			return _faceRepDistInv;
		}
	}

	public double[] FaceInvPerpDistances => _faceInvPerpDistances ?? LazyThreadsafe<double[]>.AllocateOnce(ref _faceInvPerpDistances, _lock, Mesh, (Mesh mesh) =>
	{
		double[] doubleValues = new double[mesh.FaceCount];
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			Face face = _mesh.Faces[i];
			Point point = ((face.cellB != -1) ? _mesh.CellCenters[face.cellB] : _mesh.FaceMidPoint(i));
			double dot = (point - _mesh.CellCenters[face.cellA]).Dot(FaceNormal[i]);
			doubleValues[i] = 1.0 / dot;
		}
		return doubleValues;
	});

	public FaceAB[] FaceAlphaCellBathAInv
	{
		get
		{
			if (_faceAlphaCellBathAInv == null)
			{
				EnsureAllFaceAlphas();
			}
			return _faceAlphaCellBathAInv;
		}
	}

	public FaceAB[] FaceAlphaCellAInv
	{
		get
		{
			if (_faceAlphaCellAInv == null)
			{
				EnsureAllFaceAlphas();
			}
			return _faceAlphaCellAInv;
		}
	}

	public double[] CellAInv
	{
		get
		{
			if (_cellAInv == null)
			{
				EnsureAllFaceAlphas();
			}
			return _cellAInv;
		}
	}

	public FaceAB[] FaceAlpha
	{
		get
		{
			if (_faceAlpha == null)
			{
				EnsureAllFaceAlphas();
			}
			return _faceAlpha;
		}
	}

	public int[] CellFaceStartIndex => _cellFaceStartIndex ?? LazyThreadsafe<int[]>.AllocateOnce(ref _cellFaceStartIndex, _lock, Mesh, (Mesh mesh) =>
	{
		int[] intValues = new int[mesh.CellCount];
		int faceLengthSum = 0;
		for (int i = 0; i < mesh.CellCount; i++)
		{
			intValues[i] = faceLengthSum;
			faceLengthSum += mesh.Cells[i].Faces.Length;
		}
		return intValues;
	});

	public Vector2[] PerotUV => _perotUV ?? LazyThreadsafe<Vector2[]>.AllocateOnce(ref _perotUV, _perotLock, this, (HydraulicMeshComputer hmc) => hmc.ComputeCellFacePerotWeights());

	public Vector[] FaceNormal => _faceNormal ?? LazyThreadsafe<Vector[]>.AllocateOnce(ref _faceNormal, _lock, Mesh, (Mesh mesh) => mesh.ComputeFaceNormals());

	public Vector2[] FaceNormalF => _faceNormalF ?? LazyThreadsafe<Vector2[]>.AllocateOnce(ref _faceNormalF, _lock, this, (HydraulicMeshComputer hmc) =>
	{
		Vector[] faceNormal = hmc.FaceNormal;
		Vector2[] vector2s = new Vector2[faceNormal.Length];
		for (int i = 0; i < faceNormal.Length; i++)
		{
			vector2s[i] = new Vector2((float)faceNormal[i].DeltaX, (float)faceNormal[i].DeltaY);
		}
		return vector2s;
	});

	public Mesh Mesh => _mesh;

	private Face[] Faces => Mesh.Faces;

	private FacePoint[] FacePoints => Mesh.FacePoints;

	private Cell[] Cells => Mesh.Cells;

	public int CellCount => Mesh.CellCount;

	public int FaceCount => Mesh.FaceCount;

	public int FacePointCount => Mesh.NodeCount;

	public int NodeCount => Mesh.NodeCount;

	public int MaxFaceCount => _maxFaceCount ?? LazyThreadsafe<int?>.AllocateOnce(ref _maxFaceCount, _lock, Mesh, (Mesh mesh) =>
	{
		int facesCellsTerm = 0;
		for (int i = 0; i < mesh.CellCount; i++)
		{
			facesCellsTerm = Math.Max(facesCellsTerm, mesh.Cells[i].Faces.Length);
		}
		return facesCellsTerm;
	}).Value;

	public FaceTangential FaceTangential => _faceTangential ?? LazyThreadsafe<FaceTangential>.AllocateOnce(ref _faceTangential, _lock, Mesh, (Mesh mesh) => new FaceTangential(mesh));

	public float[] CellSkewness => _cellSkewness ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellSkewness, _lock, Mesh, (Mesh mesh) => (from i in Enumerable.Range(0, mesh.CellCount)
		select MeshMetrics.CellSkewness(mesh, i)).ToArray());

	public float[] CellSpacing => _cellSpacing ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellSpacing, _lock, Mesh, (Mesh mesh) => (from i in Enumerable.Range(0, mesh.CellCount)
		select MeshMetrics.CellCenterCenterSpacing(mesh, i)).ToArray());

	public float[] CellCircumcenter => _cellCircumcenter ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellCircumcenter, _lock, Mesh, (Mesh mesh) => (from i in Enumerable.Range(0, mesh.CellCount)
		select MeshMetrics.CellCircumcenter(mesh, i)).ToArray());

	public float[] CellAreaRatio => _cellAreaRatio ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellAreaRatio, _lock, Mesh, (Mesh mesh) => (from i in Enumerable.Range(0, mesh.CellCount)
		select MeshMetrics.CellAreaRatio(mesh, i) - 1f).ToArray());

	public float[] CellOrthogonality => _cellOrthogonality ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellOrthogonality, _lock, Mesh, (Mesh mesh) => (from i in Enumerable.Range(0, mesh.CellCount)
		select MeshMetrics.CellOrthogonality(mesh, i)).ToArray());

	public float[] CellDegree => _cellDegree ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellDegree, _lock, Mesh, (Mesh mesh) => (from i in Enumerable.Range(0, mesh.CellCount)
		select MeshMetrics.CellDegree(mesh, i)).ToArray());

	public float[] CellCompositeMetric => _cellCompositeMetric ?? LazyThreadsafe<float[]>.AllocateOnce(ref _cellCompositeMetric, _lock, Mesh, (Mesh mesh) =>
	{
		Func<Mesh, int, float> func = MeshMetrics.GetCustomFunc(MeshMetrics.MetricElement.Cell, float.PositiveInfinity, new List<(MeshMetrics.MetricType, float)>
		{
			(MeshMetrics.MetricType.Orthogonality, 10f),
			(MeshMetrics.MetricType.AreaRatio, 1f),
			(MeshMetrics.MetricType.Skew, 0.2f)
		});
		return (from i in Enumerable.Range(0, mesh.CellCount)
			select func(mesh, i)).ToArray();
	});

	private FaceNormalGradientGeometry NormalGradientGeometry => _faceNormalGradientGeometry ?? LazyThreadsafe<FaceNormalGradientGeometry>.AllocateOnce(ref _faceNormalGradientGeometry, _lock, this, (HydraulicMeshComputer hmesh) => new FaceNormalGradientGeometry(hmesh));

	private NodeFaceInterpolationGeometry NodeFaceGeometry => _nodeFaceInterpolationGeometry ?? LazyThreadsafe<NodeFaceInterpolationGeometry>.AllocateOnce(ref _nodeFaceInterpolationGeometry, _lock, this, (HydraulicMeshComputer hmesh) => new NodeFaceInterpolationGeometry(hmesh));

	private TVDVirtualPointInterpolationGeometry TVDVirtualPointGeometry => _TVDVirtualPointInterpolationGeometry ?? LazyThreadsafe<TVDVirtualPointInterpolationGeometry>.AllocateOnce(ref _TVDVirtualPointInterpolationGeometry, _lock, this, (HydraulicMeshComputer hmesh) => new TVDVirtualPointInterpolationGeometry(hmesh));

	public HydraulicMeshComputer(Mesh mesh)
	{
		_mesh = mesh;
		CellComputes = new CellCompute(this);
		FaceComputes = new FaceCompute(this);
		NodeComputes = new NodeCompute(this);
	}

	public void SetFaceTangential(double[] fWeights)
	{
		_faceTangential = new FaceTangential(_mesh, fWeights);
	}

	private void EnsureAllFaceRepresentativeDistances()
	{
		if (_faceRepDist != null)
		{
			return;
		}
		using (_lock.EnterScope())
		{
			if (_faceRepDist == null)
			{
				ComputeFaceRepresentativeDistances(out var faceDists, out var faceDistsSq, out var faceDistsInv);
				_faceRepDistSq = faceDistsSq;
				_faceRepDistInv = faceDistsInv;
				_faceRepDist = faceDists;
			}
		}
	}

	private void EnsureAllFaceAlphas()
	{
		if (_faceAlphaCellBathAInv != null)
		{
			return;
		}
		using (_lock.EnterScope())
		{
			if (_faceAlphaCellBathAInv == null)
			{
				ComputeFaceAlphaParams(out var faceAlphaCellBathAInv, out var faceAlphaCellAInv, out var faceAlpha, out var cellAInv);
				_faceAlphaCellAInv = faceAlphaCellAInv;
				_faceAlpha = faceAlpha;
				_cellAInv = cellAInv;
				_faceAlphaCellBathAInv = faceAlphaCellBathAInv;
			}
		}
	}

	private void ComputeFaceRepresentativeDistances(out double[] faceDists, out double[] faceDistsSq, out double[] faceDistsInv)
	{
		faceDists = new double[_mesh.FaceCount];
		faceDistsSq = new double[_mesh.FaceCount];
		faceDistsInv = new double[_mesh.FaceCount];
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			Face face = _mesh.Faces[i];
			double faceDists2 = ((face.cellB != -1) ? _mesh.CellCenters[face.cellB] : _mesh.FaceMidPoint(i)).DistanceTo(_mesh.CellCenters[face.cellA]);
			faceDists[i] = faceDists2;
			faceDistsSq[i] = faceDists2 * faceDists2;
			faceDistsInv[i] = 1.0 / faceDists2;
		}
	}

	private void ComputeFaceAlphaParams(out FaceAB[] faceAlphaCellBathAInv, out FaceAB[] faceAlphaCellAInv, out FaceAB[] faceAlpha, out double[] cellAInv)
	{
		faceAlphaCellBathAInv = new FaceAB[_mesh.FaceCount];
		faceAlphaCellAInv = new FaceAB[_mesh.FaceCount];
		faceAlpha = new FaceAB[_mesh.FaceCount];
		cellAInv = new double[_mesh.CellCount];
		double[] cellPolygonAreas = CellPolygonAreas;
		double[] cellAreas = CellAreas;
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			cellAInv[i] = 1.0 / cellAreas[i];
		}
		for (int j = 0; j < _mesh.FaceCount; j++)
		{
			Face face = _mesh.Faces[j];
			double unity;
			if (face.cellB == -1)
			{
				unity = 1.0;
				faceAlpha[j] = new FaceAB(unity, 0.0);
				faceAlphaCellBathAInv[j] = new FaceAB(unity / cellPolygonAreas[face.cellA], 0.0);
				faceAlphaCellAInv[j] = new FaceAB(unity / cellAreas[face.cellA], 0.0);
				continue;
			}
			Point point = _mesh.FaceMidPoint(j);
			double dot = FaceNormal[j].Dot(point - _mesh.CellCenters[face.cellA]);
			double dot2 = FaceNormal[j].Dot(_mesh.CellCenters[face.cellB] - point);
			double dotPlusDot2 = dot + dot2;
			unity = dot / dotPlusDot2;
			double dot2OverDotPlusDot2 = dot2 / dotPlusDot2;
			faceAlphaCellBathAInv[j] = new FaceAB(unity / cellPolygonAreas[face.cellA], dot2OverDotPlusDot2 / cellPolygonAreas[face.cellB]);
			faceAlphaCellAInv[j] = new FaceAB(unity / cellAreas[face.cellA], dot2OverDotPlusDot2 / cellAreas[face.cellB]);
			faceAlpha[j] = new FaceAB(unity, dot2OverDotPlusDot2);
		}
	}

	private Vector2[] ComputeCellFacePerotWeights()
	{
		double[] cellAreas = CellAreas;
		Vector[] faceNormal = FaceNormal;
		float[] faceSimpleLengths = FaceSimpleLengths;
		Vector2[] vector2s = new Vector2[GetTotalCellFaceCount()];
		int faceLengthSum = 0;
		for (int i = 0; i < CellCount; i++)
		{
			Cell cell = Cells[i];
			Span<int> faces = cell.Faces.Span;
			double cellArea = cellAreas[i];
			for (int j = 0; j < faces.Length; j++)
			{
				int faceIndex = faces[j];
				Point point = _mesh.FaceMidPoint(faceIndex);
				Vector vector = faceNormal[faceIndex];
				double absoluteDot = Math.Abs(vector.Dot(point - _mesh.CellCenters[i]));
				float faceSimpleLength = faceSimpleLengths[faceIndex];
				double absoluteDotTimesFaceSimpleLengthOverCellArea = absoluteDot * (double)faceSimpleLength / cellArea;
				float x = (float)(absoluteDotTimesFaceSimpleLengthOverCellArea * vector.DeltaX);
				float y = (float)(absoluteDotTimesFaceSimpleLengthOverCellArea * vector.DeltaY);
				vector2s[faceLengthSum + j] = new Vector2(x, y);
			}
			faceLengthSum += faces.Length;
		}
		return vector2s;
	}

	private static void ComputeFaceVelocityXY(in double fVelNorm, in double fVelTang, in Vector fNorm, out double fVelX, out double fVelY)
	{
		fVelX = fVelNorm * fNorm.DeltaX - fVelTang * fNorm.DeltaY;
		fVelY = fVelNorm * fNorm.DeltaY + fVelTang * fNorm.DeltaX;
	}

	public SpatialIndex<int> GetCellSpatialIndex()
	{
		return _cellSpIdx ?? LazyThreadsafe<SpatialIndex<int>>.AllocateOnce(ref _cellSpIdx, _lock, Mesh, (Mesh mesh) => mesh.ComputeCellSpatialIndex());
	}

	public SpatialIndex<int> GetFaceSpatialIndex()
	{
		return _faceSpIdx ?? LazyThreadsafe<SpatialIndex<int>>.AllocateOnce(ref _faceSpIdx, _lock, Mesh, (Mesh mesh) => mesh.ComputeFaceSpatialIndex());
	}

	public SpatialIndex<int> GetFacePointSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<int>>.AllocateOnce(ref _facePtSpIdx, _lock, Mesh, (Mesh mesh) => mesh.ComputeFacepointSpatialIndex());
	}

	public double[] GetAllCellAreasSorted()
	{
		return _cellAreasSorted ?? LazyThreadsafe<double[]>.AllocateOnce(ref _cellAreasSorted, _lock, this, (HydraulicMeshComputer th) => th.ComputeAllCellAreasSorted());
	}

	private double[] ComputeAllCellAreasSorted()
	{
		double[] areas = new double[CellCount];
		Parallel.SmartFor(CellCount, (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				areas[i] = Mesh.CellPolygonArea(i);
			}
		});
		Array.Sort(areas);
		return areas;
	}

	public double[,] GetFacePointCrossProductWeights()
	{
		return _benFpWeights ?? LazyThreadsafe<double[,]>.AllocateOnce(ref _benFpWeights, _lock, this, (HydraulicMeshComputer th) => th.ComputeBensFacepointWeights());
	}

	private double[,] ComputeBensFacepointWeights()
	{
		int maxFaceCount = MaxFaceCount;
		double[,] retn = new double[CellCount, maxFaceCount];
		Parallel.SmartFor(CellCount, (int start, int fin) =>
		{
			for (int i = start; i <= fin; i++)
			{
				Cell cell = Cells[i];
				Span<int> faces = cell.Faces.Span;
				int length = faces.Length;
				for (int j = 0; j < length; j++)
				{
					int index = j;
					int lengthOffset = j - 1;
					if (lengthOffset == -1)
					{
						lengthOffset = length - 1;
					}
					int faceIndex = faces[index];
					int faceIndex2 = faces[lengthOffset];
					int fPPrev = Faces[faceIndex2].GetFPPrev(i);
					int fPNext = Faces[faceIndex2].GetFPNext(i);
					int fPNext2 = Faces[faceIndex].GetFPNext(i);
					Point c = FacePoints[fPPrev].Point;
					Point a = FacePoints[fPNext].Point;
					Point b = FacePoints[fPNext2].Point;
					retn[i, j] = Vector.CrossProduct(in a, in b, in c);
				}
			}
		});
		return retn;
	}

	private int GetTotalCellFaceCount()
	{
		if (Mesh.CellCount == 0)
		{
			return 0;
		}
		return CellFaceStartIndex[Mesh.CellCount - 1] + Mesh.Cells[Mesh.CellCount - 1].Faces.Length;
	}

	public static double SmoothCubic(double ratio)
	{
		if (ratio <= 0.0)
		{
			return 0.0;
		}
		if (ratio >= 1.5)
		{
			return 1.0;
		}
		return ratio - 4.0 / 27.0 * ratio * ratio * ratio;
	}

	public ScalarGradientWlsWorkspace CreateScalarGradientWlsWorkspace()
	{
		return new ScalarGradientWlsWorkspace(this);
	}

	private void EnsureScalarGradientWlsConnectivity()
	{
		if (Volatile.Read(in _scalarGradientWlsStructure) != null)
		{
			return;
		}
		int maxFaceCount = MaxFaceCount;
		using (_lock.EnterScope())
		{
			if (_scalarGradientWlsStructure == null)
			{
				int cellCount = CellCount;
				int[] intValues = new int[cellCount + 1];
				Span<int> columns = stackalloc int[maxFaceCount];
				Span<int> faces = stackalloc int[maxFaceCount];
				for (int i = 0; i < cellCount; i++)
				{
					int scalarGradientWlsRowColumns = ScalarGradientWlsRowColumns(i, columns, faces);
					intValues[i + 1] = intValues[i] + scalarGradientWlsRowColumns;
				}
				int[] intValuesArray = new int[intValues[cellCount]];
				int[] scalarGradientWlsNeighborFaceIndices = new int[intValuesArray.Length];
				for (int j = 0; j < cellCount; j++)
				{
					int length = ScalarGradientWlsRowColumns(j, columns, faces);
					columns.Slice(0, length).CopyTo(intValuesArray.AsSpan(intValues[j], length));
					faces.Slice(0, length).CopyTo(scalarGradientWlsNeighborFaceIndices.AsSpan(intValues[j], length));
				}
				_scalarGradientWlsNeighborFaceIndices = scalarGradientWlsNeighborFaceIndices;
				SparseMatrix<Vector> vectorCellCountTerm = new SparseMatrix<Vector>(cellCount, intValues, intValuesArray);
				Volatile.Write(ref _scalarGradientWlsStructure, vectorCellCountTerm);
			}
		}
	}

	private int ScalarGradientWlsRowColumns(int cIdx, Span<int> columns, Span<int> faces)
	{
		int count = 0;
		Span<int> facesSpan = Mesh.Cells[cIdx].Faces.Span;
		for (int i = 0; i < facesSpan.Length; i++)
		{
			int faceIndex = facesSpan[i];
			Face face = Mesh.Faces[faceIndex];
			int cellB = ((face.cellA == cIdx) ? face.cellB : face.cellA);
			if (cellB < 0)
			{
				continue;
			}
			bool condition = false;
			for (int j = 0; j < count; j++)
			{
				if (columns[j] == cellB)
				{
					condition = true;
					if (faces[j] != faceIndex)
					{
						faces[j] = -1;
					}
					break;
				}
			}
			if (!condition)
			{
				columns[count] = cellB;
				faces[count++] = faceIndex;
			}
		}
		for (int k = 1; k < count; k++)
		{
			int column = columns[k];
			int faceIndex2 = faces[k];
			int ordinal = k - 1;
			while (ordinal >= 0 && columns[ordinal] > column)
			{
				columns[ordinal + 1] = columns[ordinal];
				faces[ordinal + 1] = faces[ordinal];
				ordinal--;
			}
			columns[ordinal + 1] = column;
			faces[ordinal + 1] = faceIndex2;
		}
		return count;
	}

	public ScalarBoundaryReconstructionWorkspace CreateScalarBoundaryReconstructionWorkspace()
	{
		return new ScalarBoundaryReconstructionWorkspace(this);
	}

	public TVDVirtualPointInterpolationWorkspace CreateTVDVirtualPointInterpolationWorkspace()
	{
		return new TVDVirtualPointInterpolationWorkspace(TVDVirtualPointGeometry);
	}
}
