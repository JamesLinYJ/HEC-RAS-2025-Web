using System;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public class StreamwiseData
{
	public List<int> Cells;

	private List<int> _starts;

	private List<int> _counts;

	private Dictionary<int, int> _cellMap;

	private List<Vector> CellFaceNormals;

	public StreamwiseData()
	{
		Cells = new List<int>();
		CellFaceNormals = new List<Vector>();
		_starts = new List<int>();
		_counts = new List<int>();
		_cellMap = new Dictionary<int, int>();
	}

	private StreamwiseData(StreamwiseData src)
	{
		Cells = new List<int>(src.Cells);
		_starts = new List<int>(src._starts);
		_counts = new List<int>(src._counts);
		CellFaceNormals = new List<Vector>(src.CellFaceNormals);
		BuildCellMap();
	}

	private StreamwiseData(double[,] normals, int[] cells, int[] starts, int[] counts)
	{
		Cells = new List<int>(cells);
		_starts = new List<int>(starts);
		_counts = new List<int>(counts);
		CellFaceNormals = new List<Vector>(normals.GetLength(0));
		for (int i = 0; i < normals.GetLength(0); i++)
		{
			CellFaceNormals.Add(new Vector(normals[i, 0], normals[i, 1]));
		}
		BuildCellMap();
	}

	private void BuildCellMap()
	{
		_cellMap = new Dictionary<int, int>(Cells.Count);
		for (int i = 0; i < Cells.Count; i++)
		{
			_cellMap[Cells[i]] = i;
		}
	}

	public StreamwiseData Clone()
	{
		return new StreamwiseData(this);
	}

	public StreamwiseData Subset(Mesh subsetMesh, int[] cellMapping, (int, int)[] faceMapping)
	{
		StreamwiseData streamwiseData = new StreamwiseData();
		for (int i = 0; i < cellMapping.Length; i++)
		{
			int key = cellMapping[i];
			if (!_cellMap.TryGetValue(key, out var localValue))
			{
				continue;
			}
			List<Vector> range = CellFaceNormals.GetRange(_starts[localValue], _counts[localValue]);
			Memory<int> faces = subsetMesh.Cells[i].Faces;
			for (int j = 0; j < faces.Length; j++)
			{
				if (faceMapping[j].Item2 < 0)
				{
					range[j] *= -1f;
				}
			}
			streamwiseData.AddCellData(i, range);
		}
		return streamwiseData;
	}

	public void AddCellData(int cIdx, List<Vector> faceNormals)
	{
		Cells.Add(cIdx);
		_starts.Add(CellFaceNormals.Count);
		_counts.Add(faceNormals.Count);
		CellFaceNormals.AddRange(faceNormals);
		_cellMap[cIdx] = Cells.Count - 1;
	}

	public List<Vector> GetFaceNormals(int cIdx)
	{
		if (!_cellMap.TryGetValue(cIdx, out var localValue))
		{
			throw new KeyNotFoundException($"No streamwise data found for cell index {cIdx}.");
		}
		return CellFaceNormals.GetRange(_starts[localValue], _counts[localValue]);
	}

	public void ApplyCellOrdering(int[] cellOldToNew)
	{
		for (int i = 0; i < Cells.Count; i++)
		{
			Cells[i] = cellOldToNew[Cells[i]];
		}
		BuildCellMap();
	}

	public double[,] CellFaceNormalsAsJaggedArray()
	{
		if (CellFaceNormals.Count == 0)
		{
			return null;
		}
		double[,] doubleValues = new double[CellFaceNormals.Count, 2];
		for (int i = 0; i < CellFaceNormals.Count; i++)
		{
			doubleValues[i, 0] = CellFaceNormals[i].X;
			doubleValues[i, 1] = CellFaceNormals[i].Y;
		}
		return doubleValues;
	}

	public int[] JaggedStarts()
	{
		return _starts.ToArray();
	}

	public int[] JaggedCounts()
	{
		return _counts.ToArray();
	}

	public static StreamwiseData FromJagged(double[,] normals, int[] cells, int[] starts, int[] counts)
	{
		return new StreamwiseData(normals, cells, starts, counts);
	}
}
