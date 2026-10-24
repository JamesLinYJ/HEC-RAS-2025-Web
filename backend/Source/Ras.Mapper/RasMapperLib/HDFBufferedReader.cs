using System;
using System.Linq;
using System.Runtime.InteropServices;
using H5Assist.VB;

namespace RasMapperLib;

[ComVisible(false)]
public class HDFBufferedReader<T> : IDisposable
{
	private H5Reader _hr;

	private string _datasetName;

	private int _bufferRow;

	private int _bufferRows;

	private int _rowsRead;

	private int _rows;

	private int _cols;

	private T[] _buffer1D;

	private T[,] _buffer2D;

	public int Rows => _rows;

	public int Cols => _cols;

	public H5Reader hr => _hr;

	public string DatasetName => _datasetName;

	public HDFBufferedReader(H5Reader hr, string datasetName)
	{
		_hr = hr;
		_datasetName = datasetName;
		int datasetNDims = hr.GetDatasetNDims(datasetName);
		long[] datasetDimensions = hr.GetDatasetDimensions(datasetName);
		checked
		{
			if (datasetNDims == 1)
			{
				_rows = (int)datasetDimensions[0];
				_cols = 1;
			}
			else
			{
				_rows = (int)datasetDimensions[0];
				_cols = (int)datasetDimensions[1];
			}
		}
		if ((_rows > 0) & (_cols > 0))
		{
			if (typeof(T) == typeof(double))
			{
				_bufferRows = 8192;
			}
			else if (typeof(T) == typeof(float))
			{
				_bufferRows = 16384;
			}
			else if (typeof(T) == typeof(int))
			{
				_bufferRows = 16384;
			}
			_bufferRows /= _cols;
			long[] chunkSize = hr.GetChunkSize(datasetName);
			if (chunkSize.Count() > 0 && _bufferRows > chunkSize[0])
			{
				_bufferRows = checked((int)chunkSize[0]);
			}
			if (_bufferRows > _rows)
			{
				_bufferRows = _rows;
			}
			if (datasetNDims == 1)
			{
				_buffer1D = (T[])Array.CreateInstance(typeof(T), _bufferRows);
			}
			else
			{
				_buffer2D = (T[,])Array.CreateInstance(typeof(T), _bufferRows, _cols);
			}
			_rowsRead = 0;
			FillBuffer();
		}
	}

	public void ReadRow(ref T col0)
	{
		if (_cols == 1)
		{
			col0 = _buffer1D[_bufferRow];
		}
		else
		{
			col0 = _buffer2D[_bufferRow, 0];
		}
		ReloadBufferIfNecessary();
	}

	public void ReadRow(ref T col0, ref T col1)
	{
		col0 = _buffer2D[_bufferRow, 0];
		col1 = _buffer2D[_bufferRow, 1];
		ReloadBufferIfNecessary();
	}

	public void ReadRow(ref T col0, ref T col1, ref T col2)
	{
		col0 = _buffer2D[_bufferRow, 0];
		col1 = _buffer2D[_bufferRow, 1];
		col2 = _buffer2D[_bufferRow, 2];
		ReloadBufferIfNecessary();
	}

	public void ReadRow(ref T col0, ref T col1, ref T col2, ref T col3)
	{
		col0 = _buffer2D[_bufferRow, 0];
		col1 = _buffer2D[_bufferRow, 1];
		col2 = _buffer2D[_bufferRow, 2];
		col3 = _buffer2D[_bufferRow, 3];
		ReloadBufferIfNecessary();
	}

	public void ReadRow(ref T col0, ref T col1, ref T col2, ref T col3, ref T col4)
	{
		col0 = _buffer2D[_bufferRow, 0];
		col1 = _buffer2D[_bufferRow, 1];
		col2 = _buffer2D[_bufferRow, 2];
		col3 = _buffer2D[_bufferRow, 3];
		col4 = _buffer2D[_bufferRow, 4];
		ReloadBufferIfNecessary();
	}

	public void ReadRow(ref T col0, ref T col1, ref T col2, ref T col3, ref T col4, ref T col5)
	{
		col0 = _buffer2D[_bufferRow, 0];
		col1 = _buffer2D[_bufferRow, 1];
		col2 = _buffer2D[_bufferRow, 2];
		col3 = _buffer2D[_bufferRow, 3];
		col4 = _buffer2D[_bufferRow, 4];
		col5 = _buffer2D[_bufferRow, 5];
		ReloadBufferIfNecessary();
	}

	public void ReadRow(T[] cols)
	{
		checked
		{
			if (_cols == 1)
			{
				cols[0] = _buffer1D[_bufferRow];
			}
			else
			{
				int colsTerm = Math.Min(cols.Count(), _cols);
				Buffer.BlockCopy(_buffer2D, _bufferRow * _cols * 4, cols, 0, colsTerm * 4);
			}
			ReloadBufferIfNecessary();
		}
	}

	private void FillBuffer()
	{
		int bufferRows = checked((_rowsRead + _bufferRows <= _rows) ? _bufferRows : (_rows - _rowsRead));
		if (bufferRows != 0)
		{
			if (_cols == 1)
			{
				_hr.ReadElements(_datasetName, _rowsRead, bufferRows, ref _buffer1D);
			}
			else
			{
				_hr.ReadRows(_datasetName, _rowsRead, bufferRows, ref _buffer2D);
			}
		}
		_bufferRow = 0;
	}

	private void ReloadBufferIfNecessary()
	{
		checked
		{
			_bufferRow++;
			_rowsRead++;
			if (_bufferRow == _bufferRows && _rowsRead < _rows)
			{
				FillBuffer();
			}
		}
	}

	public void Dispose()
	{
		_hr = null;
		_buffer1D = null;
		_buffer2D = null;
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
