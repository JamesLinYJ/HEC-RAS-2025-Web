using System;
using System.Collections.Generic;
using System.Text;

namespace Hec.Dss;

internal class ByteString : IDisposable
{
	public int Length => Data.Length;

	public byte[] Data { get; }

	public ByteString(int size)
	{
		Data = new byte[size];
	}

	public ByteString(string s)
	{
		Data = Encoding.ASCII.GetBytes(s);
	}

	public ByteString(List<string> labels)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string label in labels)
		{
			stringBuilder.Append(label);
			stringBuilder.Append('\0');
		}
		Data = Encoding.ASCII.GetBytes(stringBuilder.ToString());
	}

	public void Dispose()
	{
	}

	public override string ToString()
	{
		if (Data == null || Data.Length == 0)
		{
			return "";
		}
		int count = Math.Min(Array.IndexOf(Data, (byte)0), Data.Length);
		return Encoding.ASCII.GetString(Data, 0, count);
	}

	public string[] ToStringArray()
	{
		string[] parts = Encoding.ASCII.GetString(Data).TrimEnd('\0').Split('\0');
		for (int i = 0; i < parts.Length; i++)
		{
			parts[i] = parts[i].Trim();
		}
		return parts;
	}
}
