using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Utility.Extensions;
using Utility.Systems;

namespace PipeClient;

public class ResultProcessReaderAsync : IDisposable
{
	public int ServerPID;

	private NamedPipeClientStream _client;

	private BinaryReader _br;

	private BinaryWriter _bw;

	internal ResultProcessReaderAsync(NamedPipeClientStream client)
	{
		_client = client;
		_br = new BinaryReader(client, Encoding.Default, leaveOpen: true);
		_bw = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
	}

	public async Task ReadResultAsync(string filename, IStreamableObject streamer)
	{
		if (File.Exists(filename))
		{
			_bw.WriteStringA(filename);
			// Deserialize the actual response off the caller's thread, and
			// propagate completion/errors instead of awaiting a zero-byte read.
			await Task.Run(() => streamer.StreamIn(_br)).ConfigureAwait(false);
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
