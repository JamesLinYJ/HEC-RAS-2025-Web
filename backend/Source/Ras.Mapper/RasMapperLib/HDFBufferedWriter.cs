using System;
using System.Runtime.InteropServices;
using H5Assist.VB;

namespace RasMapperLib;

[ComVisible(false)]
public class HDFBufferedWriter<T> : IDisposable
{
	private static int _maxBufferSize;

	private H5Writer _hw;

	private string _datasetName;

	private int _bufferRow;

	private int _bufferRows;

	private int _rowsWritten;

	private int _cols;

	private T[] _buffer1D;

	private T[,] _buffer2D;

	public int RowsWritten => _rowsWritten;

	public H5Writer ActiveWriter => _hw;

	static HDFBufferedWriter()
	{
		_maxBufferSize = 65536;
	}

	public HDFBufferedWriter(H5Writer hw, string datasetName, int rows, int cols, string rowName, params string[] colNames)
	{
		_rowsWritten = 0;
		_hw = hw;
		_datasetName = datasetName;
		_cols = cols;
		if (typeof(T) == typeof(double))
		{
			_bufferRows = _maxBufferSize / 8;
		}
		else if (typeof(T) == typeof(float))
		{
			_bufferRows = _maxBufferSize / 4;
		}
		else if (typeof(T) == typeof(int))
		{
			_bufferRows = _maxBufferSize / 4;
		}
		_bufferRows /= cols;
		if (rows > 0 && _bufferRows > rows)
		{
			_bufferRows = rows;
		}
		if (cols == 1)
		{
			_hw.Create1dExtendibleDataset(datasetName, typeof(T), _bufferRows);
			_buffer1D = (T[])Array.CreateInstance(typeof(T), _bufferRows);
		}
		else
		{
			_hw.Create2dExtendibleDataset(datasetName, typeof(T), _bufferRows, cols);
			_buffer2D = (T[,])Array.CreateInstance(typeof(T), _bufferRows, cols);
		}
		hw.WriteAttribute(datasetName, "Row", rowName);
		hw.WriteAttribute(datasetName, "Column", colNames);
		_bufferRow = 0;
	}

	public void AddRow(T col0)
	{
		_buffer1D[_bufferRow] = col0;
		WriteBufferIfNecessary();
	}

	public void AddRow(T col0, T col1)
	{
		_buffer2D[_bufferRow, 0] = col0;
		_buffer2D[_bufferRow, 1] = col1;
		WriteBufferIfNecessary();
	}

	public void AddRow(T col0, T col1, T col2)
	{
		_buffer2D[_bufferRow, 0] = col0;
		_buffer2D[_bufferRow, 1] = col1;
		_buffer2D[_bufferRow, 2] = col2;
		WriteBufferIfNecessary();
	}

	public void AddRow(T col0, T col1, T col2, T col3)
	{
		_buffer2D[_bufferRow, 0] = col0;
		_buffer2D[_bufferRow, 1] = col1;
		_buffer2D[_bufferRow, 2] = col2;
		_buffer2D[_bufferRow, 3] = col3;
		WriteBufferIfNecessary();
	}

	public void AddRow(T col0, T col1, T col2, T col3, T col4)
	{
		_buffer2D[_bufferRow, 0] = col0;
		_buffer2D[_bufferRow, 1] = col1;
		_buffer2D[_bufferRow, 2] = col2;
		_buffer2D[_bufferRow, 3] = col3;
		_buffer2D[_bufferRow, 4] = col4;
		WriteBufferIfNecessary();
	}

	public void AddRow(T col0, T col1, T col2, T col3, T col4, T col5)
	{
		_buffer2D[_bufferRow, 0] = col0;
		_buffer2D[_bufferRow, 1] = col1;
		_buffer2D[_bufferRow, 2] = col2;
		_buffer2D[_bufferRow, 3] = col3;
		_buffer2D[_bufferRow, 4] = col4;
		_buffer2D[_bufferRow, 5] = col5;
		WriteBufferIfNecessary();
	}

	public void AddRow(T col0, T col1, T col2, T col3, T col4, T col5, T col6)
	{
		_buffer2D[_bufferRow, 0] = col0;
		_buffer2D[_bufferRow, 1] = col1;
		_buffer2D[_bufferRow, 2] = col2;
		_buffer2D[_bufferRow, 3] = col3;
		_buffer2D[_bufferRow, 4] = col4;
		_buffer2D[_bufferRow, 5] = col5;
		_buffer2D[_bufferRow, 6] = col6;
		WriteBufferIfNecessary();
	}

	public void AddRow(params T[] cols)
	{
		checked
		{
			if (_cols == 1)
			{
				_buffer1D[_bufferRow] = cols[0];
			}
			else
			{
				int count = _cols - 1;
				for (int i = 0; i <= count; i++)
				{
					_buffer2D[_bufferRow, i] = cols[i];
				}
			}
			WriteBufferIfNecessary();
		}
	}

	private void FlushBuffer()
	{
		if (_cols == 1)
		{
			_hw.AddElements(_datasetName, _buffer1D, _bufferRow);
		}
		else
		{
			_hw.AddRows(_datasetName, _buffer2D, _bufferRow);
		}
		_bufferRow = 0;
	}

	private void WriteBufferIfNecessary()
	{
		checked
		{
			_bufferRow++;
			_rowsWritten++;
			if (_bufferRow == _bufferRows)
			{
				FlushBuffer();
			}
		}
	}

	public void Dispose()
	{
		if (_bufferRow > 0)
		{
			FlushBuffer();
		}
		_hw = null;
		_buffer1D = null;
		_buffer2D = null;
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
