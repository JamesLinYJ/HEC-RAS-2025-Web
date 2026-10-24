namespace Utility.Communication;

public class TransferProgressEventArgs
{
	public long TotalTransferSize { get; set; }

	public long CompletedTransferSize { get; set; }

	public int PercentDone { get; set; }
}
