using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Rasters;

namespace Geospatial.Vectors.MeshTopology;

public class TinCubicInterpolator
{
	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public struct DoF
	{
		public const int V0 = 0;

		public const int V1 = 1;

		public const int V2 = 2;

		public const int C0 = 3;

		public const int C1 = 4;

		public const int C2 = 5;

		public const int T01 = 6;

		public const int T10 = 7;

		public const int T02 = 8;

		public const int T20 = 9;

		public const int T12 = 10;

		public const int T21 = 11;

		public const int I01 = 12;

		public const int I02 = 13;

		public const int I11 = 14;

		public const int I12 = 15;

		public const int I21 = 16;

		public const int I22 = 17;

		public const int S = 18;

		public const int Count = 19;

		public static readonly int[,] T = new int[3, 3]
		{
			{ -1, 6, 8 },
			{ 7, -1, 10 },
			{ 9, 11, -1 }
		};

		public static readonly int[,] I = new int[3, 3]
		{
			{ -1, 12, 13 },
			{ -1, 14, 15 },
			{ -1, 16, 17 }
		};

		public static readonly int[] C = new int[3] { 3, 4, 5 };

		public static readonly int[] V = new int[3] { 0, 1, 2 };
	}

	private Tin _tin;

	public bool ClipInputs = true;

	public static double absTolZMSq = 1E-12;

	private double[,] _Z;

	private double[,] _M;

	private double[,] _zGradients;

	private double[,] _mGradients;

	private int _t;

	private object _lock = new object();

	private static int[] C0idxs = new int[10] { 18, 1, 2, 15, 17, 14, 10, 16, 11, 3 };

	private static int[] C1idxs = new int[10] { 18, 2, 0, 17, 13, 16, 9, 12, 8, 4 };

	private static int[] C2idxs = new int[10] { 18, 0, 1, 13, 15, 12, 6, 14, 7, 5 };

	public double[,] Z => _Z;

	public double[,] M => _M;

	public TinCubicInterpolator(Tin tin, bool clipInputs)
	{
		_tin = tin;
		ClipInputs = clipInputs;
		_Z = new double[_tin.TriangleCount, 19];
		_M = new double[_tin.TriangleCount, 19];
		_zGradients = new double[_tin.PointCount, 2];
		_mGradients = new double[_tin.PointCount, 2];
		CopyVFromTin();
		EstimateNormals();
		ComputeTI1FromTangentPlane();
		ComputeC();
		ComputeI2S();
	}

	public IResample<double> ResampleZ()
	{
		return ResampleZ(_tin.Extent);
	}

	public IResample<double> ResampleM()
	{
		return ResampleM(_tin.Extent);
	}

	public IResample<double> ResampleZ(Extent extent)
	{
		return new FunctionalResampler<double>(extent, double.NaN, MapXYtoZ);
	}

	public IResample<double> ResampleM(Extent extent)
	{
		return new FunctionalResampler<double>(extent, double.NaN, MapXYtoM);
	}

	public double MapXYtoZ(Point pnt)
	{
		return EvaluateCoeffsAtXY(pnt, _Z);
	}

	public double MapXYtoM(Point pnt)
	{
		return EvaluateCoeffsAtXY(pnt, _M);
	}

	public Point MapXYtoZM(Point pnt, ref int t)
	{
		int t2 = t;
		_tin.FindTriangleContainingPoint(pnt, ref t2);
		if (t2 >= 0)
		{
			t = t2;
			MapXYtoXMInTriangle(pnt, t, out var Z, out var M);
			return new Point(Z, M);
		}
		return Point.NullPoint;
	}

	public double EvaluateCoeffsAtXY(Point pnt, double[,] coeffs)
	{
		int t;
		lock (_lock)
		{
			_tin.FindTriangleContainingPoint(pnt, ref _t);
			if (_t < 0)
			{
				return double.NaN;
			}
			t = _t;
		}
		if (_tin.TriangleDefinition[t].IsDeleted)
		{
			return double.NaN;
		}
		return EvaluateInTriangle(pnt, coeffs, t);
	}

