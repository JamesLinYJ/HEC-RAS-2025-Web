using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Utility.Systems;

namespace PipeClient;

public class H5ProcessReader : IDisposable
{
	private NamedPipeClientStream _client;

	private BinaryReader _br;

	private BinaryWriter _bw;

	private string _file;

	private string _dataset;

	internal H5ProcessReader(NamedPipeClientStream client, string h5File, string h5Dset)
	{
		_client = client;
		_br = new BinaryReader(client, Encoding.Default, leaveOpen: true);
		_bw = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
		_file = h5File;
		_dataset = h5Dset;
		Pipe.WriteString(_bw, _file);
		Pipe.WriteString(_bw, _dataset);
	}

	public float[] ReadRows(int rowNumber, int rowCount)
	{
		_bw.Write(rowNumber);
		_bw.Write(rowCount);
		return Pipe.ReadArray<float>(_br);
	}

	public void Dispose()
	{
		_bw.Write(-1);
		_bw.Dispose();
		_br.Dispose();
		_client.Dispose();
	}
}
