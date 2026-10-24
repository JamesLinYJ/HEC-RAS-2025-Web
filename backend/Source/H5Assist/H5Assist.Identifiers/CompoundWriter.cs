using System;

namespace H5Assist.Identifiers;

public class CompoundWriter : IDisposable
{
	private H5Writer _h5w;

	private string _dataset;

	private int _numElements;

	public readonly CompoundTypeDefinition DataType;

	private byte[] _buffer;

	public CompoundWriter(H5Writer h5w, string dataset, int numElements)
	{
		_h5w = h5w;
		_dataset = dataset;
		_numElements = numElements;
		using (H5DatasetID h5DatasetID = H5DatasetID.Open(h5w.H5FileID, dataset, needType: true, needSpace: false))
		{
			DataType = h5DatasetID.DataType.GetCompoundDataType();
		}
		_buffer = new byte[DataType.SumSize() * numElements];
	}

	public void WriteColumn(int idx, Array col)
	{
		if (col == null || col.Length != _numElements)
		{
			throw new ArgumentException("col");
		}
		CompoundFieldDefinition compoundFieldDefinition = DataType.Fields[idx];
		int sumSize = DataType.SumSize();
		Type type = compoundFieldDefinition.Type;
		Type elementType = col.GetType().GetElementType();
		if (type != elementType)
		{
			throw new ArgumentException("Type doesn't match in compound dataset.");
		}
		if (type == typeof(string))
		{
			WriteColumn(idx, (string[])col);
			return;
		}
		byte[] bytes = null;
		int offset = compoundFieldDefinition.Offset;
		if (type == typeof(byte))
		{
			byte[] colArray = (byte[])col;
			for (int i = 0; i < col.Length; i++)
			{
				_buffer[offset] = colArray[i];
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(bool))
		{
			bool[] col2 = (bool[])col;
			for (int j = 0; j < col.Length; j++)
			{
				bytes = BitConverter.GetBytes(col2[j]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(short))
		{
			short[] col3 = (short[])col;
			for (int k = 0; k < col.Length; k++)
			{
				bytes = BitConverter.GetBytes(col3[k]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(int))
		{
			int[] col4 = (int[])col;
			for (int l = 0; l < col.Length; l++)
			{
				bytes = BitConverter.GetBytes(col4[l]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(long))
		{
			long[] col5 = (long[])col;
			for (int m = 0; m < col.Length; m++)
			{
				bytes = BitConverter.GetBytes(col5[m]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(float))
		{
			float[] col6 = (float[])col;
			for (int n = 0; n < col.Length; n++)
			{
				bytes = BitConverter.GetBytes(col6[n]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(double))
		{
			double[] col7 = (double[])col;
			for (int intervalIndex = 0; intervalIndex < col.Length; intervalIndex++)
			{
				bytes = BitConverter.GetBytes(col7[intervalIndex]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		throw new NotImplementedException();
	}

	public void WriteColumn<T>(int idx, T[] col) where T : struct
	{
		if (col == null || col.Length != _numElements)
		{
			throw new ArgumentException("col");
		}
		CompoundFieldDefinition compoundFieldDefinition = DataType.Fields[idx];
		int sumSize = DataType.SumSize();
		Type type = compoundFieldDefinition.Type;
		if (type != typeof(T))
		{
			throw new ArgumentException("Type doesn't match in compound dataset.");
		}
		byte[] bytes = null;
		int offset = compoundFieldDefinition.Offset;
		if (type == typeof(byte))
		{
			byte[] colArray = (byte[])(object)col;
			for (int i = 0; i < col.Length; i++)
			{
				_buffer[offset] = colArray[i];
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(bool))
		{
			bool[] col2 = (bool[])(object)col;
			for (int j = 0; j < col.Length; j++)
			{
				bytes = BitConverter.GetBytes(col2[j]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(short))
		{
			short[] col3 = (short[])(object)col;
			for (int k = 0; k < col.Length; k++)
			{
				bytes = BitConverter.GetBytes(col3[k]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(int))
		{
			int[] col4 = (int[])(object)col;
			for (int l = 0; l < col.Length; l++)
			{
				bytes = BitConverter.GetBytes(col4[l]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(long))
		{
			long[] col5 = (long[])(object)col;
			for (int m = 0; m < col.Length; m++)
			{
				bytes = BitConverter.GetBytes(col5[m]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(float))
		{
			float[] col6 = (float[])(object)col;
			for (int n = 0; n < col.Length; n++)
			{
				bytes = BitConverter.GetBytes(col6[n]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		if (type == typeof(double))
		{
			double[] col7 = (double[])(object)col;
			for (int intervalIndex = 0; intervalIndex < col.Length; intervalIndex++)
			{
				bytes = BitConverter.GetBytes(col7[intervalIndex]);
				Buffer.BlockCopy(bytes, 0, _buffer, offset, bytes.Length);
				offset += sumSize;
			}
			return;
		}
		throw new NotImplementedException();
	}

	public void WriteColumn(int idx, string[] col)
	{
		if (col == null || col.Length != _numElements)
		{
			throw new ArgumentException("col");
		}
		CompoundFieldDefinition compoundFieldDefinition = DataType.Fields[idx];
		int sumSize = DataType.SumSize();
		if (compoundFieldDefinition.Type != typeof(string))
		{
			throw new ArgumentException("Type doesn't match in compound dataset.");
		}
		int offset = compoundFieldDefinition.Offset;
		int size = compoundFieldDefinition.Size;
		foreach (string value in col)
		{
			H5StringEncoding.WriteFixed(value, _buffer.AsSpan(offset, size));
			offset += sumSize;
		}
	}

	public void Dispose()
	{
		_h5w.AddBuffer1D(_dataset, _buffer);
	}
}
