using System;
using System.IO;
using System.Text;

namespace Utility.Extensions;

public class BufferedBinaryWriter : IDisposable
{
	public BinaryWriter Writer;

	public MemoryStream Stream;

	private BinaryWriter _flushToThis;

	public BufferedBinaryWriter(BinaryWriter bw)
	{
		_flushToThis = bw;
		Stream = new MemoryStream();
		Writer = new BinaryWriter(Stream, Encoding.Default, leaveOpen: true);
	}

	public void Dispose()
	{
		byte[] byteValues = new byte[Stream.Length];
		Buffer.BlockCopy(Stream.GetBuffer(), 0, byteValues, 0, byteValues.Length);
		_flushToThis.WriteBuffer<byte>(byteValues);
		Stream.Dispose();
	}
}
