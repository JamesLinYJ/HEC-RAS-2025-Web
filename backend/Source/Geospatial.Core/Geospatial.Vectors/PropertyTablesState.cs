namespace Geospatial.Vectors;

public class PropertyTablesState
{
	public TablesStatus Status;

	public string Message;

	public PropertyTablesState(TablesStatus status)
	{
		Status = status;
	}

	public PropertyTablesState(TablesStatus status, string message)
	{
		Status = status;
		Message = message;
	}
}
