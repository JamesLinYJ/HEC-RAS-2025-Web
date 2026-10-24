using System;
using System.ComponentModel;
using System.Data;

namespace RasMapperLib;

[DesignerCategory("Code")]
public class CustomDataColumn : DataColumn
{
	public DataColumnViewState ViewState { get; }

	public DataColumnGroup Group { get; }

	public CustomDataColumn(string header, Type type, DataColumnViewState viewState, DataColumnGroup group = null)
		: base(header, type)
	{
		ViewState = viewState;
		Group = group;
	}

	public CustomDataColumn()
	{
	}
}
