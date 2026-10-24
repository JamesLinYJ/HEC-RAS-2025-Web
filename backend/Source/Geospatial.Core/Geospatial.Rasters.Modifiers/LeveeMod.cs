using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Geospatial.Rasters.Modifiers;

public class LeveeMod : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	public static class MessageCodes
	{
		public static readonly MessageClass LeveePropertiesNotFound = new MessageClass("Levee_PropertiesNotFound", MessageSeverity.Error, "Levee \"{LeveeName}\" could not identify levee properties \"{LeveePropertiesName}\".");

		public static readonly MessageClass LeveePropertiesNotSet = new MessageClass("Levee_PropertiesNotSet", MessageSeverity.Error, "Levee \"{LeveeName}\" has no levee properties assigned.");

		public static Message GetLeveePropertiesNotFound(string leveeName, string propertiesName)
		{
			return LeveePropertiesNotFound.Create(leveeName, propertiesName);
		}

		public static Message GetLeveePropertiesNotSet(string leveeName)
		{
			return LeveePropertiesNotSet.Create(leveeName);
		}
	}

	private VersionTracker _lineTracker;

	private Polyline _line;

	private long _internalVersion = 1L;

	private string _leveePropertyName;

	private VersionTracker _profileTracker;

	private StationElevationProfile _profile;

	public ILeveeModPropertiesLookup PropertiesSource { get; set; }

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; } = "Levee Modifications";

	[HashContributor]
	[Metadata("Levee Properties")]
	public string LeveePropertyName
	{
		get
		{
			return _leveePropertyName;
		}
		set
		{
			if (value != _leveePropertyName)
			{
				_leveePropertyName = value;
				_internalVersion++;
			}
		}
	}

	[HashContributor]
	[DatasetName("Line")]
	public Polyline Line
	{
		get
		{
			return _line;
		}
		set
		{
			_lineTracker.Assign(ref _line, value);
		}
	}

	[HashContributor]
	[DatasetName("Profile")]
	public StationElevationProfile Profile
	{
		get
		{
			return _profile;
		}
		set
		{
			_profileTracker.Assign(ref _profile, value);
		}
	}

	public Extent Extent => Line?.Extent ?? Extent.NullExtent;

	public long VersionXY => _internalVersion + _line.VersionXY + _profile.VersionXY;

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public bool TryEvaluateAssociations(out LeveeModProperties properties)
	{
		if (PropertiesSource == null)
		{
			properties = null;
			return false;
		}
		return PropertiesSource.TryFindByName(LeveePropertyName, out properties);
	}

	public LeveeModProperties EvaluateProperties()
	{
		if (TryEvaluateAssociations(out var properties))
		{
			return properties;
		}
		return LeveeModProperties.Default;
	}

	public OperationResult IsValid()
	{
		OperationResult operationResult = true;
		LeveeModProperties properties;
		if (string.IsNullOrEmpty(LeveePropertyName))
		{
			operationResult.AddFailure(MessageCodes.GetLeveePropertiesNotSet(Name));
		}
		else if (!TryEvaluateAssociations(out properties))
		{
			operationResult.AddFailure(MessageCodes.GetLeveePropertiesNotFound(Name, LeveePropertyName));
		}
		return operationResult;
	}

	public LeveeMod()
	{
	}

	public LeveeMod(Polyline line, StationElevationProfile profile)
	{
		Line = line;
		Profile = profile;
	}
}
