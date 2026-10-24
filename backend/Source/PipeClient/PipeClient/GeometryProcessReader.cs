using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Utility.Systems;

namespace PipeClient;

public class GeometryProcessReader : IDisposable
{
	private NamedPipeClientStream _client;

	private BinaryReader _br;

	private BinaryWriter _bw;

	internal GeometryProcessReader(NamedPipeClientStream client)
	{
		_client = client;
		_br = new BinaryReader(client, Encoding.Default, leaveOpen: true);
		_bw = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
	}

	public void ReadGeometry(string filename, IStreamableObject streamer)
	{
		Pipe.WriteString(_bw, filename);
		streamer.StreamIn(_br);
	}

	public void Dispose()
	{
		_bw.Write("-");
		_bw.Dispose();
		_br.Dispose();
		_client.Dispose();
	}
}
