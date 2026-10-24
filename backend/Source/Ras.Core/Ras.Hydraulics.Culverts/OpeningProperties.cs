using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Utility;
using Utility.Attributes;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Hydraulics.Culverts;

[DebuggerDisplay("{Name}")]
public class OpeningProperties : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, ICloneable
{
	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("K In")]
	public double KIn { get; set; }

	[HashContributor]
	[Metadata("K Out")]
	public double KOut { get; set; }

	[HashContributor]
	[Metadata("Opening")]
	public BarrelOpeningType OpeningType { get; set; } = BarrelOpeningType.ConcretePipeCulvert_SquareEdgeWithHeadwall;

	public Extent Extent => Extent.NullExtent;

	public long VersionXY => 1L;

	public (int, int) TryGetChartScale()
	{
		ChartScaleAttribute attribute = AttributeHelpers.GetAttribute<ChartScaleAttribute>(OpeningType);
		if (attribute == null)
		{
			return (-1, -1);
		}
		return (attribute.Chart, attribute.Scale);
	}

	public OperationResult IsValid()
	{
		OperationResult operationResult = true;
		if (OpeningType == BarrelOpeningType.Custom || OpeningType == BarrelOpeningType.None)
		{
			operationResult.AddFailure("Opening Properties " + Name + " is using OpeningType Custom/None, which is not currently supported.");
		}
		return operationResult;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public object Clone()
	{
		return new OpeningProperties
		{
			Name = Name,
			KIn = KIn,
			KOut = KOut,
			OpeningType = OpeningType
		};
	}

	public OperationResult DataEquals(OpeningProperties other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("OpeningProperties hash mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("OpeningProperties", "Name", Name, other.Name));
		}
		if (OpeningType != other.OpeningType)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("OpeningProperties", "OpeningType", OpeningType, other.OpeningType));
		}
		if (!MathHelpers.DoubleEquals(KIn, other.KIn))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("OpeningProperties", "KIn", KIn, other.KIn));
		}
		if (!MathHelpers.DoubleEquals(KOut, other.KOut))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("OpeningProperties", "KOut", KOut, other.KOut));
		}
		return operationResult;
	}
}
