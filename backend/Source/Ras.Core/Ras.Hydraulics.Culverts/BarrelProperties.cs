using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Utility;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Hydraulics.Culverts;

[DebuggerDisplay("{Name}")]
public class BarrelProperties : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, ICloneable
{
	public const double DefaultNV = 0.025;

	public long VersionXY => 1L;

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("Rise")]
	public double Rise { get; set; }

	[HashContributor]
	[Metadata("Span")]
	public double Span { get; set; }

	[HashContributor]
	[Metadata("Shape")]
	public BarrelShape Shape { get; set; }

	[HashContributor]
	[Metadata("Mann")]
	public double Mannings { get; set; } = 0.025;

	[HashContributor]
	[Metadata("Mann Bot")]
	public double ManningsBot { get; set; } = double.NaN;

	public bool UseTwoPhaseMannings => !double.IsNaN(ManningsBot);

	[HashContributor]
	[Metadata("Mann Switch Depth")]
	public double ManningsSwitchDepth { get; set; }

	[HashContributor]
	[Metadata("Depth Blocked")]
	public double DepthBlocked { get; set; }

	public Extent Extent => Extent.NullExtent;

	public OperationResult IsValid()
	{
		OperationResult operationResult = true;
		if (Shape == BarrelShape.Custom)
		{
			operationResult.AddFailure("Barrel Properties " + Name + " is using shape-type Custom, which is not supported at this time.");
		}
		if (Shape != BarrelShape.Custom && (Rise <= 0.0 || Span <= 0.0))
		{
			operationResult.AddFailure("Barrel Properties " + Name + " has an invalid Rise/Span value.");
		}
		return operationResult;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public object Clone()
	{
		return new BarrelProperties
		{
			Name = Name,
			Rise = Rise,
			Span = Span,
			Shape = Shape,
			Mannings = Mannings,
			ManningsBot = ManningsBot,
			ManningsSwitchDepth = ManningsSwitchDepth,
			DepthBlocked = DepthBlocked
		};
	}

	public OperationResult DataEquals(BarrelProperties other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("BarrelProperties hash mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "Name", Name, other.Name));
		}
		if (Shape != other.Shape)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "Shape", Shape, other.Shape));
		}
		if (!MathHelpers.DoubleEquals(Rise, other.Rise))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "Rise", Rise, other.Rise));
		}
		if (!MathHelpers.DoubleEquals(Span, other.Span))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "Span", Span, other.Span));
		}
		if (!MathHelpers.DoubleEquals(Mannings, other.Mannings))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "Mannings", Mannings, other.Mannings));
		}
		if (!MathHelpers.DoubleEquals(ManningsBot, other.ManningsBot))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "ManningsBot", ManningsBot, other.ManningsBot));
		}
		if (!MathHelpers.DoubleEquals(ManningsSwitchDepth, other.ManningsSwitchDepth))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "ManningsSwitchDepth", ManningsSwitchDepth, other.ManningsSwitchDepth));
		}
		if (!MathHelpers.DoubleEquals(DepthBlocked, other.DepthBlocked))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BarrelProperties", "DepthBlocked", DepthBlocked, other.DepthBlocked));
		}
		return operationResult;
	}
}
