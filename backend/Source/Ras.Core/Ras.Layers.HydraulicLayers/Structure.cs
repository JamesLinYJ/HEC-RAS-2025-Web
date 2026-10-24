using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.Features;
using Geospatial.Vectors;
using Utility;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.HydraulicLayers;

[DebuggerDisplay("{Name}")]
public class Structure : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, ICloneable
{
	public static class MessageCodes
	{
		public static readonly MessageClass StructurePropertiesNotFound = new MessageClass("Structure_StructurePropertiesNotFound", MessageSeverity.Error, "Structure \"{StructureName}\" cannot find associated properties \"{StructurePropertiesName}\".");

		public static readonly MessageClass StructureMissingRequiredProperty = new MessageClass("Structure_MissingRequiredProperty", MessageSeverity.Error, "Structure \"{StructureName}\" is missing required property \"{PropertyName}\".");

		public static Message GetStructurePropertiesNotFound(string structureName, string structureTypeName)
		{
			return StructurePropertiesNotFound.Create(structureName, structureTypeName);
		}

		public static Message GetStructureMissingRequiredProperty(string structureName, string propName)
		{
			return StructureMissingRequiredProperty.Create(structureName, propName);
		}
	}

	private Polyline _polyline;

	private VersionTracker _tracker;

	public long VersionXY => _tracker.GetVersion(_polyline);

	public Geometry Geometry { get; set; }

	[HashContributor]
	[DatasetName("Polyline")]
	public Polyline Polyline
	{
		get
		{
			return _polyline;
		}
		set
		{
			_tracker.Assign(ref _polyline, value);
		}
	}

	public Point UpstreamPoint => Polyline.First();

	public Point DownstreamPoint => Polyline.Last();

	public double Length => Polyline.Length;

	public Extent Extent => Polyline?.Extent ?? Extent.NullExtent;

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("Structure Properties")]
	public string StructurePropertyName { get; set; }

	public OperationResult IsValid()
	{
		if (Geometry == null)
		{
			throw new NullReferenceException("Geometry");
		}
		OperationResult operationResult = true;
		if (Polyline == null || Polyline.Count < 2 || Polyline.Length <= 0.0)
		{
			operationResult.AddFailure(FeatureMessageCodes.GetBadOrMissingFeatureData(Name));
		}
		if (string.IsNullOrEmpty(StructurePropertyName))
		{
			operationResult.AddFailure(MessageCodes.GetStructureMissingRequiredProperty(Name, "Structure Properties"));
		}
		TryEvaluateAssociations(out var strucProps);
		if (strucProps == null)
		{
			operationResult.AddFailure(MessageCodes.GetStructurePropertiesNotFound(Name, StructurePropertyName));
		}
		return operationResult;
	}

	public bool TryEvaluateAssociations(out StructureProperties strucProps)
	{
		if (Geometry == null)
		{
			throw new NullReferenceException("Geometry");
		}
		strucProps = null;
		return false;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public object Clone()
	{
		return new Structure
		{
			Name = Name,
			Polyline = ((Polyline != null) ? new Polyline(Polyline) : null),
			StructurePropertyName = StructurePropertyName
		};
	}

	public OperationResult DataEquals(Structure other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("Structure hash mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("Structure", "Name", Name, other.Name));
		}
		return operationResult;
	}
}