	private void EvaluateInTriangleCommon(Point pnt, int t, out double u, out double v, out double w, out int[] idxs, double[,] jacobian = null)
	{
		_tin.Triangles[t].InterpolationWeights(pnt.X, pnt.Y, out var lambda, out var lambda2, out var lambda3);
		Triangle triangle;
		if (lambda <= lambda2 && lambda <= lambda3)
		{
			triangle = new Triangle(StencilPoint(t, 18), StencilPoint(t, 1), StencilPoint(t, 2));
			idxs = C0idxs;
		}
		else if (lambda2 <= lambda && lambda2 <= lambda3)
		{
			triangle = new Triangle(StencilPoint(t, 18), StencilPoint(t, 2), StencilPoint(t, 0));
			idxs = C1idxs;
		}
		else
		{
			if (!(lambda3 <= lambda) || !(lambda3 <= lambda2))
			{
				throw new Exception("Logic fail");
			}
			triangle = new Triangle(StencilPoint(t, 18), StencilPoint(t, 0), StencilPoint(t, 1));
			idxs = C2idxs;
		}
		triangle.InterpolationWeights(pnt.X, pnt.Y, out u, out v, out w);
		if (jacobian != null)
		{
			double scaledArea = 0.5 / triangle.Area;
			Segment edge = triangle.Edge1;
			jacobian[0, 0] = (0.0 - edge.DeltaY) * scaledArea;
			jacobian[0, 1] = edge.DeltaX * scaledArea;
			edge = triangle.Edge2;
			jacobian[1, 0] = (0.0 - edge.DeltaY) * scaledArea;
			jacobian[1, 1] = edge.DeltaX * scaledArea;
			edge = triangle.Edge0;
			jacobian[2, 0] = (0.0 - edge.DeltaY) * scaledArea;
			jacobian[2, 1] = edge.DeltaX * scaledArea;
		}
		double tolerance = 1E-06;
		if (u < 0.0 - tolerance || v < 0.0 - tolerance || w < 0.0 - tolerance)
		{
			throw new Exception("Subtriangle failure");
		}
	}

	private double EvaluateInTriangle(Point pnt, double[,] coeffs, int t)
	{
		EvaluateInTriangleCommon(pnt, t, out var u, out var v, out var w, out var idxs);
		return CubicPolynomial(t, coeffs, u, v, w, idxs);
	}

	private static double CubicPolynomial(int t, double[,] coeffs, double u, double v, double w, int[] idxs)
	{
		return 1.0 * u * u * u * coeffs[t, idxs[0]] + 1.0 * v * v * v * coeffs[t, idxs[1]] + 1.0 * w * w * w * coeffs[t, idxs[2]] + 3.0 * u * u * v * coeffs[t, idxs[3]] + 3.0 * u * u * w * coeffs[t, idxs[4]] + 3.0 * u * v * v * coeffs[t, idxs[5]] + 3.0 * v * v * w * coeffs[t, idxs[6]] + 3.0 * u * w * w * coeffs[t, idxs[7]] + 3.0 * v * w * w * coeffs[t, idxs[8]] + 6.0 * u * v * w * coeffs[t, idxs[9]];
	}

	private static double dCubicPolynomial(int t, double[,] coeffs, double u, double v, double w, int[] idxs, double du, double dv, double dw)
	{
		double duCoeffsTerm = 3.0 * u * u * du * coeffs[t, idxs[0]] + 6.0 * u * du * v * coeffs[t, idxs[3]] + 6.0 * u * du * w * coeffs[t, idxs[4]] + 3.0 * du * v * v * coeffs[t, idxs[5]] + 3.0 * du * w * w * coeffs[t, idxs[7]] + 6.0 * du * v * w * coeffs[t, idxs[9]];
		double dvCoeffsTerm = 3.0 * v * v * dv * coeffs[t, idxs[1]] + 3.0 * u * u * dv * coeffs[t, idxs[3]] + 6.0 * u * v * dv * coeffs[t, idxs[5]] + 6.0 * v * dv * w * coeffs[t, idxs[6]] + 3.0 * dv * w * w * coeffs[t, idxs[8]] + 6.0 * u * dv * w * coeffs[t, idxs[9]];
		double dwCoeffsTerm = 3.0 * w * w * dw * coeffs[t, idxs[2]] + 3.0 * u * u * dw * coeffs[t, idxs[4]] + 3.0 * v * v * dw * coeffs[t, idxs[6]] + 6.0 * u * w * dw * coeffs[t, idxs[7]] + 6.0 * v * w * dw * coeffs[t, idxs[8]] + 6.0 * u * v * dw * coeffs[t, idxs[9]];
		return duCoeffsTerm + dvCoeffsTerm + dwCoeffsTerm;
	}

