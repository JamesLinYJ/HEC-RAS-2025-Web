using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Utility.Communication;

public class TransferRequest
{
	public delegate void ProgressEvent(object sender, TransferProgressEventArgs e);

	public ProgressEvent TransferProgressCallback;

	public TransferDirection TransferDirection { get; set; }

	public FileProxy LocalFile { get; set; }

	public FileProxy RemoteFile { get; set; }

	public bool FileSizesMatch => LocalFile.FileSize == RemoteFile.FileSize;

	public bool CanForcePull
	{
		get
		{
			if (TransferDirection == TransferDirection.RemoteToLocal)
			{
				return false;
			}
			if (!RemoteFile.Exists)
			{
				return false;
			}
			return true;
		}
	}

	public bool CanForcePush
	{
		get
		{
			if (TransferDirection == TransferDirection.LocalToRemote)
			{
				return false;
			}
			if (!LocalFile.Exists)
			{
				return false;
			}
			return true;
		}
	}

	public void S3Request_UploadProgressEvent(object sender, UploadProgressArgs e)
	{
		if (e.TotalBytes != 0L)
		{
			TransferProgressEventArgs e2 = new TransferProgressEventArgs
			{
				TotalTransferSize = e.TotalBytes,
				CompletedTransferSize = e.TransferredBytes,
				PercentDone = e.PercentDone
			};
			TransferProgressCallback(this, e2);
		}
	}

	public void S3Request_WriteObjectProgressEvent(object sender, WriteObjectProgressArgs e)
	{
		if (e.TotalBytes != 0L)
		{
			TransferProgressEventArgs e2 = new TransferProgressEventArgs
			{
				TotalTransferSize = e.TotalBytes,
				CompletedTransferSize = e.TransferredBytes,
				PercentDone = e.PercentDone
			};
			TransferProgressCallback(this, e2);
		}
	}

	public TransferRequest Clone()
	{
		return new TransferRequest
		{
			LocalFile = LocalFile,
			RemoteFile = RemoteFile,
			TransferDirection = TransferDirection
		};
	}
}
