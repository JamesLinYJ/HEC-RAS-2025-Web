using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Utility.Extensions;
using Utility.Systems;

namespace Utility.Matrices;

public class SparseMatrix<TStorage> : IStreamableObject where TStorage : unmanaged
{
	private const int ElementMinimumWorkForParallel = 10000;

	private const int ElementTargetWorkPerChunk = 10000;

	private const int DifferentStructureMinimumWorkForParallel = 20000;

	private const int RowTargetNonZerosPerChunk = 10000;

	private const int ProductMinimumRowsForParallel = 10000;

	private const int ProductMinimumNonZerosForParallel = 20000;

	private const int UpperProductMinimumNonZerosForParallel = 100000;

	private const int ResidualMinimumNonZerosForParallel = 20000;

	private const int ResidualTargetNonZerosPerChunk = 5000;

	private const int ScaleMinimumNonZerosForParallel = 20000;

	public bool IsUpper;

	private int _rowCount;

	public int[] RowPointers;

	public int[] ColumnIndices;

	public int[] ColumnPointers;

	public bool UpToDateColumnPointers;

	public int[] DiagonalPointers;

	public bool UpToDateDiagonalPointers;

	public int[] TransposePointers;

	public bool UpToDateTransposePointers;

	public TStorage[] Values;

	public bool HasSortedIndices;

	public int RowCount
	{
		get
		{
			if (_rowCount != 0)
			{
				return _rowCount;
			}
			return MaxRows;
		}
		set
		{
			InvalidateStructureCaches();
			_rowCount = value;
		}
	}

	public int MaxRows => Math.Max(RowPointers.Length, 1) - 1;

	public int NonZeroCount
	{
		get
		{
			if (RowPointers.Length != 0)
			{
				return RowPointers[RowCount];
			}
			return 0;
		}
	}

	public int MaxNonZero => ColumnIndices.Length;

	public SparseMatrix()
	{
		Deallocate();
	}

	public SparseMatrix(int maxRows, int maxNonZero)
	{
		Deallocate();
		Initialize(maxRows, maxNonZero);
	}

	public SparseMatrix(int maxRows, int[] rowPointers, int[] columnIndices, TStorage[] values)
	{
		ArgumentNullException.ThrowIfNull(rowPointers, "rowPointers");
		ArgumentNullException.ThrowIfNull(columnIndices, "columnIndices");
		ArgumentNullException.ThrowIfNull(values, "values");
		if (rowPointers.Length < maxRows + 1)
		{
			throw new ArgumentException("Row pointer array is too small.", "rowPointers");
		}
		if (values.Length < columnIndices.Length)
		{
			throw new ArgumentException("Values array is smaller than the column index array.", "values");
		}
		Deallocate();
		_rowCount = maxRows;
		RowPointers = rowPointers;
		ColumnIndices = columnIndices;
		Values = values;
		VerifySortedIndices();
	}

	public SparseMatrix(SparseMatrix<TStorage> source, bool cloneData = false)
	{
		Deallocate();
		Initialize(source, cloneData);
	}

	public SparseMatrix(SparseMatrix<TStorage> first, SparseMatrix<TStorage> second)
	{
		Deallocate();
		Initialize(first, second);
	}

	public SparseMatrix(TStorage[,] dense)
	{
		Deallocate();
		Initialize(dense);
	}

	public SparseMatrix(TStorage[,] dense, int[,] columns)
	{
		Deallocate();
		Initialize(dense, columns);
	}

	public SparseMatrix(int[] rowPointers, int[] columnIndices, TStorage[] values = null)
	{
		ArgumentNullException.ThrowIfNull(rowPointers, "rowPointers");
		ArgumentNullException.ThrowIfNull(columnIndices, "columnIndices");
		Deallocate();
		Initialize(rowPointers.Length - 1, columnIndices.Length);
		SetStructure(rowPointers, columnIndices);
		if (values != null)
		{
			SetData(values);
		}
		VerifySortedIndices();
	}

	public SparseMatrix(int maxRows, int[] rowPointers, int[] columnIndices)
	{
		ArgumentNullException.ThrowIfNull(rowPointers, "rowPointers");
		ArgumentNullException.ThrowIfNull(columnIndices, "columnIndices");
		if (rowPointers.Length < maxRows + 1)
		{
			throw new ArgumentException("Row pointer array is too small.", "rowPointers");
		}
		Deallocate();
		_rowCount = maxRows;
		RowPointers = rowPointers;
		ColumnIndices = columnIndices;
		Values = new TStorage[columnIndices.Length];
		VerifySortedIndices();
	}

	public SparseMatrix(List<int>[] rows)
	{
		ArgumentNullException.ThrowIfNull(rows, "rows");
		int rowLength = rows.Length;
		int[] rowPointers = new int[rowLength + 1];
		List<int> scaledRowLength = new List<int>(rowLength * 5);
		for (int i = 0; i < rowLength; i++)
		{
			rows[i].Sort();
			scaledRowLength.AddRange(rows[i]);
			rowPointers[i + 1] = scaledRowLength.Count;
		}
		Deallocate();
		_rowCount = rowLength;
		RowPointers = rowPointers;
		ColumnIndices = scaledRowLength.ToArray();
		Values = new TStorage[scaledRowLength.Count];
		HasSortedIndices = true;
	}

	public static int GetNumberNonZeroValues(int[,] columns)
	{
		int numberNonZeroValues = 0;
		for (int i = 0; i < columns.GetLength(0); i++)
		{
			for (int j = 0; j < columns.GetLength(1); j++)
			{
				if (columns[i, j] >= 0)
				{
					numberNonZeroValues++;
				}
			}
		}
		return numberNonZeroValues;
	}

	public static bool HaveSameStructure(SparseMatrix<TStorage> first, SparseMatrix<TStorage> second)
	{
		if (first == second)
		{
			return true;
		}
		if (!HaveSameSize(first, second))
		{
			return false;
		}
		if (first.RowCount == 0)
		{
			return true;
		}
		if (first.RowPointers == second.RowPointers && first.ColumnIndices == second.ColumnIndices)
		{
			return true;
		}
		if (((ReadOnlySpan<int>)first.RowPointers.AsSpan(0, first.RowCount + 1)).SequenceEqual((ReadOnlySpan<int>)second.RowPointers.AsSpan(0, second.RowCount + 1)))
		{
			return ((ReadOnlySpan<int>)first.ColumnIndices.AsSpan(0, first.NonZeroCount)).SequenceEqual((ReadOnlySpan<int>)second.ColumnIndices.AsSpan(0, second.NonZeroCount));
		}
		return false;
	}

	public static bool HaveSameSize(SparseMatrix<TStorage> first, SparseMatrix<TStorage> second)
	{
		if (first.RowCount == second.RowCount)
		{
			return first.NonZeroCount == second.NonZeroCount;
		}
		return false;
	}

	public void Deallocate()
	{
		_rowCount = 0;
		IsUpper = false;
		RowPointers = Array.Empty<int>();
		ColumnIndices = Array.Empty<int>();
		Values = Array.Empty<TStorage>();
		ColumnPointers = Array.Empty<int>();
		DiagonalPointers = Array.Empty<int>();
		TransposePointers = Array.Empty<int>();
		HasSortedIndices = false;
		InvalidateStructureCaches();
	}

	public void Initialize(int maxRows, int maxNonZero)
	{
		_rowCount = maxRows;
		AllocateStructure(maxRows, maxNonZero);
		AllocateData(maxNonZero);
	}

	public void Initialize(SparseMatrix<TStorage> source, bool cloneData)
	{
		ArgumentNullException.ThrowIfNull(source, "source");
		CopyStructureFromSparse(source);
		Values = new TStorage[source.MaxNonZero];
		if (cloneData)
		{
			source.Values.AsSpan(0, source.NonZeroCount).CopyTo(Values);
		}
	}

	public void Initialize(SparseMatrix<TStorage> first, SparseMatrix<TStorage> second)
	{
		ArgumentNullException.ThrowIfNull(first, "first");
		ArgumentNullException.ThrowIfNull(second, "second");
		if (first.RowCount != second.RowCount)
		{
			throw new ArgumentException("Matrices must have the same number of rows.");
		}
		if (first.HasSortedIndices && second.HasSortedIndices)
		{
			InitializeSortedUnion(first, second);
			return;
		}
		int rowCount = first.RowCount;
		List<int> nonZeroCountDifference = new List<int>(first.NonZeroCount + second.NonZeroCount);
		int[] rowPointers = new int[rowCount + 1];
		for (int i = 0; i < rowCount; i++)
		{
			SortedSet<int> sortedSet = new SortedSet<int>();
			for (int j = first.RowPointers[i]; j < first.RowPointers[i + 1]; j++)
			{
				sortedSet.Add(first.ColumnIndices[j]);
			}
			for (int k = second.RowPointers[i]; k < second.RowPointers[i + 1]; k++)
			{
				sortedSet.Add(second.ColumnIndices[k]);
			}
			nonZeroCountDifference.AddRange(sortedSet);
			rowPointers[i + 1] = nonZeroCountDifference.Count;
		}
		_rowCount = rowCount;
		RowPointers = rowPointers;
		ColumnIndices = nonZeroCountDifference.ToArray();
		Values = new TStorage[nonZeroCountDifference.Count];
		ColumnPointers = Array.Empty<int>();
		DiagonalPointers = Array.Empty<int>();
		TransposePointers = Array.Empty<int>();
		HasSortedIndices = true;
		InvalidateStructureCaches();
	}

	private void InitializeSortedUnion(SparseMatrix<TStorage> first, SparseMatrix<TStorage> second)
	{
		int rowCount = first.RowCount;
		int[] rowPointers = new int[rowCount + 1];
		int count = 0;
		for (int i = 0; i < rowCount; i++)
		{
			int rowPointer = first.RowPointers[i];
			int rowPointer2 = first.RowPointers[i + 1];
			int rowPointer3 = second.RowPointers[i];
			int rowPointer4 = second.RowPointers[i + 1];
			while (rowPointer < rowPointer2 && rowPointer3 < rowPointer4)
			{
				int columnIndex = first.ColumnIndices[rowPointer];
				int columnIndex2 = second.ColumnIndices[rowPointer3];
				count++;
				if (columnIndex <= columnIndex2)
				{
					rowPointer++;
				}
				if (columnIndex2 <= columnIndex)
				{
					rowPointer3++;
				}
			}
			count = (rowPointers[i + 1] = count + (rowPointer2 - rowPointer + rowPointer4 - rowPointer3));
		}
		int[] columnIndices = new int[count];
		int ordinal = 0;
		for (int j = 0; j < rowCount; j++)
		{
			int rowPointer5 = first.RowPointers[j];
			int rowPointer6 = first.RowPointers[j + 1];
			int rowPointer7 = second.RowPointers[j];
			int rowPointer8 = second.RowPointers[j + 1];
			while (rowPointer5 < rowPointer6 && rowPointer7 < rowPointer8)
			{
				int columnIndex3 = first.ColumnIndices[rowPointer5];
				int columnIndex4 = second.ColumnIndices[rowPointer7];
				if (columnIndex3 <= columnIndex4)
				{
					columnIndices[ordinal++] = columnIndex3;
					rowPointer5++;
				}
				else
				{
					columnIndices[ordinal++] = columnIndex4;
					rowPointer7++;
				}
				if (columnIndex3 == columnIndex4)
				{
					rowPointer7++;
				}
			}
			while (rowPointer5 < rowPointer6)
			{
				columnIndices[ordinal++] = first.ColumnIndices[rowPointer5++];
			}
			while (rowPointer7 < rowPointer8)
			{
				columnIndices[ordinal++] = second.ColumnIndices[rowPointer7++];
			}
		}
		_rowCount = rowCount;
		RowPointers = rowPointers;
		ColumnIndices = columnIndices;
		Values = new TStorage[count];
		ColumnPointers = Array.Empty<int>();
		DiagonalPointers = Array.Empty<int>();
		TransposePointers = Array.Empty<int>();
		HasSortedIndices = true;
		InvalidateStructureCaches();
	}

