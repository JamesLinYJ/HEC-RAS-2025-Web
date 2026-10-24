using System;
using System.Diagnostics;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Systems.Tools;
using Geospatial.Rendering.TiledRenderingSystem;
using Utility.Systems;

namespace Visual.Mapping.Observables;

[DebuggerDisplay("Descriptor: {DisplayName,nq} ({Id,nq})")]
public sealed class ToolDescriptor
{
	public string Id { get; }

	public string DisplayName { get; }

	public string IconKey { get; }

	public Func<PyramidedTileManager, Func<KeyCode, bool>, MapTool> Build { get; }

	public Func<bool> IsAvailable { get; init; }

	public string Description { get; init; }

	public OptionStore Options { get; init; }

	public ToolDescriptor(string id, string displayName, string iconKey, Func<PyramidedTileManager, Func<KeyCode, bool>, MapTool> build)
	{
		Id = id;
		DisplayName = displayName;
		IconKey = iconKey;
		Build = build;
	}
}
