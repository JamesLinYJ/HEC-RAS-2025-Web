using System;
using H5Assist.VB;

namespace RasMapperLib;

public class HDFBufferedStringWriter : IDisposable
{
	private const int _maxBufferSize = 65536;

	private H5Writer _hw;

	private string _datasetName;

	private int _bufferRow;

	private int _bufferRows;

	private int _rowsWritten;

	private string[] _buffer1D;

	public int RowsWritten => _rowsWritten;

	public H5Writer ActiveWriter => _hw;

	public HDFBufferedStringWriter(H5Writer hw, string datasetName, int rows, int maxStringSize, string rowName, string colName)
	{
		_rowsWritten = 0;
		_hw = hw;
		_datasetName = datasetName;
		_bufferRows = 65536 / maxStringSize;
		if (rows > 0 && _bufferRows > rows)
		{
			_bufferRows = rows;
		}
		_hw.Create1dExtendibleStringDataset(datasetName, _bufferRows, maxStringSize);
		_buffer1D = new string[checked(_bufferRows - 1 + 1)];
		hw.WriteAttribute(datasetName, "Row", rowName);
		hw.WriteAttribute(datasetName, "Column", colName);
		_bufferRow = 0;
	}

	public void AddRow(string col0)
	{
		if (string.IsNullOrEmpty(col0))
		{
			_buffer1D[_bufferRow] = "";
		}
		else
		{
			_buffer1D[_bufferRow] = col0;
		}
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

	private void FlushBuffer()
	{
		_hw.AddStringElements(_datasetName, _buffer1D, _bufferRow);
		_bufferRow = 0;
	}

	public void Dispose()
	{
		if (_bufferRow > 0)
		{
			FlushBuffer();
		}
		_hw = null;
		_buffer1D = null;
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
