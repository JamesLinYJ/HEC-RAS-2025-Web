using System;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Systems.Vector;

namespace Geospatial.Rendering.Editing;

public sealed class EditContext
{
	private readonly OptionStore _options;

	public EditStack Stack { private get; init; }

	public Func<EditOperation, bool, bool> ExecuteEditOperationOverride { private get; init; }

	public IVectorManager VectorManager { get; init; }

	public OverlayChannels Preview { get; set; }

	public OptionStore Options
	{
		get
		{
			return _options ?? OptionStore.Empty;
		}
		init
		{
			_options = value;
		}
	}

	public bool ExecuteEditOperation(EditOperation op, bool isSilent = false)
	{
		if (ExecuteEditOperationOverride != null)
		{
			return ExecuteEditOperationOverride(op, isSilent);
		}
		return Stack.Execute(op, isSilent);
	}

	public EditStack DebugGetEditStack()
	{
		return Stack;
	}
}
