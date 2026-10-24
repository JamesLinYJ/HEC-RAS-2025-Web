namespace RasMapperLib;

public class PropertyTablesState
{
	protected class TableEntry
	{
		public string AssociationName;

		public string LayerName;

		public bool CanRecompute;

		public bool? ShouldRecompute;

		public string Message;
	}

	public TablesStatus Status;

	public string Message;

	public PropertyTablesState(TablesStatus status, string message)
	{
		Status = status;
		Message = message;
	}
}
