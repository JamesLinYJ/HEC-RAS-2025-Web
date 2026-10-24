using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RasMapperLib.Utilities;

public class Hash
{
	private List<byte[]> _buffers;

	public Hash()
	{
		_buffers = new List<byte[]>();
	}

	public static bool HashEquals(byte[] hash1, byte[] hash2)
	{
		if (hash1 == null || hash2 == null)
		{
			return false;
		}
		if (hash1.Length != hash2.Length)
		{
			return false;
		}
		checked
		{
			int count = hash1.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (hash1[i] != hash2[i])
				{
					return false;
				}
			}
			return true;
		}
	}

	public void Add<T>(T[] buf) where T : struct
	{
		byte[] byteValues = new byte[checked(buf.Length * Marshal.SizeOf(new T()) - 1 + 1)];
		Buffer.BlockCopy(buf, 0, byteValues, 0, byteValues.Length);
		_buffers.Add(byteValues);
	}

	public void Add(string stringValue)
	{
		byte[] bytes = new ASCIIEncoding().GetBytes(stringValue);
		_buffers.Add(bytes);
	}

	public void Add(IList<PointM> buf)
	{
		checked
		{
			double[] doubleValues = new double[4 * buf.Count - 1 + 1];
			int count = buf.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = buf[i];
				doubleValues[4 * i] = pointM.X;
				doubleValues[4 * i + 1] = pointM.Y;
				doubleValues[4 * i + 2] = pointM.Z;
				doubleValues[4 * i + 3] = pointM.M;
			}
			byte[] byteValues = new byte[doubleValues.Length * 8 - 1 + 1];
			Buffer.BlockCopy(doubleValues, 0, byteValues, 0, byteValues.Length);
			_buffers.Add(byteValues);
		}
	}

	public void Clear()
	{
		_buffers.Clear();
	}

	public byte[] HashBytes()
	{
		MemoryStream memoryStream = new MemoryStream();
		foreach (byte[] buffer in _buffers)
		{
			memoryStream.Write(buffer, 0, buffer.Length);
		}
		return SHA256.Create().ComputeHash(memoryStream.GetBuffer());
	}

	public string HashString()
	{
		byte[] bytes = HashBytes();
		return new ASCIIEncoding().GetString(bytes);
	}
}
