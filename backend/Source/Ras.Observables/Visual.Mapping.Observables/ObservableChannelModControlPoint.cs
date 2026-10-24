using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservableChannelModControlPoint : ObservableBaseControlPoint
{
	public override ObservableChannelModTopology Topology => base.Topology as ObservableChannelModTopology;

	[NotifyPropertyChangedForTableColumn("PatchType")]
	public bool IsMasked
	{
		get
		{
			object tableRow = TableRow["IsMasked"];
			if (tableRow == null)
			{
				return false;
			}
			return (bool)tableRow;
		}
		set
		{
			SetProperty(IsMasked, value, this, (ObservableChannelModControlPoint model, bool condition) =>
			{
				model._table.SetCellValue(model.Fid, "IsMasked", condition);
			}, "IsMasked");
		}
	}

	public ObservableChannelModControlPoint(ObservableTopology topo, int fid)
		: base(topo, fid)
	{
	}
}
