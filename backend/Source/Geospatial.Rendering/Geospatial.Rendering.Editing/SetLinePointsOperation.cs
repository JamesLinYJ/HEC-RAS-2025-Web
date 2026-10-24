using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class SetLinePointsOperation : EditOperation
{
	private Func<IEnumerable<Polyline>> _getOldLines;

	private IEnumerable<Polyline> _newLines;

	private List<Polyline> _deepClonedOldPolylines;

	public override string Description => "Set Line Points";

	public SetLinePointsOperation(Func<IEnumerable<Polyline>> getOldLines, IEnumerable<Polyline> newLines)
		: base(null)
	{
		_getOldLines = getOldLines;
		_newLines = newLines.ToList();
	}

	protected override bool ApplyInternal()
	{
		_deepClonedOldPolylines = new List<Polyline>();
		foreach (var (polyline, pc) in _getOldLines().Zip(_newLines))
		{
			_deepClonedOldPolylines.Add(polyline.Clone());
			polyline.Clear();
			polyline.AddRange(pc);
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		foreach (var (polyline, pc) in _getOldLines().Zip(_deepClonedOldPolylines))
		{
			polyline.Clear();
			polyline.AddRange(pc);
		}
		return true;
	}
}
