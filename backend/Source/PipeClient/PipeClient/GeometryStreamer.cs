using System;

namespace PipeClient;

public static class GeometryStreamer
{
	public static GeometryProcessReader Launch()
	{
		return new GeometryProcessReader(Client.StartRemoteServer(Guid.NewGuid().ToString(), "StreamGeometry"));
	}
}