	public void MapXYtoXMInTriangle(Point pnt, int t, out double Z, out double M)
	{
		EvaluateInTriangleCommon(pnt, t, out var u, out var v, out var w, out var idxs);
		Z = CubicPolynomial(t, _Z, u, v, w, idxs);
		M = CubicPolynomial(t, _M, u, v, w, idxs);
	}

	public void MapXYtoZMJacobianInTriangle(Point pnt, int t, out Point pntZM, out Vector gradZ, out Vector gradM)
	{
		double[,] doubleValues = new double[3, 2];
		EvaluateInTriangleCommon(pnt, t, out var u, out var v, out var w, out var idxs, doubleValues);
		pntZM = new Point(CubicPolynomial(t, _Z, u, v, w, idxs), CubicPolynomial(t, _M, u, v, w, idxs));
		gradZ = new Vector(dCubicPolynomial(t, _Z, u, v, w, idxs, doubleValues[0, 0], doubleValues[1, 0], doubleValues[2, 0]), dCubicPolynomial(t, _Z, u, v, w, idxs, doubleValues[0, 1], doubleValues[1, 1], doubleValues[2, 1]));
		gradM = new Vector(dCubicPolynomial(t, _M, u, v, w, idxs, doubleValues[0, 0], doubleValues[1, 0], doubleValues[2, 0]), dCubicPolynomial(t, _M, u, v, w, idxs, doubleValues[0, 1], doubleValues[1, 1], doubleValues[2, 1]));
	}

	public Polyline InterpolateSegment(Segment segXY)
	{
		int count = 20;
		Polyline polyline = new Polyline(count);
		_ = new Point[2] { segXY.A, segXY.B };
		Point[] points = new Point[2];
		int t = 0;
		points[0] = MapXYtoZM(segXY.A, ref t);
		points[1] = MapXYtoZM(segXY.B, ref t);
		for (int i = 0; i < count; i++)
		{
			double countTerm = (double)i / ((double)count - 1.0);
			Point pntZM = (1.0 - countTerm) * points[0] + countTerm * points[1];
			polyline.Add(MapZMtoXY(pntZM, ref t));
		}
		return polyline;
	}

	public Point MapZMtoXY(Point pntZM, ref int t)
	{
		if (ClipInputs)
		{
			pntZM.X = Math.Min(1.0, Math.Max(0.0, pntZM.X));
			pntZM.Y = Math.Min(1.0, Math.Max(0.0, pntZM.Y));
		}
		_tin.FindTriangleContainingZMPoint(pntZM, ref t);
		if (t == -1)
		{
			throw new DynamicMeshException("Quad transform failed. Patch corners may be ambiguous. Check if patch should be triangles.");
		}
		_tin.Triangle4D[t].ComputeXY(pntZM, out var xyPoint);
		if (!xyPoint.IsValid())
		{
			throw new DynamicMeshException("Quad transform failed. Numerical error.");
		}
		if (_tin.TriangleDefinition[t].IsDeleted)
		{
			return xyPoint;
		}
		int count = 10;
		Vector nullVelocity = Vector.NullVelocity;
		Point result = xyPoint;
		Point nullPoint = Point.NullPoint;
		for (int i = 0; i < count; i++)
		{
			_tin.FindTriangleContainingPoint(xyPoint, ref t);
			if (_tin.TriangleDefinition[t].IsDeleted)
			{
				return result;
			}
			MapXYtoZMJacobianInTriangle(xyPoint, t, out var pntZM2, out var gradZ, out var gradM);
			nullVelocity = pntZM2 - pntZM;
			if (absTolZMSq > 0.0 && nullVelocity.LengthSquared < absTolZMSq)
			{
				break;
			}
			double gradZGradMTerm = 1.0 / (gradZ.X * gradM.Y - gradZ.Y * gradM.X);
			if (!double.IsFinite(gradZGradMTerm))
			{
				throw new Exception("Could not invert jacobian in cubic interpolation");
			}
			nullPoint.X = xyPoint.X - gradZGradMTerm * (gradM.Y * nullVelocity.X - gradZ.Y * nullVelocity.Y);
			nullPoint.Y = xyPoint.Y - gradZGradMTerm * ((0.0 - gradM.X) * nullVelocity.X + gradZ.X * nullVelocity.Y);
			xyPoint = nullPoint;
		}
		return xyPoint;
	}

