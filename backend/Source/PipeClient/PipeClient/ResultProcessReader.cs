using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Utility.Extensions;
using Utility.Systems;

namespace PipeClient;

public class ResultProcessReader : IDisposable
{
	public int ServerPID;

	private NamedPipeClientStream _client;

	private BinaryReader _br;

	private BinaryWriter _bw;

	internal ResultProcessReader(NamedPipeClientStream client)
	{
		_client = client;
		_br = new BinaryReader(client, Encoding.Default, leaveOpen: true);
		_bw = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
	}

	public void ReadResult(string filename, IStreamableObject streamer)
	{
		if (File.Exists(filename))
		{
			_bw.WriteStringA(filename);
			streamer.StreamIn(_br);
		}
	}

	public void Dispose()
	{
		_bw.Write("-");
		_bw.Dispose();
		_br.Dispose();
		_client.Dispose();
	}
}
