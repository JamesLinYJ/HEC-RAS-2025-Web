using System;
using System.IO;

namespace Utility.Extensions;

public class BufferedBinaryReader : IDisposable
{
	public BinaryReader Reader;

	public BufferedBinaryReader(BinaryReader br)
	{
		byte[] buffer = br.ReadBuffer<byte>();
		Reader = new BinaryReader(new MemoryStream(buffer));
	}

	public void Dispose()
	{
		Reader.Dispose();
	}
}
