using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors;

public class MeshReport
{
	public readonly List<int> Selection;

	private Func<Mesh, int, float> ComputeOne;

	public Mesh Mesh { get; private set; }

	public MeshMetrics.MetricElement ElementType { get; private set; }

	public MeshMetrics.MetricType MetricType { get; private set; }

	public float[] Result { get; private set; }

	public static MeshReport ComputeMeshReport(Mesh mesh, MeshMetrics.MetricType metric, MeshMetrics.MetricElement elementType, List<int> selection = null)
	{
		MeshReport meshReport = new MeshReport(mesh, metric, elementType, selection);
		meshReport.Compute();
		return meshReport;
	}

	public static MeshReport ComputeMeshReport(Mesh mesh, Func<Mesh, int, float> func, MeshMetrics.MetricElement elementType, List<int> selection = null)
	{
		MeshReport meshReport = new MeshReport(mesh, func, elementType, selection);
		meshReport.Compute();
		return meshReport;
	}

	private MeshReport(Mesh mesh, MeshMetrics.MetricType metric, MeshMetrics.MetricElement elementType, List<int> selection)
	{
		Mesh = mesh;
		MetricType = metric;
		ElementType = elementType;
		Selection = selection;
		ComputeOne = MeshMetrics.GetFunc(ElementType, MetricType);
	}

	private MeshReport(Mesh mesh, Func<Mesh, int, float> func, MeshMetrics.MetricElement elementType, List<int> selection)
	{
		Mesh = mesh;
		MetricType = MeshMetrics.MetricType.Custom;
		ElementType = elementType;
		Selection = selection;
		ComputeOne = func;
	}

	public void Compute()
	{
		IEnumerable<int> enumerable;
		if (Selection != null)
		{
			Result = new float[Selection.Count];
			enumerable = Selection;
		}
		else if (ElementType == MeshMetrics.MetricElement.Cell)
		{
			Result = new float[Mesh.CellCount];
			enumerable = Enumerable.Range(0, Mesh.CellCount);
		}
		else if (ElementType == MeshMetrics.MetricElement.Face)
		{
			Result = new float[Mesh.FaceCount];
			enumerable = Enumerable.Range(0, Mesh.FaceCount);
		}
		else
		{
			if (ElementType != MeshMetrics.MetricElement.Node)
			{
				throw new ArgumentOutOfRangeException($"Bad element type {ElementType}");
			}
			Result = new float[Mesh.NodeCount];
			enumerable = Enumerable.Range(0, Mesh.NodeCount);
		}
		foreach (int enumerable2 in enumerable)
		{
			Result[enumerable2] = ComputeOne(Mesh, enumerable2);
		}
	}

	public float ComputeQuantile(float quantile)
	{
		if (Result == null || Result.Length == 0)
		{
			return float.NaN;
		}
		float[] floatValues = Result.ToArray();
		Array.Sort(floatValues);
		int quantileTerm = (int)(quantile * (float)(floatValues.Length - 1));
		return floatValues[quantileTerm];
	}

	public float[] ComputeQuantiles(float[] quantiles)
	{
		if (Result == null || Result.Length == 0)
		{
			return null;
		}
		float[] floatValues = Result.ToArray();
		Array.Sort(floatValues);
		float[] floatValuesArray = new float[quantiles.Length];
		for (int i = 0; i < quantiles.Length; i++)
		{
			int quantilesTerm = (int)(quantiles[i] * (float)(floatValues.Length - 1));
			floatValuesArray[i] = floatValues[quantilesTerm];
		}
		return floatValuesArray;
	}
}
