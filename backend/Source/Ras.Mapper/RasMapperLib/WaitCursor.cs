using System;

namespace RasMapperLib;

public class WaitCursor : IDisposable
{
	public WaitCursor(object ob)
	{
	}

	public void Dispose()
	{
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
