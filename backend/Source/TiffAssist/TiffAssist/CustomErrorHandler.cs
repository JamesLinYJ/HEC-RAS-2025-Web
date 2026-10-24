using BitMiracle.LibTiff.Classic;

namespace TiffAssist;

public class CustomErrorHandler : TiffErrorHandler
{
	public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
	{
	}

	public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
	{
		string.Format(format, args);
	}

	public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
	{
	}

	public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
	{
	}
}
