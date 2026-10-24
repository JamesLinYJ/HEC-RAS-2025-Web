using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Utility.Logging;
using Utility.Matrices;
using Utility.Progress;
using Utility.Systems;

namespace Geospatial.Vectors.MeshTopology;

public class TinLaplacian
{
	public static class MessageCodes
	{
		public static readonly MessageClass NonConvergence = new MessageClass("TinLaplacian_NonConvergence", MessageSeverity.Warning, "Matrix solution did not converge while computing Laplacian ({ConvergenceStatus}). Quads may be corrupt");

		public static Message GetNonConvergence(ConvergenceStatus status)
		{
			return NonConvergence.Create(status);
		}
	}

	private Tin _tin;

	private int maxIterations = 2500;

	private int _triGradient;

	public Tin Tin => _tin;

	public TinLaplacian(Tin tin)
	{
		_tin = tin;
	}

	public bool BuildMatrix(double[] nodeValues, Dictionary<int, double> gradients, out SparseMatrix<double> A, out double[] b, out double[] x)
	{
		List<List<int>> intListList = (from nIdx in Enumerable.Range(0, _tin.PointCount)
			select new List<int>()).ToList();
		List<List<double>> doubleListList = (from nIdx in Enumerable.Range(0, _tin.PointCount)
			select new List<double>()).ToList();
		for (int intervalIndex = 0; intervalIndex < _tin.TriangleCount; intervalIndex++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[intervalIndex];
			Triangle triangle = _tin.Triangles[intervalIndex];
			if (triangleDefinition.IsDeleted)
			{
				continue;
			}
			double x2 = triangle.PointA.X;
			double y = triangle.PointA.Y;
			double x3 = triangle.PointB.X;
			double y2 = triangle.PointB.Y;
			double x4 = triangle.PointC.X;
			double y3 = triangle.PointC.Y;
			double[] doubleValues = new double[3]
			{
				y2 - y3,
				y3 - y,
				y - y2
			};
			double[] doubleValuesArray = new double[3]
			{
				x3 - x4,
				x4 - x2,
				x2 - x3
			};
			double scaledArea = 0.5 * triangle.Area;
			if (scaledArea == 0.0)
			{
				continue;
			}
			for (int intervalIndex2 = 0; intervalIndex2 < 3; intervalIndex2++)
			{
				int index = triangleDefinition.Point(intervalIndex2);
				for (int intervalIndex3 = 0; intervalIndex3 < 3; intervalIndex3++)
				{
					if (intervalIndex2 != intervalIndex3)
					{
						int point = triangleDefinition.Point(intervalIndex3);
						int indexOf = intListList[index].IndexOf(point);
						if (indexOf == -1)
						{
							indexOf = intListList[index].Count;
							intListList[index].Add(point);
							doubleListList[index].Add(0.0);
						}
						doubleListList[index][indexOf] += (doubleValues[intervalIndex2] * doubleValues[intervalIndex3] + doubleValuesArray[intervalIndex2] * doubleValuesArray[intervalIndex3]) / scaledArea;
					}
				}
			}
		}
		int pointCount = _tin.PointCount;
		int pointCount2 = pointCount;
		for (int intervalIndex4 = 0; intervalIndex4 < _tin.PointCount; intervalIndex4++)
		{
			pointCount2 += intListList[intervalIndex4].Count;
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		for (int intervalIndex5 = 0; intervalIndex5 < _tin.PointCount; intervalIndex5++)
		{
			if (double.IsFinite(nodeValues[intervalIndex5]))
			{
				if (nodeValues[intervalIndex5] < maxValue)
				{
					maxValue = nodeValues[intervalIndex5];
				}
				if (nodeValues[intervalIndex5] > minValue)
				{
					minValue = nodeValues[intervalIndex5];
				}
			}
		}
		double maxValueMinValueTerm = (maxValue + minValue) * 0.5;
		A = new SparseMatrix<double>();
		A.Initialize(pointCount, pointCount2);
		List<int> intList = new List<int>();
		List<double> doubleList = new List<double>();
		b = new double[pointCount];
		x = new double[pointCount];
		int ordinal = 0;
		double computed = 0.0;
		for (int intervalIndex6 = 0; intervalIndex6 < _tin.PointCount; intervalIndex6++)
		{
			intList.Clear();
			doubleList.Clear();
			if (double.IsFinite(nodeValues[intervalIndex6]))
			{
				SetDirichletBC(intervalIndex6, nodeValues[intervalIndex6], intList, doubleList, b, x);
				ordinal++;
			}
			else if (gradients != null && gradients.ContainsKey(intervalIndex6))
			{
				SetGradientBC(intervalIndex6, gradients[intervalIndex6], intList, doubleList, b, x);
				x[intervalIndex6] = maxValueMinValueTerm;
			}
			else
			{
				double computed2 = 0.0;
				for (int intervalIndex7 = 0; intervalIndex7 < intListList[intervalIndex6].Count; intervalIndex7++)
				{
					intList.Add(intListList[intervalIndex6][intervalIndex7]);
					double computed3 = doubleListList[intervalIndex6][intervalIndex7];
					doubleList.Add(computed3);
					computed2 += computed3;
				}
				if (computed2 == 0.0)
				{
					intList.Add(intervalIndex6);
					doubleList.Add(1.0);
					x[intervalIndex6] = 0.0;
					b[intervalIndex6] = 0.0;
					ordinal++;
				}
				else
				{
					intList.Add(intervalIndex6);
					doubleList.Add(0.0 - computed2);
					b[intervalIndex6] = 0.0;
					x[intervalIndex6] = maxValueMinValueTerm;
					computed = Math.Max(computed, 0.0 - computed2);
				}
			}
			A.BuildRow(intervalIndex6, intList.ToArray(), doubleList.ToArray());
		}
		return ordinal < pointCount;
	}

	private void SetDirichletBC(int i, double computed, List<int> columns, List<double> values, double[] b, double[] x)
	{
		columns.Add(i);
		values.Add(1.0);
		b[i] = computed;
		x[i] = b[i];
	}

	private void SetGradientBC(int n0, double gradientAngle, List<int> columns, List<double> alphas, double[] b, double[] x)
	{
		double gradientAnglePiTerm = gradientAngle + Math.PI / 2.0;
		double computed = Math.Cos(gradientAnglePiTerm);
		double computed2 = Math.Sin(gradientAnglePiTerm);
		_tin.FindTriangleContainingPoint(_tin.Points[n0], ref _triGradient);
		List<int> boundaryTrianglesAroundPointCCW = _tin.BoundaryTrianglesAroundPointCCW(n0, out var _, _triGradient);
		int count = boundaryTrianglesAroundPointCCW.Count;
		int count2 = count + 1;
		List<int> intList = new List<int>();
		for (int i = 0; i < boundaryTrianglesAroundPointCCW.Count; i++)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[boundaryTrianglesAroundPointCCW[i]];
			if (i == 0)
			{
				intList.Add(triangleDefinition.GetCounterClockwisePointWithinTriangle(n0));
			}
			intList.Add(triangleDefinition.GetClockwisePointWithinTriangle(n0));
		}
		Point point = _tin.Points[n0];
		double computed3 = 0.0;
		for (int j = 0; j < count2; j++)
		{
			double computed4 = 0.0;
			if (j > 0)
			{
				Point point2 = _tin.Points[intList[j - 1]];
				computed4 += computed * (point.Y - point2.Y);
				computed4 += computed2 * (point2.X - point.X);
			}
			if (j < count)
			{
				Point point3 = _tin.Points[intList[j + 1]];
				computed4 += computed * (point3.Y - point.Y);
				computed4 += computed2 * (point.X - point3.X);
				Point point4 = _tin.Points[intList[j]];
				computed3 += computed / 2.0 * (point4.Y - point3.Y);
				computed3 += computed2 / 2.0 * (point3.X - point4.X);
			}
			alphas.Add(computed4 / 2.0);
			columns.Add(intList[j]);
		}
		b[n0] = 0.0;
		x[n0] = 0.5;
		alphas.Add(computed3);
		if (computed3 < 0.0)
		{
			for (int k = 0; k < alphas.Count; k++)
			{
				alphas[k] = 0.0 - alphas[k];
			}
		}
		columns.Add(n0);
	}

