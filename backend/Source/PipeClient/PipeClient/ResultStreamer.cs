using System;

namespace PipeClient;

public static class ResultStreamer
{
	public static ResultProcessReader Launch()
	{
		int PID = 0;
		return new ResultProcessReader(Client.StartRemoteServer(Guid.NewGuid().ToString(), "StreamResult", out PID))
		{
			ServerPID = PID
		};
	}
}