	private void CopyVFromTin()
	{
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
			_Z[i, 0] = _tin.Z[triangleDefinition.p0];
			_Z[i, 1] = _tin.Z[triangleDefinition.p1];
			_Z[i, 2] = _tin.Z[triangleDefinition.p2];
			_M[i, 0] = _tin.M[triangleDefinition.p0];
			_M[i, 1] = _tin.M[triangleDefinition.p1];
			_M[i, 2] = _tin.M[triangleDefinition.p2];
		}
	}

	private void EstimateNormals()
	{
		for (int i = 0; i < 8; i++)
		{
			_zGradients[i, 0] = (_zGradients[i, 1] = double.NaN);
			_mGradients[i, 0] = (_mGradients[i, 1] = double.NaN);
		}
		int t = 0;
		List<int> scratchSpace = new List<int>(10);
		for (int j = 8; j < _tin.PointCount; j++)
		{
			_tin.FindTriangleContainingPoint(_tin[j], ref t);
			double computed = 0.0;
			double computed2 = 0.0;
			double computed3 = 0.0;
			double computed4 = 0.0;
			double computed5 = 0.0;
			bool condition = false;
			foreach (int trianglesAroundPointCCW in _tin.TrianglesAroundPointCCW(j, t, scratchSpace))
			{
				Triangle triangle = _tin.Triangles[trianglesAroundPointCCW];
				if (_tin.TriangleDefinition[trianglesAroundPointCCW].IsDeleted)
				{
					condition = true;
					break;
				}
				double areaTriangleTerm = 1.0 / (1E-08 + triangle.Area);
				computed += areaTriangleTerm;
				Vector vector = TriangleGradientZ(trianglesAroundPointCCW);
				computed2 += areaTriangleTerm * vector.DeltaX;
				computed3 += areaTriangleTerm * vector.DeltaY;
				vector = TriangleGradientM(trianglesAroundPointCCW);
				computed4 += areaTriangleTerm * vector.DeltaX;
				computed5 += areaTriangleTerm * vector.DeltaY;
			}
			if (!condition && computed > 0.0)
			{
				_zGradients[j, 0] = computed2 / computed;
				_zGradients[j, 1] = computed3 / computed;
				_mGradients[j, 0] = computed4 / computed;
				_mGradients[j, 1] = computed5 / computed;
			}
			else
			{
				_zGradients[j, 0] = double.NaN;
				_zGradients[j, 1] = double.NaN;
				_mGradients[j, 0] = double.NaN;
				_mGradients[j, 1] = double.NaN;
			}
		}
	}

	private Vector TriangleGradientZ(int t)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		Vector3D a = _tin.Point3D[triangleDefinition.p1] - _tin.Point3D[triangleDefinition.p0];
		Vector3D b = _tin.Point3D[triangleDefinition.p2] - _tin.Point3D[triangleDefinition.p0];
		Vector3D vector3D = Vector3D.CrossProduct(a, b);
		return new Vector((0.0 - vector3D.DeltaX) / vector3D.DeltaZ, (0.0 - vector3D.DeltaY) / vector3D.DeltaZ);
	}

	private Vector TriangleGradientM(int t)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		Vector3D a = _tin.Point3D[triangleDefinition.p1] - _tin.Point3D[triangleDefinition.p0];
		Vector3D b = _tin.Point3D[triangleDefinition.p2] - _tin.Point3D[triangleDefinition.p0];
		a.DeltaZ = _tin.M[triangleDefinition.p1] - _tin.M[triangleDefinition.p0];
		b.DeltaZ = _tin.M[triangleDefinition.p2] - _tin.M[triangleDefinition.p0];
		Vector3D vector3D = Vector3D.CrossProduct(a, b);
		return new Vector((0.0 - vector3D.DeltaX) / vector3D.DeltaZ, (0.0 - vector3D.DeltaY) / vector3D.DeltaZ);
	}

	private void ComputeTI1FromTangentPlane()
	{
		int t = 0;
		List<int> scratchSpace = new List<int>(10);
		for (int i = 8; i < _tin.PointCount; i++)
		{
			_tin.FindTriangleContainingPoint(_tin[i], ref t);
			foreach (int trianglesAroundPointCCW in _tin.TrianglesAroundPointCCW(i, t, scratchSpace))
			{
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[trianglesAroundPointCCW];
				if (triangleDefinition.IsDeleted)
				{
					continue;
				}
				Triangle3D tri = _tin.Triangle3D[trianglesAroundPointCCW];
				int point012ByGlobalIdx = triangleDefinition.Point012ByGlobalIdx(i);
				int intervalIndex;
				int intervalIndex2;
				switch (point012ByGlobalIdx)
				{
				case 0:
					intervalIndex = 1;
					intervalIndex2 = 2;
					break;
				case 1:
					intervalIndex = 2;
					intervalIndex2 = 0;
					break;
				case 2:
					intervalIndex = 0;
					intervalIndex2 = 1;
					break;
				default:
					throw new Exception("Bad p012");
				}
				Point intercept = _tin.Points[i];
				double p = _tin.Z[i];
				double p2 = _tin.M[i];
				double d_dx = _zGradients[i, 0];
				double d_dy = _zGradients[i, 1];
				double d_dx2 = _mGradients[i, 0];
				double d_dy2 = _mGradients[i, 1];
				int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(point012ByGlobalIdx);
				if (_tin.TriangleDefinition[adjacentTriangleIdx].IsDeleted)
				{
					_Z[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex]] = 2.0 / 3.0 * _Z[trianglesAroundPointCCW, DoF.V[point012ByGlobalIdx]] + 1.0 / 3.0 * _Z[trianglesAroundPointCCW, DoF.V[intervalIndex]];
					_M[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex]] = 2.0 / 3.0 * _M[trianglesAroundPointCCW, DoF.V[point012ByGlobalIdx]] + 1.0 / 3.0 * _M[trianglesAroundPointCCW, DoF.V[intervalIndex]];
				}
				else
				{
					Point pnt = StencilPoint2D(tri, DoF.T[point012ByGlobalIdx, intervalIndex]);
					if (double.IsNaN(_zGradients[i, 0]))
					{
						Vector vector = 0.5 * (TriangleGradientZ(trianglesAroundPointCCW) + TriangleGradientZ(adjacentTriangleIdx));
						Vector vector2 = 0.5 * (TriangleGradientM(trianglesAroundPointCCW) + TriangleGradientM(adjacentTriangleIdx));
						d_dx = vector.DeltaX;
						d_dy = vector.DeltaY;
						d_dx2 = vector2.DeltaX;
						d_dy2 = vector2.DeltaY;
					}
					_Z[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex]] = EvaluatePlane(intercept, p, d_dx, d_dy, pnt);
					_M[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex]] = EvaluatePlane(intercept, p2, d_dx2, d_dy2, pnt);
				}
				int adjacentTriangleIdx2 = triangleDefinition.AdjacentTriangleIdx(intervalIndex2);
				if (_tin.TriangleDefinition[adjacentTriangleIdx2].IsDeleted)
				{
					_Z[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex2]] = 2.0 / 3.0 * _Z[trianglesAroundPointCCW, DoF.V[point012ByGlobalIdx]] + 1.0 / 3.0 * _Z[trianglesAroundPointCCW, DoF.V[intervalIndex2]];
					_M[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex2]] = 2.0 / 3.0 * _M[trianglesAroundPointCCW, DoF.V[point012ByGlobalIdx]] + 1.0 / 3.0 * _M[trianglesAroundPointCCW, DoF.V[intervalIndex2]];
				}
				else
				{
					Point pnt = StencilPoint2D(tri, DoF.T[point012ByGlobalIdx, intervalIndex2]);
					if (double.IsNaN(_zGradients[i, 0]))
					{
						Vector vector3 = 0.5 * (TriangleGradientZ(trianglesAroundPointCCW) + TriangleGradientZ(adjacentTriangleIdx2));
						Vector vector4 = 0.5 * (TriangleGradientM(trianglesAroundPointCCW) + TriangleGradientM(adjacentTriangleIdx2));
						d_dx = vector3.DeltaX;
						d_dy = vector3.DeltaY;
						d_dx2 = vector4.DeltaX;
						d_dy2 = vector4.DeltaY;
					}
					_Z[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex2]] = EvaluatePlane(intercept, p, d_dx, d_dy, pnt);
					_M[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex2]] = EvaluatePlane(intercept, p2, d_dx2, d_dy2, pnt);
				}
				_Z[trianglesAroundPointCCW, DoF.I[point012ByGlobalIdx, 1]] = 1.0 / 3.0 * (_Z[trianglesAroundPointCCW, DoF.V[point012ByGlobalIdx]] + _Z[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex]] + _Z[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex2]]);
				_M[trianglesAroundPointCCW, DoF.I[point012ByGlobalIdx, 1]] = 1.0 / 3.0 * (_M[trianglesAroundPointCCW, DoF.V[point012ByGlobalIdx]] + _M[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex]] + _M[trianglesAroundPointCCW, DoF.T[point012ByGlobalIdx, intervalIndex2]]);
				t = trianglesAroundPointCCW;
			}
		}
	}

	public Point3D StencilPoint(int t, int dof)
	{
		Point3D result = StencilPoint3D(_tin.Triangle3D[t], dof);
		result.Z = _Z[t, dof];
		return result;
	}

	public Point4D StencilPoint4D(int t, int dof)
	{
		Point point = StencilPoint2D(_tin.Triangle3D[t], dof);
		return new Point4D(point.X, point.Y, _Z[t, dof], _M[t, dof]);
	}

	private Point3D StencilPoint3D(Triangle3D tri, int dof)
	{
		return StencilPoint(tri.PointA, tri.PointB, tri.PointC, dof);
	}

	private Point StencilPoint2D(Triangle3D tri, int dof)
	{
		return StencilPoint3D(tri, dof).AsPoint2D();
	}

	private Point StencilPoint2D(int t, int dof)
	{
		return StencilPoint3D(_tin.Triangle3D[t], dof).AsPoint2D();
	}

	private Point3D StencilPoint(Point3D PointA, Point3D PointB, Point3D PointC, int dof)
	{
		return dof switch
		{
			0 => PointA, 
			1 => PointB, 
			2 => PointC, 
			6 => 2.0 / 3.0 * PointA + 1.0 / 3.0 * PointB, 
			8 => 2.0 / 3.0 * PointA + 1.0 / 3.0 * PointC, 
			7 => 2.0 / 3.0 * PointB + 1.0 / 3.0 * PointA, 
			10 => 2.0 / 3.0 * PointB + 1.0 / 3.0 * PointC, 
			9 => 2.0 / 3.0 * PointC + 1.0 / 3.0 * PointA, 
			11 => 2.0 / 3.0 * PointC + 1.0 / 3.0 * PointB, 
			12 => 7.0 / 9.0 * PointA + 1.0 / 9.0 * PointB + 1.0 / 9.0 * PointC, 
			14 => 7.0 / 9.0 * PointB + 1.0 / 9.0 * PointC + 1.0 / 9.0 * PointA, 
			16 => 7.0 / 9.0 * PointC + 1.0 / 9.0 * PointA + 1.0 / 9.0 * PointB, 
			13 => 5.0 / 9.0 * PointA + 2.0 / 9.0 * PointB + 2.0 / 9.0 * PointC, 
			15 => 5.0 / 9.0 * PointB + 2.0 / 9.0 * PointC + 2.0 / 9.0 * PointA, 
			17 => 5.0 / 9.0 * PointC + 2.0 / 9.0 * PointA + 2.0 / 9.0 * PointB, 
			3 => 4.0 / 9.0 * PointB + 4.0 / 9.0 * PointC + 1.0 / 9.0 * PointA, 
			4 => 4.0 / 9.0 * PointC + 4.0 / 9.0 * PointA + 1.0 / 9.0 * PointB, 
			5 => 4.0 / 9.0 * PointA + 4.0 / 9.0 * PointB + 1.0 / 9.0 * PointC, 
			18 => 1.0 / 3.0 * (PointA + PointB + PointC), 
			_ => throw new Exception("Unhandled DoF"), 
		};
	}

	public double EvaluatePlane(Point3D intercept, double d_dx, double d_dy, Point3D pnt)
	{
		return intercept.Z + (pnt.X - intercept.X) * d_dx + (pnt.Y - intercept.Y) * d_dy;
	}

	public double EvaluatePlane(Point intercept, double p0, double d_dx, double d_dy, Point pnt)
	{
		return p0 + (pnt.X - intercept.X) * d_dx + (pnt.Y - intercept.Y) * d_dy;
	}

	private void ComputeC()
	{
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
			if (triangleDefinition.IsDeleted)
			{
				continue;
			}
			for (int j = 0; j < 3; j++)
			{
				int ordinal = (j + 1) % 3;
				int ordinal2 = (j + 2) % 3;
				Point4D point4D = StencilPoint4D(i, DoF.T[ordinal, ordinal2]);
				Point4D point4D2 = StencilPoint4D(i, DoF.T[ordinal2, ordinal]);
				Point4D point4D3 = StencilPoint4D(i, DoF.I[ordinal, 1]);
				Point4D point4D4 = StencilPoint4D(i, DoF.I[ordinal2, 1]);
				int at = ordinal;
				Vector4D vector4D;
				if (!_tin.TriangleDefinition[triangleDefinition.AdjacentTriangleIdx(at)].IsDeleted)
				{
					Point4D point4D5 = OppStencilPoint4D(i, DoF.I[ordinal, 1], at);
					Point4D point4D6 = OppStencilPoint4D(i, DoF.I[ordinal2, 1], at);
					vector4D = point4D3 + point4D4 - (point4D5 + point4D6);
				}
				else
				{
					vector4D = point4D3 + point4D4 - (point4D + point4D2);
				}
				Point4D point4D7 = point4D + vector4D;
				Plane plane = new Plane(point4D, point4D2, point4D7);
				Point point = StencilPoint2D(i, DoF.C[j]);
				_Z[i, DoF.C[j]] = plane.Z(point.X, point.Y);
				_M[i, DoF.C[j]] = plane.M(point.X, point.Y);
			}
		}
	}

	private Point4D OppStencilPoint4D(int t, int dof, int at012)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		int dofP0Term = -1;
		dofP0Term = dof switch
		{
			12 => triangleDefinition.p0, 
			14 => triangleDefinition.p1, 
			16 => triangleDefinition.p2, 
			_ => throw new Exception("Unhandled opposite degree-of-freedom"), 
		};
		int adjacentTriangleIdx = _tin.TriangleDefinition[t].AdjacentTriangleIdx(at012);
		TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[adjacentTriangleIdx];
		if (triangleDefinition2.IsDeleted)
		{
			throw new Exception("Opposite stencil point requested on deleted triangle");
		}
		int dof2;
		if (triangleDefinition2.p0 == dofP0Term)
		{
			dof2 = 12;
		}
		else if (triangleDefinition2.p1 == dofP0Term)
		{
			dof2 = 14;
		}
		else
		{
			if (triangleDefinition2.p2 != dofP0Term)
			{
				throw new Exception("Unexpected point, not found in neighbor");
			}
			dof2 = 16;
		}
		return StencilPoint4D(adjacentTriangleIdx, dof2);
	}

	private void ComputeI2S()
	{
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			if (!_tin.TriangleDefinition[i].IsDeleted)
			{
				Point point = StencilPoint2D(i, 13);
				Plane plane = new Plane(StencilPoint4D(i, 4), StencilPoint4D(i, 5), StencilPoint4D(i, 12));
				_Z[i, 13] = plane.Z(point.X, point.Y);
				_M[i, 13] = plane.M(point.X, point.Y);
				point = StencilPoint2D(i, 15);
				plane = new Plane(StencilPoint4D(i, 5), StencilPoint4D(i, 3), StencilPoint4D(i, 14));
				_Z[i, 15] = plane.Z(point.X, point.Y);
				_M[i, 15] = plane.M(point.X, point.Y);
				point = StencilPoint2D(i, 17);
				plane = new Plane(StencilPoint4D(i, 3), StencilPoint4D(i, 4), StencilPoint4D(i, 16));
				_Z[i, 17] = plane.Z(point.X, point.Y);
				_M[i, 17] = plane.M(point.X, point.Y);
				_Z[i, 18] = 1.0 / 3.0 * (_Z[i, 13] + _Z[i, 15] + _Z[i, 17]);
				_M[i, 18] = 1.0 / 3.0 * (_M[i, 13] + _M[i, 15] + _M[i, 17]);
			}
		}
	}
}
