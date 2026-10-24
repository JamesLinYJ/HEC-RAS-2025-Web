using System;
using System.Diagnostics;
using Geospatial.Vectors;
using Utility;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Geospatial.Rasters.Modifiers;

[DebuggerDisplay("{Name}")]
public class LeveeModProperties : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, ICloneable
{
	private long _internalVersion = 1L;

	private string _name;

	private double _topWidth = 50.0;

	private double _leftGrade = 4.0;

	private double _rightGrade = double.NaN;

	private double _leftWidth = 100.0;

	private double _rightWidth = double.NaN;

	private LeveeMode _mode;

	private RasterModType _modType = RasterModType.TakeHigher;

	public static LeveeModProperties Default { get; } = new LeveeModProperties();

	[HashContributor]
	[Metadata("Name")]
	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (value != _name)
			{
				_name = value;
				_internalVersion++;
			}
		}
	}

	[HashContributor]
	[Metadata("TopWidth")]
	public double TopWidth
	{
		get
		{
			return _topWidth;
		}
		set
		{
			if (!value.Equals(_topWidth))
			{
				_topWidth = value;
				_internalVersion++;
			}
		}
	}

	[HashContributor]
	[Metadata("LeftGrade")]
	public double LeftGrade
	{
		get
		{
			return _leftGrade;
		}
		set
		{
			if (!value.Equals(_leftGrade))
			{
				_leftGrade = value;
				_internalVersion++;
			}
		}
	}

	public double EffectiveLeftGrade => LeftGrade;

	[HashContributor]
	[Metadata("RightGrade", Optional = true)]
	public double RightGrade
	{
		get
		{
			return _rightGrade;
		}
		set
		{
			if (!value.Equals(_rightGrade))
			{
				_rightGrade = value;
				_internalVersion++;
			}
		}
	}

	public double EffectiveRightGrade
	{
		get
		{
			if (!double.IsNaN(RightGrade))
			{
				return RightGrade;
			}
			return LeftGrade;
		}
	}

	[HashContributor]
	[Metadata("LeftWidth", LegacyKeyNames = new string[] { "SideWidth" })]
	public double LeftWidth
	{
		get
		{
			return _leftWidth;
		}
		set
		{
			if (!value.Equals(_leftWidth))
			{
				_leftWidth = value;
				_internalVersion++;
			}
		}
	}

	public double EffectiveLeftWidth => LeftWidth;

	[HashContributor]
	[Metadata("RightWidth", Optional = true)]
	public double RightWidth
	{
		get
		{
			return _rightWidth;
		}
		set
		{
			if (!value.Equals(_rightWidth))
			{
				_rightWidth = value;
				_internalVersion++;
			}
		}
	}

	public double EffectiveRightWidth
	{
		get
		{
			if (!double.IsNaN(RightWidth))
			{
				return RightWidth;
			}
			return LeftWidth;
		}
	}

	[HashContributor]
	[Metadata("Mode")]
	public LeveeMode Mode
	{
		get
		{
			return _mode;
		}
		set
		{
			if (value != _mode)
			{
				_mode = value;
				_internalVersion++;
			}
		}
	}

	[HashContributor]
	[Metadata("ModType")]
	public RasterModType ModType
	{
		get
		{
			return _modType;
		}
		set
		{
			if (value != _modType)
			{
				_modType = value;
				_internalVersion++;
			}
		}
	}

	public Extent Extent => Extent.NullExtent;

	public long VersionXY => _internalVersion;

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public OperationResult IsValid()
	{
		OperationResult operationResult = true;
		if (string.IsNullOrEmpty(Name))
		{
			operationResult.AddFailure("A simple cut/fill property set has no name, so nothing can reference it.");
		}
		return operationResult;
	}

	public object Clone()
	{
		return new LeveeModProperties
		{
			Name = Name,
			TopWidth = TopWidth,
			LeftGrade = LeftGrade,
			RightGrade = RightGrade,
			LeftWidth = LeftWidth,
			RightWidth = RightWidth,
			Mode = Mode,
			ModType = ModType
		};
	}

	public OperationResult DataEquals(LeveeModProperties other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("LeveeModProperties hash mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "Name", Name, other.Name));
		}
		if (!TopWidth.Equals(other.TopWidth))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "TopWidth", TopWidth, other.TopWidth));
		}
		if (!LeftGrade.Equals(other.LeftGrade))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "LeftGrade", LeftGrade, other.LeftGrade));
		}
		if (!RightGrade.Equals(other.RightGrade))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "RightGrade", RightGrade, other.RightGrade));
		}
		if (!LeftWidth.Equals(other.LeftWidth))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "LeftWidth", LeftWidth, other.LeftWidth));
		}
		if (!RightWidth.Equals(other.RightWidth))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "RightWidth", RightWidth, other.RightWidth));
		}
		if (Mode != other.Mode)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "Mode", Mode, other.Mode));
		}
		if (ModType != other.ModType)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("LeveeModProperties", "ModType", ModType, other.ModType));
		}
		return operationResult;
	}
}
