using System;
using System.Linq;
using System.Runtime.InteropServices;
using H5Assist.VB;
using Microsoft.VisualBasic;

namespace RasMapperLib;

[ComVisible(false)]
public class HDFBufferedStringReader : IDisposable
{
	private H5Reader _hr;

	private string _datasetName;

	private int _bufferRow;

	private int _bufferRows;

	private int _rowsRead;

	private int _rows;

	private string[] _buffer1D;

	public int Rows => _rows;

	public HDFBufferedStringReader(H5Reader hr, string datasetName)
	{
		_hr = hr;
		_datasetName = datasetName;
		if (hr.GetDatasetNDims(datasetName) != 1)
		{
			throw new Exception("Error reading string column from file: '" + hr.Filename + "' only one dimension string columns can be read (dataset name ='" + datasetName + "')");
		}
		long[] datasetDimensions = hr.GetDatasetDimensions(datasetName);
		checked
		{
			_rows = (int)datasetDimensions[0];
			long[] chunkSize = hr.GetChunkSize(datasetName);
			if (chunkSize.Count() > 0)
			{
				_bufferRows = (int)chunkSize[0];
			}
			else
			{
				_bufferRows = 1024;
			}
			if (_bufferRows > _rows)
			{
				_bufferRows = _rows;
			}
			_buffer1D = new string[_bufferRows - 1 + 1];
			_rowsRead = 0;
			FillBuffer();
		}
	}

	public void ReadRow(ref string elem)
	{
		elem = Strings.Trim(_buffer1D[_bufferRow]);
		ReloadBufferIfNecessary();
	}

	private void FillBuffer()
	{
		int bufferRows = checked((_rowsRead + _bufferRows <= _rows) ? _bufferRows : (_rows - _rowsRead));
		if (bufferRows > 0)
		{
			_hr.ReadStringElements(_datasetName, _rowsRead, bufferRows, ref _buffer1D);
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
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
