using System;

namespace PipeClient;

public static class ReadHDF5
{
	public static H5ProcessReader Launch(string h5File, string h5Dset)
	{
		return new H5ProcessReader(Client.StartRemoteServer(Guid.NewGuid().ToString(), "ReadHDF5"), h5File, h5Dset);
	}
}
