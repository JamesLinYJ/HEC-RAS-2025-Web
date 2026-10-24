using Utility;

namespace H5Assist.Caching;

public static class H5ResourceWrapper
{
	public static NativeResourceWrapper<H5Reader> KeepOpen(string filename)
	{
		return NativeResourceWrapper<H5Reader>.Give(new H5Reader(filename));
	}

	public static NativeResourceWrapper<H5Reader> Give(H5Reader hr)
	{
		return NativeResourceWrapper<H5Reader>.Give(hr);
	}

	public static NativeResourceWrapper<H5Reader> OpenOnDemand(string filename)
	{
		return new NativeResourceWrapper<H5Reader>(() => new H5Reader(filename));
	}

	public static void ChangeToOpenOnDemand(NativeResourceWrapper<H5Reader> wrapper, string filename)
	{
		wrapper.ChangeToOpenOnDemand(() => new H5Reader(filename));
	}
}
