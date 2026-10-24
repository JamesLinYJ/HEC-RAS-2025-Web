using System;
using System.Text;

namespace H5Assist;

public static class H5StringEncoding
{
	// HDF fixed string widths are bytes, not UTF-16 characters. A bounded
	// write must never split a UTF-8 scalar or overwrite the following field.
	public static int WriteFixed(string value, Span<byte> destination)
	{
		destination.Clear();
		int written = 0;
		foreach (Rune scalar in (value ?? "").EnumerateRunes())
		{
			if (scalar.Utf8SequenceLength > destination.Length - written) break;
			written += scalar.EncodeToUtf8(destination.Slice(written));
		}
		return written;
	}
}
