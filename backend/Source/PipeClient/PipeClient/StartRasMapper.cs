using System.IO;
using System.IO.Pipes;
using System.Text;
using Utility.Systems;

namespace PipeClient;

public static class StartRasMapper
{
	public static void Launch(string pipeName, string rasmapFile, string xml)
	{
		Client.RunRemote(pipeName, "Mapper", blockUntilExit: false, (NamedPipeClientStream client) =>
		{
			RunClient(client, rasmapFile, xml);
		});
	}

	public static void Launch(string pipeName, string rasmapFile, string xml, out int procID)
	{
		Client.StartRemoteServer(pipeName, "Mapper", (NamedPipeClientStream client) =>
		{
			RunClient(client, rasmapFile, xml);
		}, out procID);
	}

	private static void RunClient(NamedPipeClientStream client, string rasmapFile, string xml)
	{
		using (new BinaryReader(client, Encoding.Default, leaveOpen: true))
		{
			using BinaryWriter writer = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
			Pipe.WriteString(writer, rasmapFile);
			Pipe.WriteString(writer, xml);
		}
	}
}
