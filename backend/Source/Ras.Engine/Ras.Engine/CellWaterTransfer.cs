namespace Ras.Engine;

public readonly struct CellWaterTransfer(int donorCell, int receiverCell, double flow)
{
	public int DonorCell { get; } = donorCell;

	public int ReceiverCell { get; } = receiverCell;

	public double Flow { get; } = flow;
}
