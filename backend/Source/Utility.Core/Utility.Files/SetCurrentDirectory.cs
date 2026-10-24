using System;
using System.IO;

namespace Utility.Files;

public class SetCurrentDirectory : IDisposable
{
	private readonly string _initialDirectory;

	private bool disposedValue;

	public SetCurrentDirectory(string newCurDirectory)
	{
		_initialDirectory = Directory.GetCurrentDirectory();
		if (string.IsNullOrEmpty(newCurDirectory))
		{
			return;
		}
		try
		{
			Directory.SetCurrentDirectory(newCurDirectory);
		}
		catch
		{
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposedValue)
		{
			return;
		}
		if (disposing)
		{
			try
			{
				if (string.IsNullOrEmpty(_initialDirectory))
				{
					Directory.SetCurrentDirectory(_initialDirectory);
				}
			}
			catch
			{
			}
		}
		disposedValue = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
	}
}
