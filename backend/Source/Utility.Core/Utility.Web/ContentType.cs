namespace Utility.Web;

public class ContentType
{
	private string _contentType;

	public static ContentType None => new ContentType("");

	public static ContentType Javascript => new ContentType("application/javascript");

	public static ContentType OctetStream => new ContentType("application/octet-stream");

	public static ContentType OGG => new ContentType("application/ogg");

	public static ContentType PDF => new ContentType("application/pdf");

	public static ContentType XML => new ContentType("application/xml");

	public static ContentType JSON => new ContentType("application/json");

	public static ContentType ZIP => new ContentType("application/zip");

	public static ContentType WWWFormUrlEncoded => new ContentType("application/x-www-form-urlencoded");

	public static ContentType MPEG => new ContentType("audio/mpeg");

	public static ContentType GIF => new ContentType("image/gif");

	public static ContentType JPEG => new ContentType("image/jpeg");

	public static ContentType PNG => new ContentType("image/png");

	public static ContentType TIFF => new ContentType("image/tiff");

	public static ContentType HTML => new ContentType("text/html");

	public static ContentType Plain => new ContentType("text/plain");

	public ContentType(string contentTypeStr)
	{
		_contentType = contentTypeStr;
	}

	public override string ToString()
	{
		return _contentType;
	}
}