	public bool SolveLaplacian(double[] nodeValues, Dictionary<int, double> gradients = null, ProgressReporter cancelReporter = null)
	{
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.BuildLaplace);
		if (!BuildMatrix(nodeValues, gradients, out var A, out var b, out var x))
		{
			MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.BuildLaplace);
			return true;
		}
		A.Pack();
		A.Sort();
		if (cancelReporter != null && cancelReporter.CancelRequested)
		{
			throw new OperationCanceledException();
		}
		if (gradients != null && gradients.Count > 0)
		{
			double[,] doubleValues = new double[A.RowCount, A.RowCount];
			A.ToDense(doubleValues);
			double[,] dense = DenseMatrix<double>.TransposeMultiply(doubleValues, doubleValues);
			b = DenseMatrix<double>.TransposeMultiply(doubleValues, b);
			A = new SparseMatrix<double>(dense);
			A.Pack();
			A.Sort();
			SparseMatrix<double>.CheckSymmetric(A, 1E-10);
		}
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.BuildLaplace);
		if (cancelReporter != null && cancelReporter.CancelRequested)
		{
			throw new OperationCanceledException();
		}
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.SolveLaplace);
		Eliminate<double>.Compute(A, b, out var indexMap, out var rhsBound, out var AFree, out var rhsFree);
		SparseMatrix<double>.CheckSymmetric(AFree, 1E-10);
		double[] doubleValuesArray = new double[rhsFree.Length];
		for (int i = 0; i < A.RowCount; i++)
		{
			if (indexMap[i] >= 0)
			{
				doubleValuesArray[indexMap[i]] = x[i];
			}
		}
		if (doubleValuesArray.Length == 1)
		{
			doubleValuesArray[0] = rhsFree[0] / AFree.Values[0];
		}
		else
		{
			PCG<double> pCG = new PCG<double>();
			pCG.Convergence.ToleranceRMSE = 1E-08;
			double[] doubleValues2 = (double[])doubleValuesArray.Clone();
			pCG.Convergence.MaxIterations = Math.Max(4, AFree.RowCount);
			pCG.Convergence.MinIterations = 2;
			OperationResult operationResult = pCG.PrepareAndSolve(AFree, doubleValuesArray, rhsFree, new IC<double>(), null, cancelReporter);
			if (!operationResult)
			{
				throw new StructuredException(operationResult);
			}
			if (pCG.Convergence.Status != ConvergenceStatus.Converged && pCG.Convergence.Status != ConvergenceStatus.SmallNorm)
			{
				pCG = new PCG<double>();
				pCG.Convergence.MaxIterations = 5 * AFree.RowCount;
				doubleValuesArray = doubleValues2;
				pCG.Solve(AFree, doubleValuesArray, rhsFree);
			}
			if (pCG.Convergence.Status != ConvergenceStatus.Converged && pCG.Convergence.Status != ConvergenceStatus.SmallNorm)
			{
				throw new StructuredException(MessageCodes.GetNonConvergence(pCG.Convergence.Status));
			}
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		for (int j = 0; j < nodeValues.Length; j++)
		{
			if (!double.IsNaN(nodeValues[j]))
			{
				maxValue = Math.Min(maxValue, nodeValues[j]);
				minValue = Math.Max(minValue, nodeValues[j]);
			}
		}
		double minValueAboveMaxValue = minValue - maxValue;
		for (int k = 0; k < A.RowCount; k++)
		{
			if (indexMap[k] >= 0)
			{
				x[k] = doubleValuesArray[indexMap[k]];
				if (x[k] < maxValue)
				{
					x[k] = maxValue + 1E-06 * minValueAboveMaxValue;
				}
				else if (x[k] > minValue)
				{
					x[k] = minValue - 1E-06 * minValueAboveMaxValue;
				}
			}
			else
			{
				x[k] = rhsBound[~indexMap[k]];
			}
		}
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.SolveLaplace);
		for (int l = 0; l < 8; l++)
		{
			double computed = 0.0;
			int ordinal = 0;
			int startingTriangle = _tin.FindTriangleContainingPoint(_tin.Points[l]);
			foreach (Tuple<int, bool> pointsAroundPointCCW in _tin.PointsAroundPointCCW(l, startingTriangle))
			{
				if (pointsAroundPointCCW.Item1 >= 8)
				{
					ordinal++;
					computed += x[pointsAroundPointCCW.Item1];
				}
			}
			if (ordinal > 0)
			{
				nodeValues[l] = computed / (double)ordinal;
			}
		}
		for (int m = 8; m < _tin.PointCount; m++)
		{
			nodeValues[m] = x[m];
		}
		return true;
	}

	public void SolveByFixedPoint(SparseMatrix<double> A, double[] b, double[] x, double absTol)
	{
		A.ComputeDiagonalPointers();
		double diagonalPointerTerm = 0.0;
		int[] diagonalPointers = A.DiagonalPointers;
		foreach (int diagonalPointer in diagonalPointers)
		{
			diagonalPointerTerm = Math.Max(diagonalPointerTerm, A.Values[diagonalPointer]);
		}
		double computed = 1.0 / diagonalPointerTerm;
		double[] doubleValues = new double[_tin.PointCount];
		for (int j = 0; j < maxIterations; j++)
		{
			SparseMatrix<double>.Product(A, x, doubleValues);
			double computed2 = 0.0;
			for (int k = 0; k < _tin.PointCount; k++)
			{
				double computed3 = computed * (doubleValues[k] - b[k]);
				computed2 = Math.Max(computed2, Math.Abs(computed3));
				x[k] -= computed3;
			}
			if (computed2 < absTol)
			{
				break;
			}
		}
	}

	public void DumpMatrixProblem(SparseMatrix<double> A, double[] b, string label, double[] x, int[] indexMap, double[] rhsBound)
	{
		string filename = "C:\\Temp\\sparseA-" + label + ".bin";
		A.StreamOutToFile(filename);
		using (FileStream output = File.Create("C:\\Temp\\rhs-" + label + ".bin"))
		{
			using BinaryWriter binaryWriter = new BinaryWriter(output);
			foreach (double computed in b)
			{
				binaryWriter.Write(computed);
			}
		}
		double[,] doubleValues = new double[A.RowCount, A.RowCount];
		A.ToDense(doubleValues);
		List<List<object>> localValue = new List<List<object>>
		{
			new List<object> { "SET", "tin_raw", _tin },
			new List<object>
			{
				"SET",
				"A_raw",
				ToSimple(doubleValues)
			},
			new List<object> { "SET", "b_raw", b },
			new List<object> { "SET", "x_raw", x },
			new List<object> { "SET", "indexMap_raw", indexMap },
			new List<object> { "SET", "rhsBound_raw", rhsBound },
			new List<object> { "PYTHON", "\r\ntin=json_to_mesh(tin_raw)\r\n# Drop exterior triangles.\r\n[tin.delete_cell(c) for c in np.nonzero(tin.cells['state']==1)[0]]\r\nfig,ax = plt.subplots(num = 10, clear = 1,figsize=(8.4,7))\r\nax.set_adjustable('datalim')\r\nax.set_position([0,0,1,1])\r\ntin.plot_edges(ax = ax, lw = 0.7, color='k', alpha = 0.6)\r\nax.axis('equal')\r\nax.axis('off')\r\nA=np.array(A_raw)\r\nb=np.array(b_raw)\r\nx=np.array(x_raw)\r\nif indexMap_raw is not None:\r\n    indexMap=np.array(indexMap_raw)\r\n    rhsBound=np.array(rhsBound_raw)\r\n    xFull = np.where(indexMap>=0, x[indexMap.clip(0)], rhsBound[(~indexMap).clip(0)])\r\nelse:\r\n    xFull=x\r\ntin.contour_node_values(xFull, np.linspace(0,1,50), colors='r')\r\n" }
		};
		using (FileStream utf8Json = File.Create("C:\\Temp\\cmd.json"))
		{
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true,
				NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
				Converters = { (JsonConverter)new TinJsonConverter() }
			};
			JsonSerializer.Serialize(utf8Json, localValue, options);
		}
		static List<List<double>> ToSimple(double[,] M)
		{
			List<List<double>> doubleListList = new List<List<double>>();
			for (int j = 0; j < M.GetLength(0); j++)
			{
				List<double> doubleList = new List<double>();
				for (int k = 0; k < M.GetLength(1); k++)
				{
					doubleList.Add(M[j, k]);
				}
				doubleListList.Add(doubleList);
			}
			return doubleListList;
		}
	}
}