	public void Initialize(TStorage[,] dense)
	{
		ArgumentNullException.ThrowIfNull(dense, "dense");
		int length = dense.GetLength(0);
		int length2 = dense.GetLength(1);
		EqualityComparer<TStorage> equalityComparer = EqualityComparer<TStorage>.Default;
		TStorage y = default;
		int ordinal = 0;
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < length2; j++)
			{
				if (!equalityComparer.Equals(dense[i, j], y))
				{
					ordinal++;
				}
			}
		}
		Initialize(length, ordinal);
		int rowPointers = 0;
		for (int k = 0; k < length; k++)
		{
			RowPointers[k] = rowPointers;
			for (int l = 0; l < length2; l++)
			{
				TStorage denseTerm = dense[k, l];
				if (!equalityComparer.Equals(denseTerm, y))
				{
					ColumnIndices[rowPointers] = l;
					Values[rowPointers] = denseTerm;
					rowPointers++;
				}
			}
		}
		RowPointers[length] = rowPointers;
		HasSortedIndices = true;
	}

	public void Initialize(TStorage[,] dense, int[,] columns)
	{
		ArgumentNullException.ThrowIfNull(dense, "dense");
		ArgumentNullException.ThrowIfNull(columns, "columns");
		if (dense.GetLength(0) != columns.GetLength(0) || dense.GetLength(1) != columns.GetLength(1))
		{
			throw new ArgumentException("Dense values and columns must have the same dimensions.");
		}
		int length = dense.GetLength(0);
		int length2 = dense.GetLength(1);
		EqualityComparer<TStorage> equalityComparer = EqualityComparer<TStorage>.Default;
		TStorage y = default;
		int ordinal = 0;
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < length2; j++)
			{
				if (columns[i, j] >= 0 && !equalityComparer.Equals(dense[i, j], y))
				{
					ordinal++;
				}
			}
		}
		Initialize(length, ordinal);
		int rowPointers = 0;
		for (int k = 0; k < length; k++)
		{
			RowPointers[k] = rowPointers;
			for (int l = 0; l < length2; l++)
			{
				TStorage denseTerm = dense[k, l];
				int columnIndices = columns[k, l];
				if (columnIndices >= 0 && !equalityComparer.Equals(denseTerm, y))
				{
					ColumnIndices[rowPointers] = columnIndices;
					Values[rowPointers] = denseTerm;
					rowPointers++;
				}
			}
		}
		RowPointers[length] = rowPointers;
		VerifySortedIndices();
	}

	public void StreamOut(BinaryWriter writer)
	{
		writer.Write(IsUpper);
		writer.Write(HasSortedIndices);
		writer.Write(_rowCount);
		writer.WriteBuffer(RowPointers.AsMemory());
		writer.WriteBuffer(ColumnIndices.AsMemory());
		writer.WriteBuffer(ColumnPointers.AsMemory());
		writer.WriteBuffer(DiagonalPointers.AsMemory());
		writer.WriteBuffer(Values.AsMemory());
	}

	public void StreamIn(BinaryReader reader)
	{
		IsUpper = reader.ReadBoolean();
		HasSortedIndices = reader.ReadBoolean();
		_rowCount = reader.ReadInt32();
		RowPointers = reader.ReadBuffer<int>();
		ColumnIndices = reader.ReadBuffer<int>();
		ColumnPointers = reader.ReadBuffer<int>();
		DiagonalPointers = reader.ReadBuffer<int>();
		Values = reader.ReadBuffer<TStorage>();
		TransposePointers = Array.Empty<int>();
		UpToDateColumnPointers = ColumnPointers.Length != 0;
		UpToDateDiagonalPointers = DiagonalPointers.Length != 0;
		UpToDateTransposePointers = false;
	}

	public void Clone(SparseMatrix<TStorage> source)
	{
		CloneStructureFromSparse(source);
		AllocateData(source.MaxNonZero);
	}

	public void Copy(SparseMatrix<TStorage> source)
	{
		CopyStructureFromSparse(source);
		AllocateData(source.MaxNonZero);
	}

	public void AllocateStructure(int maxRows, int maxNonZero)
	{
		RowPointers = new int[maxRows + 1];
		ColumnIndices = new int[maxNonZero];
		ColumnPointers = Array.Empty<int>();
		DiagonalPointers = Array.Empty<int>();
		TransposePointers = Array.Empty<int>();
		InvalidateStructureCaches();
	}

	public void AllocateData(int maxNonZero)
	{
		Values = new TStorage[maxNonZero];
	}

	public bool EnsureCapacity(int requiredRows, int requiredNnz, int rowCap = 0, int nnzCap = 0)
	{
		bool condition = false;
		int lengthOffset = Math.Max(0, RowPointers.Length - 1);
		bool requiredRowsTerm = lengthOffset < requiredRows;
		bool requiredRowsTerm2 = requiredRows > 0 && requiredRows * 5 < lengthOffset * 2;
		if (requiredRowsTerm | requiredRowsTerm2)
		{
			int requiredRows2 = (requiredRowsTerm ? Math.Max((lengthOffset <= 0) ? requiredRows : (lengthOffset * 2), requiredRows) : requiredRows);
			if (rowCap > 0)
			{
				requiredRows2 = Math.Max(requiredRows, Math.Min(requiredRows2, rowCap));
			}
			int[] rowPointers = new int[requiredRows2 + 1];
			Array.Copy(RowPointers, rowPointers, Math.Min(RowPointers.Length, rowPointers.Length));
			RowPointers = rowPointers;
			condition = true;
		}
		int columnIndexLength = ColumnIndices.Length;
		bool columnIndexLengthRequiredNnzTerm = columnIndexLength < requiredNnz;
		bool requiredNnzColumnIndexLengthTerm = requiredNnz > 0 && requiredNnz * 5 < columnIndexLength * 2;
		if (columnIndexLengthRequiredNnzTerm | requiredNnzColumnIndexLengthTerm)
		{
			int requiredNnz2 = (columnIndexLengthRequiredNnzTerm ? Math.Max((columnIndexLength <= 0) ? requiredNnz : (columnIndexLength * 2), requiredNnz) : requiredNnz);
			if (nnzCap > 0)
			{
				requiredNnz2 = Math.Max(requiredNnz, Math.Min(requiredNnz2, nnzCap));
			}
			int[] columnIndices = new int[requiredNnz2];
			TStorage[] values = new TStorage[requiredNnz2];
			Array.Copy(ColumnIndices, columnIndices, Math.Min(ColumnIndices.Length, requiredNnz2));
			Array.Copy(Values, values, Math.Min(Values.Length, requiredNnz2));
			ColumnIndices = columnIndices;
			Values = values;
			condition = true;
		}
		if (condition)
		{
			InvalidateStructureCaches();
		}
		return condition;
	}

	public bool EnsureValuesCapacity(int requiredNnz, int nnzCap = 0)
	{
		int ordinal = Values.Length;
		bool requiredNnzTerm = ordinal < requiredNnz;
		bool requiredNnzTerm2 = requiredNnz > 0 && requiredNnz * 5 < ordinal * 2;
		if (!requiredNnzTerm && !requiredNnzTerm2)
		{
			return false;
		}
		int requiredNnz2 = (requiredNnzTerm ? Math.Max((ordinal <= 0) ? requiredNnz : (ordinal * 2), requiredNnz) : requiredNnz);
		if (nnzCap > 0)
		{
			requiredNnz2 = Math.Max(requiredNnz, Math.Min(requiredNnz2, nnzCap));
		}
		TStorage[] values = new TStorage[requiredNnz2];
		Array.Copy(Values, values, Math.Min(Values.Length, requiredNnz2));
		Values = values;
		return true;
	}

	public void CloneStructureFromSparse(SparseMatrix<TStorage> source)
	{
		_rowCount = source.RowCount;
		RowPointers = new int[source.MaxRows + 1];
		ColumnIndices = new int[source.MaxNonZero];
		Array.Copy(source.RowPointers, RowPointers, source.RowCount + 1);
		Array.Copy(source.ColumnIndices, ColumnIndices, source.NonZeroCount);
		ColumnPointers = ((source.ColumnPointers.Length == 0) ? Array.Empty<int>() : ((int[])source.ColumnPointers.Clone()));
		DiagonalPointers = ((source.DiagonalPointers.Length == 0) ? Array.Empty<int>() : ((int[])source.DiagonalPointers.Clone()));
		TransposePointers = ((source.TransposePointers.Length == 0) ? Array.Empty<int>() : ((int[])source.TransposePointers.Clone()));
		HasSortedIndices = source.HasSortedIndices;
		UpToDateColumnPointers = source.UpToDateColumnPointers;
		UpToDateDiagonalPointers = source.UpToDateDiagonalPointers;
		UpToDateTransposePointers = source.UpToDateTransposePointers;
	}

	public void CopyStructureFromSparse(SparseMatrix<TStorage> source)
	{
		_rowCount = source.RowCount;
		RowPointers = source.RowPointers;
		ColumnIndices = source.ColumnIndices;
		ColumnPointers = source.ColumnPointers;
		DiagonalPointers = source.DiagonalPointers;
		TransposePointers = source.TransposePointers;
		HasSortedIndices = source.HasSortedIndices;
		UpToDateColumnPointers = source.UpToDateColumnPointers;
		UpToDateDiagonalPointers = source.UpToDateDiagonalPointers;
		UpToDateTransposePointers = source.UpToDateTransposePointers;
	}

	public void SetStructure(int[] rowPointers, int[] columnIndices)
	{
		ArgumentNullException.ThrowIfNull(rowPointers, "rowPointers");
		ArgumentNullException.ThrowIfNull(columnIndices, "columnIndices");
		int rowCount = rowPointers.Length - 1;
		if (MaxRows < rowCount || MaxNonZero < columnIndices.Length)
		{
			AllocateStructure(rowCount, columnIndices.Length);
			if (Values.Length < columnIndices.Length)
			{
				AllocateData(columnIndices.Length);
			}
		}
		_rowCount = rowCount;
		Array.Copy(rowPointers, RowPointers, rowPointers.Length);
		Array.Copy(columnIndices, ColumnIndices, columnIndices.Length);
		VerifySortedIndices();
		InvalidateStructureCaches();
	}

	public void CloneDataFromSparse(SparseMatrix<TStorage> source)
	{
		if (Values.Length != source.MaxNonZero)
		{
			AllocateData(source.MaxNonZero);
		}
		source.Values.AsSpan(0, source.NonZeroCount).CopyTo(Values);
	}

	public void ComputeDiagonalPointers()
	{
		if (UpToDateDiagonalPointers)
		{
			return;
		}
		if (DiagonalPointers.Length < RowCount)
		{
			DiagonalPointers = new int[MaxRows];
		}
		int rowCount = RowCount;
		DiagonalPointers.AsSpan(0, rowCount).Fill(-1);
		for (int i = 0; i < rowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				if (ColumnIndices[j] == i)
				{
					DiagonalPointers[i] = j;
					break;
				}
			}
		}
		UpToDateDiagonalPointers = true;
	}

	public void ComputeColumnPointers()
	{
		if (!UpToDateColumnPointers)
		{
			if (ColumnPointers.Length < MaxNonZero)
			{
				ColumnPointers = new int[MaxNonZero];
			}
			int nonZeroCount = NonZeroCount;
			for (int i = 0; i < nonZeroCount; i++)
			{
				ColumnPointers[i] = i;
			}
			UpToDateColumnPointers = true;
		}
	}

	public void ComputeTransposePointers()
	{
		if (UpToDateTransposePointers)
		{
			return;
		}
		int nonZeroCount = NonZeroCount;
		if (TransposePointers.Length < nonZeroCount)
		{
			TransposePointers = new int[MaxNonZero];
		}
		TransposePointers.AsSpan(0, nonZeroCount).Fill(-1);
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				int columnIndex = ColumnIndices[j];
				if (i == columnIndex)
				{
					TransposePointers[j] = j;
				}
				else if (columnIndex >= i)
				{
					int transposeIndex = GetTransposeIndex(columnIndex, i);
					TransposePointers[j] = transposeIndex;
					if (transposeIndex >= 0)
					{
						TransposePointers[transposeIndex] = j;
					}
				}
			}
		}
		UpToDateTransposePointers = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetTransposeIndex(int row, int column)
	{
		int rowPointerStart = RowPointers[row];
		int rowPointerEnd = RowPointers[row + 1];
		if (rowPointerEnd - rowPointerStart > 8)
		{
			return GetIndexRowColumn(row, column);
		}
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			int columnIndex = ColumnIndices[i];
			if (columnIndex == column)
			{
				return i;
			}
			if (HasSortedIndices && columnIndex > column)
			{
				break;
			}
		}
		return -1;
	}

	public bool VerifySortedIndices()
	{
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1] - 1; j++)
			{
				if (ColumnIndices[j] >= ColumnIndices[j + 1])
				{
					HasSortedIndices = false;
					return false;
				}
			}
		}
		HasSortedIndices = true;
		return true;
	}

	public void Zero()
	{
		Array.Clear(Values, 0, NonZeroCount);
	}

	public void Pack()
	{
		int nonZeroCount = NonZeroCount;
		if (ColumnIndices.Length != nonZeroCount)
		{
			Array.Resize(ref ColumnIndices, nonZeroCount);
		}
		if (Values.Length != nonZeroCount)
		{
			Array.Resize(ref Values, nonZeroCount);
		}
		if (ColumnPointers.Length > nonZeroCount)
		{
			Array.Resize(ref ColumnPointers, nonZeroCount);
		}
		if (TransposePointers.Length > nonZeroCount)
		{
			Array.Resize(ref TransposePointers, nonZeroCount);
		}
	}

	public void ExtractSubmatrix(List<int> subToFull, int[] fullToSub, SparseMatrix<TStorage> output, int[] positions = null)
	{
		ArgumentNullException.ThrowIfNull(subToFull, "subToFull");
		ArgumentNullException.ThrowIfNull(fullToSub, "fullToSub");
		ArgumentNullException.ThrowIfNull(output, "output");
		int count = subToFull.Count;
		int ordinal = 0;
		for (int i = 0; i < count; i++)
		{
			int subToFull2 = subToFull[i];
			for (int j = RowPointers[subToFull2]; j < RowPointers[subToFull2 + 1]; j++)
			{
				if (fullToSub[ColumnIndices[j]] >= 0)
				{
					ordinal++;
				}
			}
		}
		if (output.MaxRows < count || output.MaxNonZero < ordinal)
		{
			throw new ArgumentException("Output capacity is too small for the resulting submatrix.", "output");
		}
		output._rowCount = count;
		output.RowPointers[0] = 0;
		int rowPointers = 0;
		for (int k = 0; k < count; k++)
		{
			int subToFull3 = subToFull[k];
			for (int l = RowPointers[subToFull3]; l < RowPointers[subToFull3 + 1]; l++)
			{
				int fullToSub2 = fullToSub[ColumnIndices[l]];
				if (fullToSub2 >= 0)
				{
					output.ColumnIndices[rowPointers] = fullToSub2;
					output.Values[rowPointers] = Values[l];
					if (positions != null)
					{
						positions[rowPointers] = l;
					}
					rowPointers++;
				}
			}
			output.RowPointers[k + 1] = rowPointers;
		}
		output.HasSortedIndices = HasSortedIndices;
		output.InvalidateStructureCaches();
	}

	public void ExtractSubmatrixUpper(int[] fullToSub, int rowCount, SparseMatrix<TStorage> output, int[] positions = null)
	{
		ArgumentNullException.ThrowIfNull(fullToSub, "fullToSub");
		ArgumentNullException.ThrowIfNull(output, "output");
		int ordinal = 0;
		for (int i = 0; i < RowCount; i++)
		{
			int fullToSub2 = fullToSub[i];
			if (fullToSub2 < 0)
			{
				continue;
			}
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				if (fullToSub[ColumnIndices[j]] >= fullToSub2)
				{
					ordinal++;
				}
			}
		}
		if (output.MaxRows < rowCount || output.MaxNonZero < ordinal)
		{
			throw new ArgumentException("Output capacity is too small for the resulting submatrix.", "output");
		}
		output.IsUpper = true;
		output._rowCount = rowCount;
		output.RowPointers.AsSpan(0, rowCount + 1).Clear();
		int rowPointers = 0;
		for (int k = 0; k < RowCount; k++)
		{
			int fullToSub3 = fullToSub[k];
			if (fullToSub3 < 0)
			{
				continue;
			}
			for (int l = RowPointers[k]; l < RowPointers[k + 1]; l++)
			{
				int fullToSub4 = fullToSub[ColumnIndices[l]];
				if (fullToSub4 >= fullToSub3)
				{
					output.ColumnIndices[rowPointers] = fullToSub4;
					output.Values[rowPointers] = Values[l];
					if (positions != null)
					{
						positions[rowPointers] = l;
					}
					rowPointers++;
				}
			}
			output.RowPointers[fullToSub3 + 1] = rowPointers;
		}
		output.HasSortedIndices = HasSortedIndices;
		output.InvalidateStructureCaches();
	}

	public void MapData(SparseMatrix<TStorage> source, Span<int> positionMap)
	{
		if (positionMap.Length < NonZeroCount)
		{
			throw new ArgumentException("Position map is too small.", "positionMap");
		}
		for (int i = 0; i < NonZeroCount; i++)
		{
			Values[i] = source.Values[positionMap[i]];
		}
	}

	public void ExtractUpperTriangular(SparseMatrix<TStorage> output, int[] positions = null)
	{
		ArgumentNullException.ThrowIfNull(output, "output");
		int ordinal = 0;
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				if (ColumnIndices[j] >= i && ColumnIndices[j] < RowCount)
				{
					ordinal++;
				}
			}
		}
		if (output.MaxRows < RowCount || output.MaxNonZero < ordinal)
		{
			throw new ArgumentException("Output capacity is too small for the upper triangle.", "output");
		}
		output.IsUpper = true;
		output._rowCount = RowCount;
		int rowPointers = 0;
		for (int k = 0; k < RowCount; k++)
		{
			output.RowPointers[k] = rowPointers;
			for (int l = RowPointers[k]; l < RowPointers[k + 1]; l++)
			{
				int columnIndex = ColumnIndices[l];
				if (columnIndex >= k && columnIndex < RowCount)
				{
					output.ColumnIndices[rowPointers] = columnIndex;
					output.Values[rowPointers] = Values[l];
					if (positions != null)
					{
						positions[rowPointers] = l;
					}
					rowPointers++;
				}
			}
		}
		output.RowPointers[RowCount] = rowPointers;
		output.HasSortedIndices = HasSortedIndices;
		output.InvalidateStructureCaches();
	}

	public bool IsSameSize(SparseMatrix<TStorage> other)
	{
		return HaveSameSize(this, other);
	}

	public bool FitsIn(SparseMatrix<TStorage> other)
	{
		if (RowCount <= other.MaxRows)
		{
			return NonZeroCount <= other.MaxNonZero;
		}
		return false;
	}

	public void PermuteRows(SparseMatrix<TStorage> output, int[] inversePermutation)
	{
		int count = (output._rowCount = RowCount);
		output.RowPointers.AsSpan(0, count + 1).Clear();
		for (int i = 0; i < count; i++)
		{
			int inversePermutation2 = inversePermutation[i];
			output.RowPointers[inversePermutation2 + 1] = RowPointers[i + 1] - RowPointers[i];
		}
		for (int j = 0; j < count; j++)
		{
			output.RowPointers[j + 1] += output.RowPointers[j];
		}
		for (int k = 0; k < count; k++)
		{
			int rowPointer = output.RowPointers[inversePermutation[k]];
			int rowPointer2 = RowPointers[k];
			while (rowPointer2 < RowPointers[k + 1])
			{
				output.ColumnIndices[rowPointer] = ColumnIndices[rowPointer2];
				output.Values[rowPointer] = Values[rowPointer2];
				rowPointer2++;
				rowPointer++;
			}
		}
		output.HasSortedIndices = HasSortedIndices;
		output.InvalidateStructureCaches();
	}

	public void PermuteColumns(SparseMatrix<TStorage> output, int[] inversePermutation)
	{
		output._rowCount = RowCount;
		Array.Copy(RowPointers, output.RowPointers, RowCount + 1);
		for (int i = 0; i < NonZeroCount; i++)
		{
			output.ColumnIndices[i] = inversePermutation[ColumnIndices[i]];
			output.Values[i] = Values[i];
		}
		output.HasSortedIndices = false;
		if (HasSortedIndices)
		{
			output.Sort();
		}
		output.InvalidateStructureCaches();
	}

	public void PermuteSymmetric(SparseMatrix<TStorage> output, int[] inversePermutation, int[] positions = null)
	{
		PermuteRowsColumns(output, inversePermutation, inversePermutation, positions);
	}

	public void PermuteRowsColumns(SparseMatrix<TStorage> output, int[] rowInversePermutation, int[] columnInversePermutation, int[] positions = null)
	{
		int count = (output._rowCount = RowCount);
		output.RowPointers.AsSpan(0, count + 1).Clear();
		for (int i = 0; i < count; i++)
		{
			int rowInversePermutation2 = rowInversePermutation[i];
			output.RowPointers[rowInversePermutation2 + 1] = RowPointers[i + 1] - RowPointers[i];
		}
		for (int j = 0; j < count; j++)
		{
			output.RowPointers[j + 1] += output.RowPointers[j];
		}
		for (int k = 0; k < count; k++)
		{
			int rowPointer = output.RowPointers[rowInversePermutation[k]];
			int rowPointer2 = RowPointers[k];
			while (rowPointer2 < RowPointers[k + 1])
			{
				output.ColumnIndices[rowPointer] = columnInversePermutation[ColumnIndices[rowPointer2]];
				output.Values[rowPointer] = Values[rowPointer2];
				if (positions != null)
				{
					positions[rowPointer] = rowPointer2;
				}
				rowPointer2++;
				rowPointer++;
			}
		}
		output.HasSortedIndices = false;
		if (HasSortedIndices)
		{
			if (positions == null)
			{
				output.Sort();
			}
			else
			{
				output.Sort(positions);
			}
		}
		output.InvalidateStructureCaches();
	}

	public void PermuteDataRowsColumns(SparseMatrix<TStorage> output, int[] rowInversePermutation, int[] columnInversePermutation)
	{
		for (int i = 0; i < RowCount; i++)
		{
			int rowPointer = output.RowPointers[rowInversePermutation[i]];
			int rowPointer2 = RowPointers[i];
			while (rowPointer2 < RowPointers[i + 1])
			{
				output.Values[rowPointer] = Values[rowPointer2];
				rowPointer2++;
				rowPointer++;
			}
		}
	}

	public void PermuteRows(int[] inversePermutation)
	{
		SparseMatrix<TStorage> sparseMatrix = new SparseMatrix<TStorage>(MaxRows, MaxNonZero);
		sparseMatrix._rowCount = RowCount;
		PermuteRows(sparseMatrix, inversePermutation);
		AdoptOwnedStorage(sparseMatrix);
	}

	public void PermuteColumns(int[] inversePermutation)
	{
		for (int i = 0; i < NonZeroCount; i++)
		{
			ColumnIndices[i] = inversePermutation[ColumnIndices[i]];
		}
		HasSortedIndices = false;
		Sort();
	}

	public void PermuteSymmetric(int[] inversePermutation)
	{
		PermuteRowsColumns(inversePermutation, inversePermutation);
	}

	public void PermuteRowsColumns(int[] rowInversePermutation, int[] columnInversePermutation)
	{
		SparseMatrix<TStorage> sparseMatrix = new SparseMatrix<TStorage>(MaxRows, MaxNonZero);
		sparseMatrix._rowCount = RowCount;
		PermuteRowsColumns(sparseMatrix, rowInversePermutation, columnInversePermutation);
		AdoptOwnedStorage(sparseMatrix);
	}

	public void Transpose()
	{
		int rowCount = RowCount;
		int nonZeroCount = NonZeroCount;
		int[] intValues = new int[rowCount];
		for (int i = 0; i < nonZeroCount; i++)
		{
			intValues[ColumnIndices[i]]++;
		}
		int[] rowPointers = new int[rowCount + 1];
		for (int j = 0; j < rowCount; j++)
		{
			rowPointers[j + 1] = rowPointers[j] + intValues[j];
		}
		for (int k = 0; k < rowCount; k++)
		{
			intValues[k] = rowPointers[k];
		}
		int[] columnIndices = new int[MaxNonZero];
		TStorage[] values = new TStorage[Values.Length];
		for (int l = 0; l < rowCount; l++)
		{
			for (int m = RowPointers[l]; m < RowPointers[l + 1]; m++)
			{
				int columnIndicesTerm = intValues[ColumnIndices[m]]++;
				columnIndices[columnIndicesTerm] = l;
				values[columnIndicesTerm] = Values[m];
			}
		}
		RowPointers = rowPointers;
		ColumnIndices = columnIndices;
		Values = values;
		HasSortedIndices = true;
		InvalidateStructureCaches();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetDataRowColumn(int row, int column, TStorage dataRowColumn)
	{
		int indexRowColumn = GetIndexRowColumn(row, column);
		if (indexRowColumn < 0)
		{
			throw new ArgumentException("The requested sparse entry does not exist.");
		}
		Values[indexRowColumn] = dataRowColumn;
	}

	public void SetDataRow(int row, int[] columns, TStorage[] values)
	{
		for (int i = 0; i < columns.Length; i++)
		{
			SetDataRowColumn(row, columns[i], values[i]);
		}
	}

	public void SetDataRowColumnIndex(int row, int columnIndex, TStorage dataRowColumnIndex)
	{
		Values[GetIndexRowColumnIndex(row, columnIndex)] = dataRowColumnIndex;
	}

	public void BuildRow(int row, int[] columns, TStorage[] values, int length = 0)
	{
		if (length == 0)
		{
			length = columns.Length;
		}
		int rowPointer = RowPointers[row];
		for (int i = 0; i < length; i++)
		{
			if (columns[i] >= 0)
			{
				if (rowPointer >= MaxNonZero)
				{
					throw new ArgumentException("Matrix capacity exceeded.");
				}
				ColumnIndices[rowPointer] = columns[i];
				Values[rowPointer] = values[i];
				rowPointer++;
			}
		}
		RowPointers[row + 1] = rowPointer;
		HasSortedIndices = false;
		InvalidateStructureCaches();
	}

	public void BuildRow(int row, int[] columns, int length)
	{
		int rowPointer = RowPointers[row];
		for (int i = 0; i < length; i++)
		{
			if (columns[i] >= 0)
			{
				if (rowPointer >= MaxNonZero)
				{
					throw new ArgumentException("Matrix capacity exceeded.");
				}
				ColumnIndices[rowPointer++] = columns[i];
			}
		}
		RowPointers[row + 1] = rowPointer;
		HasSortedIndices = false;
		InvalidateStructureCaches();
	}

	public void Sort(bool skipColumnPointers = false)
	{
		SortCore(null, skipColumnPointers);
	}

	public void Sort(int[] positions, bool skipColumnPointers = false)
	{
		ArgumentNullException.ThrowIfNull(positions, "positions");
		SortCore(positions, skipColumnPointers);
	}

	public void SetDataDiagonal(int row, TStorage dataDiagonal)
	{
		Values[DiagonalPointers[row]] = dataDiagonal;
	}

	public void SetData(TStorage[] values)
	{
		Array.Copy(values, Values, Math.Min(values.Length, NonZeroCount));
	}

	public void SetDataIndex(int idx, TStorage dataIndex)
	{
		Values[idx] = dataIndex;
	}

	public void SetDataDiagonalRow(int row, TStorage dataDiagonalRow)
	{
		Values[DiagonalPointers[row]] = dataDiagonalRow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TStorage GetDataRowColumn(int row, int column)
	{
		int indexRowColumn = GetIndexRowColumn(row, column);
		if (indexRowColumn < 0)
		{
			throw new ArgumentException("The requested sparse entry does not exist.");
		}
		return Values[indexRowColumn];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TStorage GetDataRowColumn(int row, int column, TStorage missingValue)
	{
		int indexRowColumn = GetIndexRowColumn(row, column);
		if (indexRowColumn >= 0)
		{
			return Values[indexRowColumn];
		}
		return missingValue;
	}

	public TStorage GetDataRowColumnIndex(int row, int columnIndex)
	{
		return Values[GetIndexRowColumnIndex(row, columnIndex)];
	}

	public TStorage GetDataDiagonalRow(int row)
	{
		return Values[DiagonalPointers[row]];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetIndexRowColumn(int row, int column)
	{
		int rowPointerStart = RowPointers[row];
		int rowPointerEnd = RowPointers[row + 1];
		if (!HasSortedIndices)
		{
			for (int i = rowPointerStart; i < rowPointerEnd; i++)
			{
				if (ColumnIndices[i] == column)
				{
					return i;
				}
			}
			return -1;
		}
		while (rowPointerStart < rowPointerEnd)
		{
			int rowPointerEnd2 = rowPointerStart + (rowPointerEnd - rowPointerStart >> 1);
			int columnIndex = ColumnIndices[rowPointerEnd2];
			if (columnIndex == column)
			{
				return rowPointerEnd2;
			}
			if (columnIndex < column)
			{
				rowPointerStart = rowPointerEnd2 + 1;
			}
			else
			{
				rowPointerEnd = rowPointerEnd2;
			}
		}
		return -1;
	}

	public int GetIndexRowColumnIndex(int row, int columnIndex)
	{
		return ColumnPointers[RowPointers[row] + columnIndex];
	}

	public bool CheckIndexRowColumn(int row, int column)
	{
		return GetIndexRowColumn(row, column) >= 0;
	}

	public Span<int> GetColumnIndices(int row)
	{
		int rowPointer = RowPointers[row];
		return ColumnIndices.AsSpan(rowPointer, RowPointers[row + 1] - rowPointer);
	}

	public Span<TStorage> GetRowDataAsSpan(int row)
	{
		int rowPointer = RowPointers[row];
		return Values.AsSpan(rowPointer, RowPointers[row + 1] - rowPointer);
	}

	public Span<TStorage> GetDataAsSpan()
	{
		return Values.AsSpan(0, NonZeroCount);
	}

	public void ToDense(TStorage[,] dense)
	{
		int rowCount = RowCount;
		if (dense.GetLength(0) < rowCount || dense.GetLength(1) < rowCount)
		{
			throw new ArgumentException("Dense destination is too small.", "dense");
		}
		Array.Clear(dense);
		for (int i = 0; i < rowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				dense[i, ColumnIndices[j]] = Values[j];
			}
		}
	}

	public void ToDense(Span<TStorage> dense)
	{
		int rowCount = RowCount;
		dense = dense.Slice(0, rowCount * rowCount);
		dense.Clear();
		for (int i = 0; i < rowCount; i++)
		{
			int rowCountTerm = i * rowCount;
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				dense[rowCountTerm + ColumnIndices[j]] = Values[j];
			}
		}
	}

	public int Bandwidth()
	{
		int columnIndexTerm = 0;
		int columnIndexTerm2 = 0;
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				int columnIndex = ColumnIndices[j];
				columnIndexTerm = Math.Max(columnIndexTerm, i - columnIndex);
				columnIndexTerm2 = Math.Max(columnIndexTerm2, columnIndex - i);
			}
		}
		return columnIndexTerm + 1 + columnIndexTerm2;
	}

	public bool IsStructureSymmetric()
	{
		for (int i = 0; i < RowCount; i++)
		{
			for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
			{
				if (GetIndexRowColumn(ColumnIndices[j], i) < 0)
				{
					return false;
				}
			}
		}
		return true;
	}

	private void SortCore(int[] positions, bool skipColumnPointers)
	{
		bool notSkipColumnPointers = !skipColumnPointers;
		if (HasSortedIndices)
		{
			if (notSkipColumnPointers)
			{
				ComputeColumnPointers();
				return;
			}
			ColumnPointers = Array.Empty<int>();
			UpToDateColumnPointers = false;
			return;
		}
		int[] columnPointers = (notSkipColumnPointers ? new int[MaxNonZero] : Array.Empty<int>());
		int ordinal = 0;
		for (int i = 0; i < RowCount; i++)
		{
			int rowPointer = RowPointers[i];
			int rowPointersRowPointerTerm = RowPointers[i + 1] - rowPointer;
			if (rowPointersRowPointerTerm > 5)
			{
				ordinal = Math.Max(ordinal, rowPointersRowPointerTerm);
			}
		}
		int[] rent = ((ordinal == 0) ? null : ArrayPool<int>.Shared.Rent(ordinal));
		int[] rentArray = ((ordinal == 0) ? null : ArrayPool<int>.Shared.Rent(ordinal));
		TStorage[] rent2 = ((ordinal == 0) ? null : ArrayPool<TStorage>.Shared.Rent(ordinal));
		int[] rent3 = ((positions == null || ordinal == 0) ? null : ArrayPool<int>.Shared.Rent(ordinal));
		Span<int> intSpan = stackalloc int[5];
		try
		{
			for (int j = 0; j < RowCount; j++)
			{
				int rowPointer2 = RowPointers[j];
				int count = RowPointers[j + 1] - rowPointer2;
				if (count <= 1)
				{
					if (notSkipColumnPointers && count == 1)
					{
						columnPointers[rowPointer2] = rowPointer2;
					}
					continue;
				}
				if (count <= 5)
				{
					Span<int> order = intSpan.Slice(0, count);
					for (int k = 0; k < count; k++)
					{
						order[k] = k;
					}
					InsertionSortRow(rowPointer2, count, positions, order);
					if (notSkipColumnPointers)
					{
						for (int l = 0; l < count; l++)
						{
							columnPointers[rowPointer2 + order[l]] = rowPointer2 + l;
						}
					}
					continue;
				}
				for (int m = 0; m < count; m++)
				{
					rent[m] = m;
					rentArray[m] = ColumnIndices[rowPointer2 + m];
					rent2[m] = Values[rowPointer2 + m];
					if (rent3 != null)
					{
						rent3[m] = positions[rowPointer2 + m];
					}
				}
				Array.Sort(rentArray, rent, 0, count);
				for (int n = 0; n < count; n++)
				{
					int rent4 = rent[n];
					ColumnIndices[rowPointer2 + n] = rentArray[n];
					Values[rowPointer2 + n] = rent2[rent4];
					if (positions != null)
					{
						positions[rowPointer2 + n] = rent3[rent4];
					}
					if (notSkipColumnPointers)
					{
						columnPointers[rowPointer2 + rent4] = rowPointer2 + n;
					}
				}
			}
		}
		finally
		{
			if (rent != null)
			{
				ArrayPool<int>.Shared.Return(rent);
			}
			if (rentArray != null)
			{
				ArrayPool<int>.Shared.Return(rentArray);
			}
			if (rent2 != null)
			{
				ArrayPool<TStorage>.Shared.Return(rent2);
			}
			if (rent3 != null)
			{
				ArrayPool<int>.Shared.Return(rent3);
			}
		}
		ColumnPointers = columnPointers;
		UpToDateColumnPointers = notSkipColumnPointers;
		HasSortedIndices = true;
		UpToDateDiagonalPointers = false;
		UpToDateTransposePointers = false;
	}

	private void InsertionSortRow(int start, int length, int[] positions, Span<int> order)
	{
		for (int i = 1; i < length; i++)
		{
			int columnIndex = ColumnIndices[start + i];
			TStorage startTerm = Values[start + i];
			int order2 = order[i];
			int position = ((positions != null) ? positions[start + i] : 0);
			int ordinal = i;
			while (ordinal > 0 && ColumnIndices[start + ordinal - 1] > columnIndex)
			{
				ColumnIndices[start + ordinal] = ColumnIndices[start + ordinal - 1];
				Values[start + ordinal] = Values[start + ordinal - 1];
				order[ordinal] = order[ordinal - 1];
				if (positions != null)
				{
					positions[start + ordinal] = positions[start + ordinal - 1];
				}
				ordinal--;
			}
			ColumnIndices[start + ordinal] = columnIndex;
			Values[start + ordinal] = startTerm;
			order[ordinal] = order2;
			if (positions != null)
			{
				positions[start + ordinal] = position;
			}
		}
	}

	private void AdoptOwnedStorage(SparseMatrix<TStorage> source)
	{
		_rowCount = source._rowCount;
		RowPointers = source.RowPointers;
		ColumnIndices = source.ColumnIndices;
		Values = source.Values;
		ColumnPointers = source.ColumnPointers;
		DiagonalPointers = source.DiagonalPointers;
		TransposePointers = source.TransposePointers;
		HasSortedIndices = source.HasSortedIndices;
		UpToDateColumnPointers = source.UpToDateColumnPointers;
		UpToDateDiagonalPointers = source.UpToDateDiagonalPointers;
		UpToDateTransposePointers = source.UpToDateTransposePointers;
	}

	private void InvalidateStructureCaches()
	{
		UpToDateColumnPointers = false;
		UpToDateDiagonalPointers = false;
		UpToDateTransposePointers = false;
	}

	public static SparseMatrix<T> FromDense<T>(T[,] dense, T tolerance) where T : unmanaged, INumber<T>
	{
		ArgumentNullException.ThrowIfNull(dense, "dense");
		int length = dense.GetLength(0);
		int length2 = dense.GetLength(1);
		int numberNonZeroValues = GetNumberNonZeroValues(dense, tolerance);
		SparseMatrix<T> sparseMatrix = new SparseMatrix<T>(length, numberNonZeroValues);
		int rowPointers = 0;
		for (int i = 0; i < length; i++)
		{
			sparseMatrix.RowPointers[i] = rowPointers;
			for (int j = 0; j < length2; j++)
			{
				T denseTerm = dense[i, j];
				if (T.Abs(denseTerm) > tolerance)
				{
					sparseMatrix.ColumnIndices[rowPointers] = j;
					sparseMatrix.Values[rowPointers] = denseTerm;
					rowPointers++;
				}
			}
		}
		sparseMatrix.RowPointers[length] = rowPointers;
		sparseMatrix.HasSortedIndices = true;
		return sparseMatrix;
	}

	public static SparseMatrix<T> FromDense<T>(T[,] dense, int[,] columns, T tolerance) where T : unmanaged, INumber<T>
	{
		ArgumentNullException.ThrowIfNull(dense, "dense");
		ArgumentNullException.ThrowIfNull(columns, "columns");
		if (dense.GetLength(0) != columns.GetLength(0) || dense.GetLength(1) != columns.GetLength(1))
		{
			throw new ArgumentException("Dense values and columns must have the same dimensions.");
		}
		int length = dense.GetLength(0);
		int length2 = dense.GetLength(1);
		int ordinal = 0;
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < length2; j++)
			{
				if (columns[i, j] >= 0 && T.Abs(dense[i, j]) > tolerance)
				{
					ordinal++;
				}
			}
		}
		SparseMatrix<T> sparseMatrix = new SparseMatrix<T>(length, ordinal);
		int rowPointers = 0;
		for (int k = 0; k < length; k++)
		{
			sparseMatrix.RowPointers[k] = rowPointers;
			for (int l = 0; l < length2; l++)
			{
				T denseTerm = dense[k, l];
				int columnIndices = columns[k, l];
				if (columnIndices >= 0 && T.Abs(denseTerm) > tolerance)
				{
					sparseMatrix.ColumnIndices[rowPointers] = columnIndices;
					sparseMatrix.Values[rowPointers] = denseTerm;
					rowPointers++;
				}
			}
		}
		sparseMatrix.RowPointers[length] = rowPointers;
		sparseMatrix.VerifySortedIndices();
		return sparseMatrix;
	}

	public static int GetNumberNonZeroValues<T>(T[,] dense, T tolerance) where T : unmanaged, INumber<T>
	{
		int numberNonZeroValues = 0;
		for (int i = 0; i < dense.GetLength(0); i++)
		{
			for (int j = 0; j < dense.GetLength(1); j++)
			{
				if (T.Abs(dense[i, j]) > tolerance)
				{
					numberNonZeroValues++;
				}
			}
		}
		return numberNonZeroValues;
	}

	public static void CopyStructureFromDense<T>(SparseMatrix<T> matrix, T[,] dense, T tolerance) where T : unmanaged, INumber<T>
	{
		int rowPointers = 0;
		matrix.RowPointers[0] = rowPointers;
		for (int i = 0; i < dense.GetLength(0); i++)
		{
			for (int j = 0; j < dense.GetLength(1); j++)
			{
				if (T.Abs(dense[i, j]) > tolerance)
				{
					matrix.ColumnIndices[rowPointers++] = j;
				}
			}
			matrix.RowPointers[i + 1] = rowPointers;
		}
		matrix.HasSortedIndices = true;
		matrix.UpToDateColumnPointers = false;
		matrix.UpToDateDiagonalPointers = false;
		matrix.UpToDateTransposePointers = false;
	}

	public static void CopyStructureFromDense<T>(SparseMatrix<T> matrix, T[,] dense, int[,] columns, T tolerance) where T : unmanaged, INumber<T>
	{
		if (dense.GetLength(0) != columns.GetLength(0) || dense.GetLength(1) != columns.GetLength(1))
		{
			throw new ArgumentException("Dense values and columns must have the same dimensions.");
		}
		int rowPointers = 0;
		matrix.RowPointers[0] = rowPointers;
		for (int i = 0; i < dense.GetLength(0); i++)
		{
			for (int j = 0; j < dense.GetLength(1); j++)
			{
				if (columns[i, j] >= 0 && T.Abs(dense[i, j]) > tolerance)
				{
					matrix.ColumnIndices[rowPointers++] = j;
				}
			}
			matrix.RowPointers[i + 1] = rowPointers;
		}
		matrix.HasSortedIndices = true;
		matrix.UpToDateColumnPointers = false;
		matrix.UpToDateDiagonalPointers = false;
		matrix.UpToDateTransposePointers = false;
	}

	public static void AddMatrices<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		if (SparseMatrix<T>.HaveSameStructure(first, second))
		{
			AddMatricesWithSameStructure(first, second, output);
		}
		else
		{
			AddMatricesWithDifferentStructure(first, second, output);
		}
	}

	public static void AddMatrices<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ArgumentNullException.ThrowIfNull(parallelOptions, "parallelOptions");
		if (SparseMatrix<T>.HaveSameStructure(first, second))
		{
			AddMatricesWithSameStructure(first, second, output, parallelOptions);
		}
		else
		{
			AddMatricesWithDifferentStructure(first, second, output, parallelOptions);
		}
	}

	public static void AddMatricesWithSameStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		EnsureOutputStructure(first, output);
		int nonZeroCount = first.NonZeroCount;
		AddMatricesWithSameStructureStartStop(first, second, output, 0, nonZeroCount - 1);
	}

	public static void AddMatricesWithSameStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		EnsureOutputStructure(first, output);
		int nonZeroCount = first.NonZeroCount;
		if (!ShouldRunParallel(parallelOptions, nonZeroCount, 10000))
		{
			AddMatricesWithSameStructureStartStop(first, second, output, 0, nonZeroCount - 1);
		}
		else
		{
			AddMatricesWithSameStructureParallel(first, second, output, parallelOptions);
		}
	}

	public static void AddMatricesWithDifferentStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		EnsureUnionStructure(first, second, output);
		AddMatricesWithDifferentStructureStartStop(first, second, output, 0, output.RowCount - 1);
	}

	public static void AddMatricesWithDifferentStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		EnsureUnionStructure(first, second, output);
		long workCount = (long)first.NonZeroCount + (long)second.NonZeroCount + output.NonZeroCount;
		if (!ShouldRunParallel(parallelOptions, workCount, 20000))
		{
			AddMatricesWithDifferentStructureStartStop(first, second, output, 0, output.RowCount - 1);
			return;
		}
		int rowsPerChunk = GetRowsPerChunk(output, 10000);
		AddMatricesWithDifferentStructureParallel(first, second, output, parallelOptions, rowsPerChunk);
	}

	public static void SubtractMatrices<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		if (SparseMatrix<T>.HaveSameStructure(first, second))
		{
			SubtractMatricesWithSameStructure(first, second, output);
		}
		else
		{
			SubtractMatricesWithDifferentStructure(first, second, output);
		}
	}

	public static void SubtractMatrices<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ArgumentNullException.ThrowIfNull(parallelOptions, "parallelOptions");
		if (SparseMatrix<T>.HaveSameStructure(first, second))
		{
			SubtractMatricesWithSameStructure(first, second, output, parallelOptions);
		}
		else
		{
			SubtractMatricesWithDifferentStructure(first, second, output, parallelOptions);
		}
	}

	public static void SubtractMatricesWithSameStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		EnsureOutputStructure(first, output);
		int nonZeroCount = first.NonZeroCount;
		SubtractMatricesWithSameStructureStartStop(first, second, output, 0, nonZeroCount - 1);
	}

	public static void SubtractMatricesWithSameStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		EnsureOutputStructure(first, output);
		int nonZeroCount = first.NonZeroCount;
		if (!ShouldRunParallel(parallelOptions, nonZeroCount, 10000))
		{
			SubtractMatricesWithSameStructureStartStop(first, second, output, 0, nonZeroCount - 1);
		}
		else
		{
			SubtractMatricesWithSameStructureParallel(first, second, output, parallelOptions);
		}
	}

	public static void SubtractMatricesWithDifferentStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		EnsureUnionStructure(first, second, output);
		SubtractMatricesWithDifferentStructureStartStop(first, second, output, 0, output.RowCount - 1);
	}

	public static void SubtractMatricesWithDifferentStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		EnsureUnionStructure(first, second, output);
		long workCount = (long)first.NonZeroCount + (long)second.NonZeroCount + output.NonZeroCount;
		if (!ShouldRunParallel(parallelOptions, workCount, 20000))
		{
			SubtractMatricesWithDifferentStructureStartStop(first, second, output, 0, output.RowCount - 1);
			return;
		}
		int rowsPerChunk = GetRowsPerChunk(output, 10000);
		SubtractMatricesWithDifferentStructureParallel(first, second, output, parallelOptions, rowsPerChunk);
	}

	private static void AddMatricesWithSameStructureStartStop<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		T[] values = first.Values;
		T[] values2 = second.Values;
		T[] values3 = output.Values;
		for (int i = start; i <= stop; i++)
		{
			values3[i] = values[i] + values2[i];
		}
	}

	private static void SubtractMatricesWithSameStructureStartStop<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		T[] values = first.Values;
		T[] values2 = second.Values;
		T[] values3 = output.Values;
		for (int i = start; i <= stop; i++)
		{
			values3[i] = values[i] - values2[i];
		}
	}

	private static void AddMatricesWithDifferentStructureStartStop<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		if (first.HasSortedIndices && second.HasSortedIndices && output.HasSortedIndices)
		{
			for (int i = start; i <= stop; i++)
			{
				int j = first.RowPointers[i];
				int rowPointer = first.RowPointers[i + 1];
				int k = second.RowPointers[i];
				int rowPointer2 = second.RowPointers[i + 1];
				for (int l = output.RowPointers[i]; l < output.RowPointers[i + 1]; l++)
				{
					int columnIndex;
					for (columnIndex = output.ColumnIndices[l]; j < rowPointer && first.ColumnIndices[j] < columnIndex; j++)
					{
					}
					for (; k < rowPointer2 && second.ColumnIndices[k] < columnIndex; k++)
					{
					}
					T zero = T.Zero;
					if (j < rowPointer && first.ColumnIndices[j] == columnIndex)
					{
						zero += first.Values[j++];
					}
					if (k < rowPointer2 && second.ColumnIndices[k] == columnIndex)
					{
						zero += second.Values[k++];
					}
					output.Values[l] = zero;
				}
			}
			return;
		}
		for (int m = start; m <= stop; m++)
		{
			for (int n = output.RowPointers[m]; n < output.RowPointers[m + 1]; n++)
			{
				int column = output.ColumnIndices[n];
				int indexRowColumn = first.GetIndexRowColumn(m, column);
				int indexRowColumn2 = second.GetIndexRowColumn(m, column);
				T zero2 = T.Zero;
				if (indexRowColumn >= 0)
				{
					zero2 += first.Values[indexRowColumn];
				}
				if (indexRowColumn2 >= 0)
				{
					zero2 += second.Values[indexRowColumn2];
				}
				output.Values[n] = zero2;
			}
		}
	}

	private static void SubtractMatricesWithDifferentStructureStartStop<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		if (first.HasSortedIndices && second.HasSortedIndices && output.HasSortedIndices)
		{
			for (int i = start; i <= stop; i++)
			{
				int j = first.RowPointers[i];
				int rowPointer = first.RowPointers[i + 1];
				int k = second.RowPointers[i];
				int rowPointer2 = second.RowPointers[i + 1];
				for (int l = output.RowPointers[i]; l < output.RowPointers[i + 1]; l++)
				{
					int columnIndex;
					for (columnIndex = output.ColumnIndices[l]; j < rowPointer && first.ColumnIndices[j] < columnIndex; j++)
					{
					}
					for (; k < rowPointer2 && second.ColumnIndices[k] < columnIndex; k++)
					{
					}
					T zero = T.Zero;
					if (j < rowPointer && first.ColumnIndices[j] == columnIndex)
					{
						zero += first.Values[j++];
					}
					if (k < rowPointer2 && second.ColumnIndices[k] == columnIndex)
					{
						zero -= second.Values[k++];
					}
					output.Values[l] = zero;
				}
			}
			return;
		}
		for (int m = start; m <= stop; m++)
		{
			for (int n = output.RowPointers[m]; n < output.RowPointers[m + 1]; n++)
			{
				int column = output.ColumnIndices[n];
				int indexRowColumn = first.GetIndexRowColumn(m, column);
				int indexRowColumn2 = second.GetIndexRowColumn(m, column);
				T zero2 = T.Zero;
				if (indexRowColumn >= 0)
				{
					zero2 += first.Values[indexRowColumn];
				}
				if (indexRowColumn2 >= 0)
				{
					zero2 -= second.Values[indexRowColumn2];
				}
				output.Values[n] = zero2;
			}
		}
	}

	public static void DotProductMatrices<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		if (SparseMatrix<T>.HaveSameStructure(first, second))
		{
			DotProductMatricesWithSameStructure(first, second, output);
		}
		else
		{
			DotProductMatricesWithDifferentStructure(first, second, output);
		}
	}

	public static void DotProductMatrices<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ArgumentNullException.ThrowIfNull(parallelOptions, "parallelOptions");
		if (SparseMatrix<T>.HaveSameStructure(first, second))
		{
			DotProductMatricesWithSameStructure(first, second, output, parallelOptions);
		}
		else
		{
			DotProductMatricesWithDifferentStructure(first, second, output, parallelOptions);
		}
	}

	public static void DotProductMatricesWithSameStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		EnsureOutputStructure(first, output);
		DotProductData(first, second, output, 0, first.NonZeroCount - 1);
	}

	public static void DotProductMatricesWithSameStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		EnsureOutputStructure(first, output);
		if (!ShouldRunParallel(parallelOptions, first.NonZeroCount, 10000))
		{
			DotProductData(first, second, output, 0, first.NonZeroCount - 1);
		}
		else
		{
			DotProductMatricesWithSameStructureParallel(first, second, output, parallelOptions);
		}
	}

	public static void DotProductData<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		for (int i = start; i <= stop; i++)
		{
			output.Values[i] = first.Values[i] * second.Values[i];
		}
	}

	public static void DotProductMatricesWithDifferentStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		EnsureUnionStructure(first, second, output);
		DotProductDataSafe(first, second, output, 0, output.RowCount - 1);
	}

	public static void DotProductMatricesWithDifferentStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		EnsureUnionStructure(first, second, output);
		long workCount = (long)first.NonZeroCount + (long)second.NonZeroCount + output.NonZeroCount;
		if (!ShouldRunParallel(parallelOptions, workCount, 20000))
		{
			DotProductDataSafe(first, second, output, 0, output.RowCount - 1);
			return;
		}
		int rowsPerChunk = GetRowsPerChunk(output, 10000);
		DotProductMatricesWithDifferentStructureParallel(first, second, output, parallelOptions, rowsPerChunk);
	}

	public static void DotProductDataSafe<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		if (first.HasSortedIndices && second.HasSortedIndices && output.HasSortedIndices)
		{
			for (int i = start; i <= stop; i++)
			{
				int j = first.RowPointers[i];
				int rowPointer = first.RowPointers[i + 1];
				int k = second.RowPointers[i];
				int rowPointer2 = second.RowPointers[i + 1];
				for (int l = output.RowPointers[i]; l < output.RowPointers[i + 1]; l++)
				{
					int columnIndex;
					for (columnIndex = output.ColumnIndices[l]; j < rowPointer && first.ColumnIndices[j] < columnIndex; j++)
					{
					}
					for (; k < rowPointer2 && second.ColumnIndices[k] < columnIndex; k++)
					{
					}
					output.Values[l] = ((j < rowPointer && k < rowPointer2 && first.ColumnIndices[j] == columnIndex && second.ColumnIndices[k] == columnIndex) ? (first.Values[j++] * second.Values[k++]) : T.Zero);
				}
			}
			return;
		}
		for (int m = start; m <= stop; m++)
		{
			for (int n = output.RowPointers[m]; n < output.RowPointers[m + 1]; n++)
			{
				int column = output.ColumnIndices[n];
				int indexRowColumn = first.GetIndexRowColumn(m, column);
				int indexRowColumn2 = second.GetIndexRowColumn(m, column);
				output.Values[n] = ((indexRowColumn >= 0 && indexRowColumn2 >= 0) ? (first.Values[indexRowColumn] * second.Values[indexRowColumn2]) : T.Zero);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ProductMatrixVector<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output) where T : unmanaged, INumber<T>
	{
		ProductMatrixVectorStartStop(matrix, input.Span, output.Span, 0, matrix.RowCount - 1);
		ProductUpperMatrixVector(matrix, input.Span, output.Span);
	}

	public static void ProductMatrixVector<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		int nonZeroCount = matrix.NonZeroCount;
		int targetWorkPerChunk = (matrix.IsUpper ? 100000 : 20000);
		if (!ShouldRunParallel(parallelOptions, nonZeroCount, targetWorkPerChunk) || matrix.RowCount <= 10000)
		{
			ProductMatrixVectorStartStop(matrix, input.Span, output.Span, 0, matrix.RowCount - 1);
		}
		else
		{
			ProductMatrixVectorParallel(matrix, input, output, parallelOptions, GetRowsPerChunk(matrix, 10000));
		}
		ProductUpperMatrixVector(matrix, input.Span, output.Span);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ProductUpperMatrixVector<T>(SparseMatrix<T> matrix, ReadOnlySpan<T> input, Span<T> output) where T : unmanaged, INumber<T>
	{
		if (!matrix.IsUpper)
		{
			return;
		}
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		int rowCount = matrix.RowCount;
		if (matrix.HasSortedIndices)
		{
			for (int i = 0; i < rowCount; i++)
			{
				T input2 = input[i];
				int j = rowPointers[i];
				int rowPointer;
				for (rowPointer = rowPointers[i + 1]; j < rowPointer && columnIndices[j] <= i; j++)
				{
				}
				for (; j < rowPointer; j++)
				{
					output[columnIndices[j]] += values[j] * input2;
				}
			}
			return;
		}
		for (int k = 0; k < rowCount; k++)
		{
			T input3 = input[k];
			int rowPointer2 = rowPointers[k + 1];
			for (int l = rowPointers[k]; l < rowPointer2; l++)
			{
				int columnIndex = columnIndices[l];
				if (columnIndex > k)
				{
					output[columnIndex] += values[l] * input3;
				}
			}
		}
	}

	public static void ProductTransposeMatrixVector<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output) where T : unmanaged, INumber<T>
	{
		if (matrix.IsUpper)
		{
			throw new ArgumentException("Upper triangular matrix is not supported.", "matrix");
		}
		Span<T> inputSpan = input.Span;
		Span<T> outputSpan = output.Span;
		int rowCount = matrix.RowCount;
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		outputSpan.Slice(0, rowCount).Clear();
		for (int i = 0; i < rowCount; i++)
		{
			T input2 = inputSpan[i];
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				outputSpan[columnIndices[j]] += values[j] * input2;
			}
		}
	}

	public static T DotMatrixProduct<T>(SparseMatrix<T> matrix, T[] temporary, T[] input) where T : unmanaged, INumber<T>
	{
		ProductMatrixVector(matrix, input, temporary);
		return DotProduct(input, temporary);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T DotProduct<T>(T[] first, T[] second) where T : unmanaged, INumber<T>
	{
		return DotProduct(first.AsSpan(), second.AsSpan());
	}

	public static T DotProduct<T>(ReadOnlySpan<T> first, ReadOnlySpan<T> second) where T : unmanaged, INumber<T>
	{
		if (second.Length < first.Length)
		{
			throw new ArgumentException("Second input is shorter than the first.", "second");
		}
		T result = T.Zero;
		int i = 0;
		if (Vector.IsHardwareAccelerated && Vector<T>.IsSupported && first.Length >= 4 * Vector<T>.Count)
		{
			Vector<T> vector = Vector<T>.Zero;
			Vector<T> vector2 = Vector<T>.Zero;
			int scaledCount = 2 * Vector<T>.Count;
			for (int count = first.Length - scaledCount; i <= count; i += scaledCount)
			{
				Vector<T> vector3 = vector;
				int ordinal = i;
				Vector<T> vector4 = new Vector<T>(first.Slice(ordinal, first.Length - ordinal));
				ordinal = i;
				vector = vector3 + vector4 * new Vector<T>(second.Slice(ordinal, second.Length - ordinal));
				Vector<T> vector5 = vector2;
				ordinal = i + Vector<T>.Count;
				Vector<T> vector6 = new Vector<T>(first.Slice(ordinal, first.Length - ordinal));
				ordinal = i + Vector<T>.Count;
				vector2 = vector5 + vector6 * new Vector<T>(second.Slice(ordinal, second.Length - ordinal));
			}
			result = Vector.Sum(vector + vector2);
		}
		for (; i < first.Length; i++)
		{
			result += first[i] * second[i];
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ProductMatrixVectorStartStop<T>(SparseMatrix<T> matrix, ReadOnlySpan<T> input, Span<T> output, int start, int stop) where T : unmanaged, INumber<T>
	{
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		for (int i = start; i <= stop; i++)
		{
			T zero = T.Zero;
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				zero += values[j] * input[columnIndices[j]];
			}
			output[i] = zero;
		}
	}

	public static void MultiplyMatrixByScalar<T>(SparseMatrix<T> matrix, T scalar, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		int nonZeroCount = matrix.NonZeroCount;
		if (!ShouldRunParallel(parallelOptions, nonZeroCount, 10000))
		{
			for (int i = 0; i < nonZeroCount; i++)
			{
				T[] values = matrix.Values;
				int ordinal = i;
				values[ordinal] *= scalar;
			}
		}
		else
		{
			MultiplyMatrixByScalarParallel(matrix, scalar, parallelOptions);
		}
	}

	public static void MultiplyMatrixByScalar<T>(SparseMatrix<T> matrix, T scalar) where T : unmanaged, INumber<T>
	{
		int nonZeroCount = matrix.NonZeroCount;
		T[] values = matrix.Values;
		for (int i = 0; i < nonZeroCount; i++)
		{
			T[] valuesArray = values;
			int ordinal = i;
			valuesArray[ordinal] *= scalar;
		}
	}

	public static void MultiplyMatrixDiagonalByVector<T>(SparseMatrix<T> matrix, T[] values) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		for (int i = 0; i < matrix.RowCount; i++)
		{
			T[] values2 = matrix.Values;
			int diagonalPointer = matrix.DiagonalPointers[i];
			values2[diagonalPointer] *= values[i];
		}
	}

	public static void MultiplyMatrixDiagonalByVector<T>(SparseMatrix<T> matrix, T[] values, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		if (!ShouldRunParallel(parallelOptions, matrix.RowCount, 10000))
		{
			for (int i = 0; i < matrix.RowCount; i++)
			{
				T[] values2 = matrix.Values;
				int diagonalPointer = matrix.DiagonalPointers[i];
				values2[diagonalPointer] *= values[i];
			}
		}
		else
		{
			MultiplyMatrixDiagonalByVectorParallel(matrix, values, parallelOptions);
		}
	}

	public static void MultiplyMatrixDiagonalbyVector<T>(SparseMatrix<T> matrix, T[] values) where T : unmanaged, INumber<T>
	{
		MultiplyMatrixDiagonalByVector(matrix, values);
	}

	public static void MultiplyMatrixDiagonalbyVector<T>(SparseMatrix<T> matrix, T[] values, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		MultiplyMatrixDiagonalByVector(matrix, values, parallelOptions);
	}

	public static void MultiplyMatrixRowDiagonal<T>(SparseMatrix<T> matrix, T localValue, int row) where T : unmanaged, INumber<T>
	{
		T[] values = matrix.Values;
		int diagonalPointer = matrix.DiagonalPointers[row];
		values[diagonalPointer] *= localValue;
	}

	public static void ResidualsLinearSystem<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual) where T : unmanaged, INumber<T>
	{
		ResidualsLinearSystemStartStop(matrix, solution, rightHandSide, residual, 0, matrix.RowCount - 1);
	}

	public static void ResidualsLinearSystem<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		if (!ShouldRunParallel(parallelOptions, matrix.NonZeroCount, 20000))
		{
			ResidualsLinearSystemStartStop(matrix, solution, rightHandSide, residual, 0, matrix.RowCount - 1);
			return;
		}
		int rowsPerChunk = GetRowsPerChunk(matrix, 5000);
		ResidualsLinearSystemParallel(matrix, solution, rightHandSide, residual, parallelOptions, rowsPerChunk);
	}

	public static void ResidualsLinearSystemStartStop<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual, int start, int stop) where T : unmanaged, INumber<T>
	{
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		for (int i = start; i <= stop; i++)
		{
			T rightHandSide2 = rightHandSide[i];
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				rightHandSide2 -= values[j] * solution[columnIndices[j]];
			}
			residual[i] = rightHandSide2;
		}
	}

	public static void ResidualsLinearSystemRow<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual, int row) where T : unmanaged, INumber<T>
	{
		residual[row] = ResidualRow(matrix, solution, rightHandSide, row);
	}

	public static void Product<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output) where T : unmanaged, INumber<T>
	{
		ProductMatrixVector(matrix, input, output);
	}

	public static void Product<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ProductMatrixVector(matrix, input, output, parallelOptions);
	}

	public static void ProductTranspose<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output) where T : unmanaged, INumber<T>
	{
		ProductTransposeMatrixVector(matrix, input, output);
	}

	public static void ProductRowStartStop<T>(SparseMatrix<T> matrix, T[] input, T[] output, int start, int stop) where T : unmanaged, INumber<T>
	{
		ProductMatrixVectorStartStop(matrix, input, output, start, stop);
	}

	public static void ProductRows<T>(SparseMatrix<T> matrix, T[] input, T[] output, int[] rowIndices) where T : unmanaged, INumber<T>
	{
		foreach (int row in rowIndices)
		{
			ProductRow(matrix, input, output, row);
		}
	}

	public static void ProductRows<T>(SparseMatrix<T> matrix, T[] input, T[] output, Span<int> rowIndices) where T : unmanaged, INumber<T>
	{
		Span<int> rowIndicesSpan = rowIndices;
		for (int i = 0; i < rowIndicesSpan.Length; i++)
		{
			int row = rowIndicesSpan[i];
			ProductRow(matrix, input, output, row);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ProductRow<T>(SparseMatrix<T> matrix, ReadOnlySpan<T> input, Span<T> output, int row) where T : unmanaged, INumber<T>
	{
		output[row] = ProductRow(matrix, input, row);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T ProductRow<T>(SparseMatrix<T> matrix, ReadOnlySpan<T> input, int row) where T : unmanaged, INumber<T>
	{
		T zero = T.Zero;
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		int rowPointer = rowPointers[row + 1];
		for (int i = rowPointers[row]; i < rowPointer; i++)
		{
			zero += values[i] * input[columnIndices[i]];
		}
		return zero;
	}

	public static T ProductRowDifferences<T, TScalar>(SparseMatrix<T> matrix, ReadOnlySpan<TScalar> scalarField, int row) where T : unmanaged, IAdditiveIdentity<T, T>, IAdditionOperators<T, T, T>, IMultiplyOperators<T, TScalar, T> where TScalar : unmanaged, ISubtractionOperators<TScalar, TScalar, TScalar>
	{
		T additiveIdentity = T.AdditiveIdentity;
		TScalar scalarField2 = scalarField[row];
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		int rowPointerStart = rowPointers[row];
		int rowPointerEnd = rowPointers[row + 1];
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			TScalar tScalar = scalarField[columnIndices[i]] - scalarField2;
			additiveIdentity += values[i] * tScalar;
		}
		return additiveIdentity;
	}

	public static Vector2 ProductRowDifferences(SparseMatrix<Vector2> matrix, ReadOnlySpan<float> scalarField, int row)
	{
		Vector2 zero = Vector2.Zero;
		float scalarField2 = scalarField[row];
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		Vector2[] values = matrix.Values;
		int rowPointerStart = rowPointers[row];
		int rowPointerEnd = rowPointers[row + 1];
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			float scalarFieldColumnIndicesTerm = scalarField[columnIndices[i]] - scalarField2;
			zero += values[i] * scalarFieldColumnIndicesTerm;
		}
		return zero;
	}

	public static void Residuals<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual) where T : unmanaged, INumber<T>
	{
		ResidualsLinearSystem(matrix, solution, rightHandSide, residual);
	}

	public static void Residuals<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ResidualsLinearSystem(matrix, solution, rightHandSide, residual, parallelOptions);
	}

	public static T ResidualRow<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, int row) where T : unmanaged, INumber<T>
	{
		T result = rightHandSide[row];
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		int rowPointer = rowPointers[row + 1];
		for (int i = rowPointers[row]; i < rowPointer; i++)
		{
			result -= values[i] * solution[columnIndices[i]];
		}
		return result;
	}

	public static void MultiplyByScalar<T>(SparseMatrix<T> matrix, T scalar) where T : unmanaged, INumber<T>
	{
		MultiplyMatrixByScalar(matrix, scalar);
	}

	public static void MultiplyByScalar<T>(SparseMatrix<T> matrix, T scalar, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		MultiplyMatrixByScalar(matrix, scalar, parallelOptions);
	}

	public static void MultiplyDiagonalByVector<T>(SparseMatrix<T> matrix, T[] values) where T : unmanaged, INumber<T>
	{
		MultiplyMatrixDiagonalByVector(matrix, values);
	}

	public static void MultiplyDiagonalByVector<T>(SparseMatrix<T> matrix, T[] values, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		MultiplyMatrixDiagonalByVector(matrix, values, parallelOptions);
	}

	public static void Eye<T>(SparseMatrix<T> matrix) where T : unmanaged, INumber<T>
	{
		for (int i = 0; i < matrix.RowCount; i++)
		{
			for (int j = matrix.RowPointers[i]; j < matrix.RowPointers[i + 1]; j++)
			{
				matrix.Values[j] = ((matrix.ColumnIndices[j] == i) ? T.One : T.Zero);
			}
		}
	}

	public static void Filter<T>(SparseMatrix<T> matrix, T tolerance) where T : unmanaged, INumber<T>
	{
		int rowPointerStart = matrix.RowPointers[0];
		int rowPointers = 0;
		for (int i = 0; i < matrix.RowCount; i++)
		{
			int rowPointerEnd = matrix.RowPointers[i + 1];
			matrix.RowPointers[i] = rowPointers;
			for (int j = rowPointerStart; j < rowPointerEnd; j++)
			{
				if (T.Abs(matrix.Values[j]) > tolerance)
				{
					matrix.ColumnIndices[rowPointers] = matrix.ColumnIndices[j];
					matrix.Values[rowPointers] = matrix.Values[j];
					rowPointers++;
				}
			}
			rowPointerStart = rowPointerEnd;
		}
		matrix.RowPointers[matrix.RowCount] = rowPointers;
		matrix.InvalidateStructureCaches();
		matrix.Pack();
	}

	public static void FindActive<T>(SparseMatrix<T> matrix, bool[] keep, bool ignoreDiag, T tolerance) where T : unmanaged, INumber<T>
	{
		Array.Fill(keep, value: false);
		for (int i = 0; i < matrix.RowCount; i++)
		{
			for (int j = matrix.RowPointers[i]; j < matrix.RowPointers[i + 1]; j++)
			{
				int columnIndex = matrix.ColumnIndices[j];
				if ((!ignoreDiag || i != columnIndex) && T.Abs(matrix.Values[j]) > tolerance)
				{
					keep[columnIndex] = true;
				}
			}
		}
	}

	public static void AddDataRowColumn<T>(SparseMatrix<T> matrix, int row, int column, T localValue) where T : unmanaged, INumber<T>
	{
		int indexRowColumn = matrix.GetIndexRowColumn(row, column);
		if (indexRowColumn < 0)
		{
			throw new ArgumentException("The requested sparse entry does not exist.");
		}
		T[] values = matrix.Values;
		int indexRowColumn2 = indexRowColumn;
		values[indexRowColumn2] += localValue;
	}

	public static void SubtractDataRowColumn<T>(SparseMatrix<T> matrix, int row, int column, T localValue) where T : unmanaged, INumber<T>
	{
		int indexRowColumn = matrix.GetIndexRowColumn(row, column);
		if (indexRowColumn < 0)
		{
			throw new ArgumentException("The requested sparse entry does not exist.");
		}
		T[] values = matrix.Values;
		int indexRowColumn2 = indexRowColumn;
		values[indexRowColumn2] -= localValue;
	}

	public static void SetRow<T>(SparseMatrix<T> matrix, int row, int[] columns, T[] values, int length, T tolerance) where T : unmanaged, INumber<T>
	{
		BuildRow(matrix, row, columns, values, tolerance, length);
	}

	public static void BuildRow<T>(SparseMatrix<T> matrix, int row, int[] columns, T[] values, T tolerance, int length = 0) where T : unmanaged, INumber<T>
	{
		if (length == 0)
		{
			length = columns.Length;
		}
		int rowPointer = matrix.RowPointers[row];
		for (int i = 0; i < length; i++)
		{
			if (columns[i] >= 0 && T.Abs(values[i]) > tolerance)
			{
				if (rowPointer >= matrix.MaxNonZero)
				{
					throw new ArgumentException("Matrix capacity exceeded.");
				}
				matrix.ColumnIndices[rowPointer] = columns[i];
				matrix.Values[rowPointer] = values[i];
				rowPointer++;
			}
		}
		matrix.RowPointers[row + 1] = rowPointer;
		matrix.HasSortedIndices = false;
	}

	public static void ScaleRowsDiagonal<T>(SparseMatrix<T> matrix) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		for (int i = 0; i < matrix.RowCount; i++)
		{
			ScalarProductRow(matrix, i, T.One / matrix.GetDataDiagonalRow(i));
		}
	}

	public static void ScaleRows<T>(SparseMatrix<T> matrix, T[] rowScale) where T : unmanaged, INumber<T>
	{
		ScaleRowsInPlaceStartStop(matrix, rowScale, 0, matrix.RowCount - 1);
	}

	public static void ScaleRows<T>(SparseMatrix<T> matrix, T[] rowScale, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		if (!ShouldRunParallel(parallelOptions, matrix.NonZeroCount, 20000))
		{
			ScaleRowsInPlaceStartStop(matrix, rowScale, 0, matrix.RowCount - 1);
		}
		else
		{
			ScaleRowsInPlaceParallel(matrix, rowScale, parallelOptions, GetRowsPerChunk(matrix, 10000));
		}
	}

	public static void ScaleRows<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale) where T : unmanaged, INumber<T>
	{
		ScaleRowsStartStop(matrix, source, rowScale, 0, matrix.RowCount - 1);
	}

	public static void ScaleRows<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		if (!ShouldRunParallel(parallelOptions, matrix.NonZeroCount, 20000))
		{
			ScaleRowsStartStop(matrix, source, rowScale, 0, matrix.RowCount - 1);
		}
		else
		{
			ScaleRowsParallel(matrix, source, rowScale, parallelOptions, GetRowsPerChunk(matrix, 10000));
		}
	}

	public static void ScaleRowsColumns<T>(SparseMatrix<T> matrix, T[] rowScale, T[] columnScale) where T : unmanaged, INumber<T>
	{
		ScaleRowsColumnsInPlaceStartStop(matrix, rowScale, columnScale, 0, matrix.RowCount - 1);
	}

	public static void ScaleRowsColumns<T>(SparseMatrix<T> matrix, T[] rowScale, T[] columnScale, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		if (!ShouldRunParallel(parallelOptions, matrix.NonZeroCount, 20000))
		{
			ScaleRowsColumnsInPlaceStartStop(matrix, rowScale, columnScale, 0, matrix.RowCount - 1);
		}
		else
		{
			ScaleRowsColumnsInPlaceParallel(matrix, rowScale, columnScale, parallelOptions, GetRowsPerChunk(matrix, 10000));
		}
	}

	public static void ScaleSymmetric<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] diagonalScale) where T : unmanaged, INumber<T>
	{
		ScaleRowsColumns(matrix, source, diagonalScale, diagonalScale);
	}

	public static void ScaleSymmetric<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] diagonalScale, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ScaleRowsColumns(matrix, source, diagonalScale, diagonalScale, parallelOptions);
	}

	public static void ScaleSymmetric<T>(SparseMatrix<T> matrix, T[] diagonalScale) where T : unmanaged, INumber<T>
	{
		ScaleRowsColumns(matrix, diagonalScale, diagonalScale);
	}

	public static void ScaleSymmetric<T>(SparseMatrix<T> matrix, T[] diagonalScale, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		ScaleRowsColumns(matrix, diagonalScale, diagonalScale, parallelOptions);
	}

	public static void ScaleRowsColumns<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, T[] columnScale) where T : unmanaged, INumber<T>
	{
		ScaleRowsColumnsStartStop(matrix, source, rowScale, columnScale, 0, matrix.RowCount - 1);
	}

	public static void ScaleRowsColumns<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, T[] columnScale, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		if (!ShouldRunParallel(parallelOptions, matrix.NonZeroCount, 20000))
		{
			ScaleRowsColumnsStartStop(matrix, source, rowScale, columnScale, 0, matrix.RowCount - 1);
		}
		else
		{
			ScaleRowsColumnsParallel(matrix, source, rowScale, columnScale, parallelOptions, GetRowsPerChunk(matrix, 10000));
		}
	}

	private static void ScaleRowsStartStop<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, int start, int stop) where T : unmanaged, INumber<T>
	{
		int[] rowPointers = matrix.RowPointers;
		T[] values = matrix.Values;
		T[] values2 = source.Values;
		for (int i = start; i <= stop; i++)
		{
			T rowScale2 = rowScale[i];
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				values[j] = rowScale2 * values2[j];
			}
		}
	}

	private static void ScaleRowsInPlaceStartStop<T>(SparseMatrix<T> matrix, T[] rowScale, int start, int stop) where T : unmanaged, INumber<T>
	{
		int[] rowPointers = matrix.RowPointers;
		T[] values = matrix.Values;
		for (int i = start; i <= stop; i++)
		{
			T rowScale2 = rowScale[i];
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				T[] valuesArray = values;
				int ordinal = j;
				valuesArray[ordinal] *= rowScale2;
			}
		}
	}

	private static void ScaleRowsColumnsInPlaceStartStop<T>(SparseMatrix<T> matrix, T[] rowScale, T[] columnScale, int start, int stop) where T : unmanaged, INumber<T>
	{
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		for (int i = start; i <= stop; i++)
		{
			T rowScale2 = rowScale[i];
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				T[] valuesArray = values;
				int ordinal = j;
				valuesArray[ordinal] *= rowScale2 * columnScale[columnIndices[j]];
			}
		}
	}

	private static void ScaleRowsColumnsStartStop<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, T[] columnScale, int start, int stop) where T : unmanaged, INumber<T>
	{
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		T[] values = matrix.Values;
		T[] values2 = source.Values;
		for (int i = start; i <= stop; i++)
		{
			T rowScale2 = rowScale[i];
			int rowPointer = rowPointers[i + 1];
			for (int j = rowPointers[i]; j < rowPointer; j++)
			{
				int columnIndex = columnIndices[j];
				values[j] = rowScale2 * values2[j] * columnScale[columnIndex];
			}
		}
	}

	public static void ScalarProductRow<T>(SparseMatrix<T> matrix, int row, T scalar) where T : unmanaged, INumber<T>
	{
		for (int i = matrix.RowPointers[row]; i < matrix.RowPointers[row + 1]; i++)
		{
			T[] values = matrix.Values;
			int ordinal = i;
			values[ordinal] *= scalar;
		}
	}

	public static void AddDataDiagonal<T>(SparseMatrix<T> matrix, T[] values) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		for (int i = 0; i < matrix.RowCount; i++)
		{
			T[] values2 = matrix.Values;
			int diagonalPointer = matrix.DiagonalPointers[i];
			values2[diagonalPointer] += values[i];
		}
	}

	public static void AddDataDiagonalRow<T>(SparseMatrix<T> matrix, int row, T localValue) where T : unmanaged, INumber<T>
	{
		T[] values = matrix.Values;
		int diagonalPointer = matrix.DiagonalPointers[row];
		values[diagonalPointer] += localValue;
	}

	public static void SolveLower<T>(SparseMatrix<T> matrix, Span<T> solution, ReadOnlySpan<T> rightHandSide) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		int rowCount = matrix.RowCount;
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		int[] diagonalPointers = matrix.DiagonalPointers;
		T[] values = matrix.Values;
		for (int i = 0; i < rowCount; i++)
		{
			T rightHandSide2 = rightHandSide[i];
			int diagonalPointer = diagonalPointers[i];
			for (int j = rowPointers[i]; j < diagonalPointer; j++)
			{
				rightHandSide2 -= values[j] * solution[columnIndices[j]];
			}
			solution[i] = rightHandSide2 / values[diagonalPointer];
		}
	}

	public static void SolveLower<T>(SparseMatrix<T> matrix, Span<T> solution, ReadOnlySpan<T> rightHandSide, ReadOnlySpan<T> inverseDiagonal) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		int rowCount = matrix.RowCount;
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		int[] diagonalPointers = matrix.DiagonalPointers;
		T[] values = matrix.Values;
		for (int i = 0; i < rowCount; i++)
		{
			T rightHandSide2 = rightHandSide[i];
			int diagonalPointer = diagonalPointers[i];
			for (int j = rowPointers[i]; j < diagonalPointer; j++)
			{
				rightHandSide2 -= values[j] * solution[columnIndices[j]];
			}
			solution[i] = rightHandSide2 * inverseDiagonal[i];
		}
	}

	public static void SolveUnitLower<T>(SparseMatrix<T> matrix, Span<T> solution, ReadOnlySpan<T> rightHandSide) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		int rowCount = matrix.RowCount;
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		int[] diagonalPointers = matrix.DiagonalPointers;
		T[] values = matrix.Values;
		for (int i = 0; i < rowCount; i++)
		{
			T rightHandSide2 = rightHandSide[i];
			for (int j = rowPointers[i]; j < diagonalPointers[i]; j++)
			{
				rightHandSide2 -= values[j] * solution[columnIndices[j]];
			}
			solution[i] = rightHandSide2;
		}
	}

	public static void SolveUpper<T>(SparseMatrix<T> matrix, Span<T> solution, ReadOnlySpan<T> rightHandSide) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		int[] diagonalPointers = matrix.DiagonalPointers;
		T[] values = matrix.Values;
		for (int rowCountOffset = matrix.RowCount - 1; rowCountOffset >= 0; rowCountOffset--)
		{
			T rightHandSide2 = rightHandSide[rowCountOffset];
			int diagonalPointer = diagonalPointers[rowCountOffset];
			int rowPointer = rowPointers[rowCountOffset + 1];
			for (int i = diagonalPointer + 1; i < rowPointer; i++)
			{
				rightHandSide2 -= values[i] * solution[columnIndices[i]];
			}
			solution[rowCountOffset] = rightHandSide2 / values[diagonalPointer];
		}
	}

	public static void SolveUpper<T>(SparseMatrix<T> matrix, Span<T> solution, ReadOnlySpan<T> rightHandSide, ReadOnlySpan<T> inverseDiagonal) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		int[] diagonalPointers = matrix.DiagonalPointers;
		T[] values = matrix.Values;
		for (int rowCountOffset = matrix.RowCount - 1; rowCountOffset >= 0; rowCountOffset--)
		{
			T rightHandSide2 = rightHandSide[rowCountOffset];
			int diagonalPointer = diagonalPointers[rowCountOffset];
			int rowPointer = rowPointers[rowCountOffset + 1];
			for (int i = diagonalPointer + 1; i < rowPointer; i++)
			{
				rightHandSide2 -= values[i] * solution[columnIndices[i]];
			}
			solution[rowCountOffset] = rightHandSide2 * inverseDiagonal[rowCountOffset];
		}
	}

	public static void SolveUnitUpper<T>(SparseMatrix<T> matrix, Span<T> solution, ReadOnlySpan<T> rightHandSide) where T : unmanaged, INumber<T>
	{
		matrix.ComputeDiagonalPointers();
		int[] rowPointers = matrix.RowPointers;
		int[] columnIndices = matrix.ColumnIndices;
		int[] diagonalPointers = matrix.DiagonalPointers;
		T[] values = matrix.Values;
		for (int rowCountOffset = matrix.RowCount - 1; rowCountOffset >= 0; rowCountOffset--)
		{
			T rightHandSide2 = rightHandSide[rowCountOffset];
			int rowPointer = rowPointers[rowCountOffset + 1];
			for (int i = diagonalPointers[rowCountOffset] + 1; i < rowPointer; i++)
			{
				rightHandSide2 -= values[i] * solution[columnIndices[i]];
			}
			solution[rowCountOffset] = rightHandSide2;
		}
	}

	public static bool CheckSymmetric<T>(SparseMatrix<T> matrix, T tolerance) where T : unmanaged, INumber<T>
	{
		for (int i = 0; i < matrix.RowCount; i++)
		{
			for (int j = matrix.RowPointers[i]; j < matrix.RowPointers[i + 1]; j++)
			{
				int columnIndex = matrix.ColumnIndices[j];
				T matrixTerm = matrix.Values[j];
				if (i == columnIndex)
				{
					if (matrixTerm < T.Zero)
					{
						return false;
					}
					continue;
				}
				int indexRowColumn = matrix.GetIndexRowColumn(columnIndex, i);
				if (indexRowColumn < 0 || T.Abs(matrixTerm - matrix.Values[indexRowColumn]) > tolerance)
				{
					return false;
				}
			}
		}
		return true;
	}

	public static bool IsSymmetric<T>(SparseMatrix<T> matrix) where T : unmanaged, INumber<T>
	{
		return CheckSymmetric(matrix, T.CreateSaturating(1E-06));
	}

	public static (bool, T) IsDiagonallyDominant<T>(SparseMatrix<T> matrix) where T : unmanaged, INumber<T>
	{
		T zero2 = T.Zero;
		for (int i = 0; i < matrix.RowCount; i++)
		{
			T zero3 = T.Zero;
			T zero = T.Zero;
			for (int j = matrix.RowPointers[i]; j < matrix.RowPointers[i + 1]; j++)
			{
				int columnIndex = matrix.ColumnIndices[j];
				if (matrix.GetIndexRowColumn(columnIndex, i) < 0)
				{
					return (false, zero2);
				}
				if (i == columnIndex)
				{
					zero3 = T.Abs(matrix.Values[j]);
				}
				else
				{
					zero += T.Abs(matrix.Values[j]);
				}
			}
			if (zero3 < zero)
			{
				zero2 = T.Max(zero2, zero / zero3 - T.One);
			}
		}
		return (zero2 == T.Zero, zero2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddMatricesWithSameStructureParallel<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(first.NonZeroCount, (int start, int stop) =>
		{
			AddMatricesWithSameStructureStartStop(first, second, output, start, stop);
		}, parallelOptions, 10000);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddMatricesWithDifferentStructureParallel<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(output.RowCount, (int start, int stop) =>
		{
			AddMatricesWithDifferentStructureStartStop(first, second, output, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SubtractMatricesWithSameStructureParallel<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(first.NonZeroCount, (int start, int stop) =>
		{
			SubtractMatricesWithSameStructureStartStop(first, second, output, start, stop);
		}, parallelOptions, 10000);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SubtractMatricesWithDifferentStructureParallel<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(output.RowCount, (int start, int stop) =>
		{
			SubtractMatricesWithDifferentStructureStartStop(first, second, output, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DotProductMatricesWithSameStructureParallel<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(first.NonZeroCount, (int start, int stop) =>
		{
			DotProductData(first, second, output, start, stop);
		}, parallelOptions, 10000);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DotProductMatricesWithDifferentStructureParallel<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(output.RowCount, (int start, int stop) =>
		{
			DotProductDataSafe(first, second, output, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ProductMatrixVectorParallel<T>(SparseMatrix<T> matrix, Memory<T> input, Memory<T> output, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			ProductMatrixVectorStartStop(matrix, input.Span, output.Span, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MultiplyMatrixByScalarParallel<T>(SparseMatrix<T> matrix, T scalar, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		T[] values = matrix.Values;
		Parallel.SmartFor(matrix.NonZeroCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				T[] valuesArray = values;
				int ordinal = i;
				valuesArray[ordinal] *= scalar;
			}
		}, parallelOptions, 10000);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MultiplyMatrixDiagonalByVectorParallel<T>(SparseMatrix<T> matrix, T[] values, ParallelOptions parallelOptions) where T : unmanaged, INumber<T>
	{
		T[] matrixValues = matrix.Values;
		int[] diagonalPointers = matrix.DiagonalPointers;
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				T[] matrixValuesArray = matrixValues;
				int diagonalPointer = diagonalPointers[i];
				matrixValuesArray[diagonalPointer] *= values[i];
			}
		}, parallelOptions, 10000);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ResidualsLinearSystemParallel<T>(SparseMatrix<T> matrix, T[] solution, T[] rightHandSide, T[] residual, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			ResidualsLinearSystemStartStop(matrix, solution, rightHandSide, residual, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ScaleRowsParallel<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			ScaleRowsStartStop(matrix, source, rowScale, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ScaleRowsInPlaceParallel<T>(SparseMatrix<T> matrix, T[] rowScale, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			ScaleRowsInPlaceStartStop(matrix, rowScale, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ScaleRowsColumnsInPlaceParallel<T>(SparseMatrix<T> matrix, T[] rowScale, T[] columnScale, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			ScaleRowsColumnsInPlaceStartStop(matrix, rowScale, columnScale, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ScaleRowsColumnsParallel<T>(SparseMatrix<T> matrix, SparseMatrix<T> source, T[] rowScale, T[] columnScale, ParallelOptions parallelOptions, int loopSize) where T : unmanaged, INumber<T>
	{
		Parallel.SmartFor(matrix.RowCount, (int start, int stop) =>
		{
			ScaleRowsColumnsStartStop(matrix, source, rowScale, columnScale, start, stop);
		}, parallelOptions, loopSize);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool ShouldRunParallel(ParallelOptions parallelOptions, long workCount, int targetWorkPerChunk)
	{
		ArgumentNullException.ThrowIfNull(parallelOptions, "parallelOptions");
		parallelOptions.CancellationToken.ThrowIfCancellationRequested();
		if (parallelOptions.MaxDegreeOfParallelism == 1)
		{
			return false;
		}
		TaskScheduler? taskScheduler = parallelOptions.TaskScheduler;
		if (taskScheduler != null && taskScheduler.MaximumConcurrencyLevel == 1)
		{
			return false;
		}
		return workCount > targetWorkPerChunk;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetRowsPerChunk<T>(SparseMatrix<T> matrix, int targetNonZeros) where T : unmanaged
	{
		int rowCount = matrix.RowCount;
		int nonZeroCount = matrix.NonZeroCount;
		if (rowCount <= 0 || nonZeroCount <= 0)
		{
			return 1;
		}
		return (int)Math.Clamp((long)targetNonZeros * (long)rowCount / nonZeroCount, 1L, rowCount);
	}

	private static void EnsureOutputStructure<T>(SparseMatrix<T> source, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		if (!SparseMatrix<T>.HaveSameStructure(source, output))
		{
			output.Copy(source);
		}
	}

	private static void EnsureUnionStructure<T>(SparseMatrix<T> first, SparseMatrix<T> second, SparseMatrix<T> output) where T : unmanaged, INumber<T>
	{
		bool rowCountOutputTerm = output.RowCount == first.RowCount;
		if (rowCountOutputTerm && output.HasSortedIndices && first.HasSortedIndices && second.HasSortedIndices)
		{
			if (!ContainsSortedStructure(output, first) || !ContainsSortedStructure(output, second))
			{
				output.Initialize(first, second);
			}
			return;
		}
		if (rowCountOutputTerm)
		{
			for (int i = 0; (i < output.RowCount) & rowCountOutputTerm; i++)
			{
				for (int j = first.RowPointers[i]; j < first.RowPointers[i + 1]; j++)
				{
					rowCountOutputTerm &= output.CheckIndexRowColumn(i, first.ColumnIndices[j]);
				}
				for (int k = second.RowPointers[i]; k < second.RowPointers[i + 1]; k++)
				{
					rowCountOutputTerm &= output.CheckIndexRowColumn(i, second.ColumnIndices[k]);
				}
			}
		}
		if (!rowCountOutputTerm)
		{
			output.Initialize(first, second);
		}
	}

	private static bool ContainsSortedStructure<T>(SparseMatrix<T> container, SparseMatrix<T> source) where T : unmanaged, INumber<T>
	{
		for (int i = 0; i < source.RowCount; i++)
		{
			int j = container.RowPointers[i];
			int rowPointer = container.RowPointers[i + 1];
			for (int k = source.RowPointers[i]; k < source.RowPointers[i + 1]; k++)
			{
				int columnIndex;
				for (columnIndex = source.ColumnIndices[k]; j < rowPointer && container.ColumnIndices[j] < columnIndex; j++)
				{
				}
				if (j >= rowPointer || container.ColumnIndices[j] != columnIndex)
				{
					return false;
				}
				j++;
			}
		}
		return true;
	}
}
