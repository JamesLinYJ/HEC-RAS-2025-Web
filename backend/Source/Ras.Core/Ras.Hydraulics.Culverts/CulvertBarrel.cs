using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.Features;
using Geospatial.Vectors;
using Ras.Layers;
using Utility;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Hydraulics.Culverts;

[DebuggerDisplay("{Name}")]
public class CulvertBarrel : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, ICloneable
{
	public static class MessageCodes
	{
		public static readonly MessageClass BarrelMissingRequiredProperty = new MessageClass("Barrel_MissingRequiredProperty", MessageSeverity.Error, "Culvert Barrel \"{BarrelName}\" is missing required property \"{PropertyName}\".");

		public static readonly MessageClass BarrelPropertiesNotFound = new MessageClass("Barrel_BarrelPropertiesNotFound", MessageSeverity.Error, "Culvert Barrel \"{BarrelName}\" could not identify barrel properties \"{BarrelPropertiesName}\".");

		public static readonly MessageClass BarrelOpeningNotFound = new MessageClass("Barrel_OpeningNotFound", MessageSeverity.Error, "Culvert Barrel \"{BarrelName}\" could not identify opening \"{OpeningPropertiesName}\".");

		public static Message GetBarrelMissingRequiredProperty(string barrelName, string propName)
		{
			return BarrelMissingRequiredProperty.Create(barrelName, propName);
		}

		public static Message GetBarrelPropertiesNotFound(string barrelName, string barrelProps)
		{
			return BarrelPropertiesNotFound.Create(barrelName, barrelProps);
		}

		public static Message GetBarrelOpeningNotFound(string barrelName, string openingName)
		{
			return BarrelOpeningNotFound.Create(barrelName, openingName);
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
	[Metadata("Barrel Properties")]
	public string BarrelPropertyName { get; set; }

	[HashContributor]
	[Metadata("Upstream Invert")]
	public double UpstreamInvert { get; set; }

	[HashContributor]
	[Metadata("Upstream Opening")]
	public string UpstreamOpeningName { get; set; }

	[HashContributor]
	[Metadata("Downstream Invert")]
	public double DownstreamInvert { get; set; }

	[HashContributor]
	[Metadata("Downstream Opening")]
	public string DownstreamOpeningName { get; set; }

	private string ActualDownstreamOpeningNameToUse
	{
		get
		{
			if (!string.IsNullOrEmpty(DownstreamOpeningName))
			{
				return DownstreamOpeningName;
			}
			return UpstreamOpeningName;
		}
	}

	public BarrelPropertyTables Tables { get; set; }

	public Polygon GetBarrelPolygonOutline(BarrelProperties props)
	{
		throw new NotImplementedException();
	}

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
		if (string.IsNullOrEmpty(UpstreamOpeningName))
		{
			operationResult.AddFailure(MessageCodes.GetBarrelMissingRequiredProperty(Name, "Upstream Opening"));
		}
		if (string.IsNullOrEmpty(BarrelPropertyName))
		{
			operationResult.AddFailure(MessageCodes.GetBarrelMissingRequiredProperty(Name, "Barrel Properties"));
		}
		TryEvaluateAssociations(out var barrelProps, out var usOpeningProps, out var dsOpeningProps);
		if (barrelProps == null)
		{
			operationResult.AddFailure(MessageCodes.GetBarrelPropertiesNotFound(Name, BarrelPropertyName));
		}
		if (usOpeningProps == null)
		{
			operationResult.AddFailure(MessageCodes.GetBarrelOpeningNotFound(Name, UpstreamOpeningName));
		}
		if (dsOpeningProps == null)
		{
			operationResult.AddFailure(MessageCodes.GetBarrelOpeningNotFound(Name, BarrelPropertyName));
		}
		return operationResult;
	}

	public void TryEvaluateAssociations(out BarrelProperties barrelProps, out OpeningProperties usOpeningProps, out OpeningProperties dsOpeningProps)
	{
		if (Geometry == null)
		{
			throw new NullReferenceException("Geometry");
		}
		barrelProps = Geometry.BarrelPropertiesLayer.TryFindByName(BarrelPropertyName);
		usOpeningProps = Geometry.OpeningPropertiesLayer.TryFindByName(UpstreamOpeningName);
		if (string.IsNullOrEmpty(DownstreamOpeningName))
		{
			dsOpeningProps = usOpeningProps;
		}
		else
		{
			dsOpeningProps = Geometry.OpeningPropertiesLayer.TryFindByName(ActualDownstreamOpeningNameToUse);
		}
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public object Clone()
	{
		return new CulvertBarrel
		{
			Name = Name,
			Polyline = ((Polyline != null) ? new Polyline(Polyline) : null),
			BarrelPropertyName = BarrelPropertyName,
			UpstreamInvert = UpstreamInvert,
			DownstreamInvert = DownstreamInvert,
			UpstreamOpeningName = UpstreamOpeningName,
			DownstreamOpeningName = DownstreamOpeningName
		};
	}

	public OperationResult DataEquals(CulvertBarrel other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("CulvertBarrel hash mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("CulvertBarrel", "Name", Name, other.Name));
		}
		if (!Polyline.EqualsXY(other.Polyline))
		{
			operationResult.AddFailure("Polyline mismatch.");
		}
		if (!StringHelpers.StringEquals(BarrelPropertyName, other.BarrelPropertyName))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("CulvertBarrel", "BarrelPropertyName", BarrelPropertyName, other.BarrelPropertyName));
		}
		if (!MathHelpers.DoubleEquals(UpstreamInvert, other.UpstreamInvert))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("CulvertBarrel", "UpstreamInvert", UpstreamInvert, other.UpstreamInvert));
		}
		if (!MathHelpers.DoubleEquals(DownstreamInvert, other.DownstreamInvert))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("CulvertBarrel", "DownstreamInvert", DownstreamInvert, other.DownstreamInvert));
		}
		if (!StringHelpers.StringEquals(UpstreamOpeningName, other.UpstreamOpeningName))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("CulvertBarrel", "UpstreamOpeningName", UpstreamOpeningName, other.UpstreamOpeningName));
		}
		if (!StringHelpers.StringEquals(DownstreamOpeningName, other.DownstreamOpeningName))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("CulvertBarrel", "DownstreamOpeningName", DownstreamOpeningName, other.DownstreamOpeningName));
		}
		return operationResult;
	}
}
