using System.Diagnostics;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.FlowAreas;

[DebuggerDisplay("{ToString()}")]
public class MeshGenerationParameters : IDataHash
{
	[HashContributor]
	[Metadata("Spacing dx")]
	public float SpacingDX { get; set; }

	[HashContributor]
	[Metadata("Spacing dy")]
	public float SpacingDY { get; set; }

	[HashContributor]
	[Metadata("Shift dx")]
	public float ShiftDX { get; set; }

	[HashContributor]
	[Metadata("Shift dy")]
	public float ShiftDY { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public override string ToString()
	{
		string spacingDXSpacingDYTerm = "[" + SpacingDX + "," + SpacingDY + "]";
		if ((ShiftDX == 0f || float.IsNaN(ShiftDX)) && (ShiftDY == 0f || float.IsNaN(ShiftDY)))
		{
			return spacingDXSpacingDYTerm + " (No Shift)";
		}
		string shiftDX = ShiftDX.ToString();
		if (ShiftDX > 0f)
		{
			shiftDX = "+" + shiftDX;
		}
		string shiftDY = ShiftDY.ToString();
		if (ShiftDY > 0f)
		{
			shiftDY = "+" + shiftDY;
		}
		return spacingDXSpacingDYTerm + " (" + shiftDX + "," + shiftDY + ")";
	}
}
