namespace RasMapperLib;

public class GeometryError
{
	public string Message;

	public GeometryErrorLevel Level;

	public string Process;

	public GeometryError(string msg, string proc = "", GeometryErrorLevel lvl = GeometryErrorLevel.Fatal)
	{
		Message = msg;
		Level = lvl;
		Process = proc;
	}
}
