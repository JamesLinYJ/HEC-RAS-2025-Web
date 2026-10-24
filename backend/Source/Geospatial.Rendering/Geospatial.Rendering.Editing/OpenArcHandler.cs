using System;

namespace Geospatial.Rendering.Editing;

public class OpenArcHandler : OpenFeatureHandler
{
	private readonly NodeArcEditContext _context;

	public OpenArcHandler(NodeArcEditContext context, Func<HitTestArgs, int> hitClosest, Func<int, EditContext, IEditHandler> buildEditHandler)
		: base(hitClosest, buildEditHandler)
	{
		_context = context;
	}

	protected override void OnOpening(EditContext ctx)
	{
		_context.OpenFeatureSelectedSetCache.Clear();
	}

	public override bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (hit == null)
		{
			return false;
		}
		return _context.EditableNetwork.TryPreviewSelectArc(hit);
	}
}
