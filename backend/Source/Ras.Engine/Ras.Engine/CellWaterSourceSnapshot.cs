using System;
using System.Collections.Generic;

namespace Ras.Engine;

public sealed class CellWaterSourceSnapshot
{
	private readonly int _cellCount;

	private readonly double[] _netCellFlow;

	private readonly double[] _advectiveOutflow;

	private CellWaterExternalInflow[] _externalInflows = Array.Empty<CellWaterExternalInflow>();

	private int[] _incomingExternalOffsets = Array.Empty<int>();

	private int[] _incomingExternalIndices = Array.Empty<int>();

	private CellWaterTransfer[] _transfers = Array.Empty<CellWaterTransfer>();

	private int[] _incomingTransferOffsets = Array.Empty<int>();

	private int[] _incomingTransferIndices = Array.Empty<int>();

	private int[] _outgoingTransferOffsets = Array.Empty<int>();

	private int[] _outgoingTransferIndices = Array.Empty<int>();

	public double Duration { get; internal set; }

	public int CellCount => _cellCount;

	public ReadOnlySpan<double> NetCellFlow => _netCellFlow;

	public ReadOnlySpan<double> AdvectiveOutflow => _advectiveOutflow;

	public IReadOnlyList<CellWaterSourceInfo> Sources { get; internal set; } = Array.Empty<CellWaterSourceInfo>();

	public ReadOnlySpan<CellWaterExternalInflow> ExternalInflows => _externalInflows;

	public ReadOnlySpan<CellWaterTransfer> Transfers => _transfers;

	internal double[] WritableNetCellFlow => _netCellFlow;

	internal double[] WritableAdvectiveOutflow => _advectiveOutflow;

	internal CellWaterSourceSnapshot(int cellCount)
	{
		_cellCount = cellCount;
		_netCellFlow = new double[cellCount];
		_advectiveOutflow = new double[cellCount];
	}

	public double GetNetFlow(int cell)
	{
		return _netCellFlow[cell];
	}

	public double GetAdvectiveOutflow(int cell)
	{
		return _advectiveOutflow[cell];
	}

	public ReadOnlySpan<int> GetIncomingTransferIndices(int cell)
	{
		if (_incomingTransferOffsets.Length != 0)
		{
			return _incomingTransferIndices.AsSpan(_incomingTransferOffsets[cell], _incomingTransferOffsets[cell + 1] - _incomingTransferOffsets[cell]);
		}
		return ReadOnlySpan<int>.Empty;
	}

	internal ReadOnlySpan<int> GetOutgoingTransferIndices(int cell)
	{
		if (_outgoingTransferOffsets.Length != 0)
		{
			return _outgoingTransferIndices.AsSpan(_outgoingTransferOffsets[cell], _outgoingTransferOffsets[cell + 1] - _outgoingTransferOffsets[cell]);
		}
		return ReadOnlySpan<int>.Empty;
	}

	public ReadOnlySpan<int> GetIncomingExternalIndices(int cell)
	{
		if (_incomingExternalOffsets.Length != 0)
		{
			return _incomingExternalIndices.AsSpan(_incomingExternalOffsets[cell], _incomingExternalOffsets[cell + 1] - _incomingExternalOffsets[cell]);
		}
		return ReadOnlySpan<int>.Empty;
	}

	internal void SetTransfers(CellWaterTransfer[] transfers)
	{
		_transfers = transfers;
		if (transfers.Length == 0)
		{
			_incomingTransferOffsets = Array.Empty<int>();
			_incomingTransferIndices = Array.Empty<int>();
			_outgoingTransferOffsets = Array.Empty<int>();
			_outgoingTransferIndices = Array.Empty<int>();
		}
		else
		{
			_incomingTransferOffsets = new int[_cellCount + 1];
			_outgoingTransferOffsets = new int[_cellCount + 1];
			BuildIncomingTransferIndex();
		}
	}

	internal void SetExternalInflows(CellWaterExternalInflow[] inflows)
	{
		_externalInflows = inflows;
		if (inflows.Length == 0)
		{
			_incomingExternalOffsets = Array.Empty<int>();
			_incomingExternalIndices = Array.Empty<int>();
			return;
		}
		_incomingExternalOffsets = new int[_cellCount + 1];
		BuildIncomingIndex(inflows.Length, _incomingExternalOffsets, ref _incomingExternalIndices, (int index) => inflows[index].Cell);
	}

	private void BuildIncomingTransferIndex()
	{
		BuildIncomingIndex(_transfers.Length, _incomingTransferOffsets, ref _incomingTransferIndices, (int index) => _transfers[index].ReceiverCell);
		BuildIncomingIndex(_transfers.Length, _outgoingTransferOffsets, ref _outgoingTransferIndices, (int index) => _transfers[index].DonorCell);
	}

	private static void BuildIncomingIndex(int count, int[] offsets, ref int[] indices, Func<int, int> getCell)
	{
		Array.Clear(offsets);
		for (int i = 0; i < count; i++)
		{
			offsets[getCell(i) + 1]++;
		}
		for (int j = 0; j < offsets.Length - 1; j++)
		{
			offsets[j + 1] += offsets[j];
		}
		indices = new int[count];
		int[] intValues = (int[])offsets.Clone();
		for (int k = 0; k < count; k++)
		{
			indices[intValues[getCell(k)]++] = k;
		}
	}
}
