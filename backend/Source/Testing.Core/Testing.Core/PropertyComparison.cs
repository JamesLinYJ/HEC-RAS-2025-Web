using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testing.Core;

public class PropertyComparison
{
	private object _value;

	[JsonPropertyName("property")]
	public string PropertyName { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter<CompareType>))]
	[JsonPropertyName("comparison")]
	public CompareType CompareType { get; set; }

	[JsonIgnore]
	public char CompareSign => CompareType switch
	{
		CompareType.GreaterThan => '>', 
		CompareType.LessThan => '<', 
		CompareType.EqualTo => '=', 
		_ => '=', 
	};

	public object Value
	{
		get
		{
			return _value;
		}
		set
		{
			if (value is JsonElement jsonElement)
			{
				switch (jsonElement.ValueKind)
				{
				case JsonValueKind.Number:
				{
					double computed;
					if (jsonElement.TryGetInt32(out var localValue))
					{
						_value = localValue;
					}
					else if (jsonElement.TryGetDouble(out computed))
					{
						_value = computed;
					}
					else
					{
						_value = null;
					}
					break;
				}
				case JsonValueKind.String:
					_value = jsonElement.GetString();
					break;
				case JsonValueKind.True:
					_value = true;
					break;
				case JsonValueKind.False:
					_value = false;
					break;
				}
			}
			else
			{
				_value = value;
			}
		}
	}

	public PropertyComparison(string propertyName, CompareType compareType, IComparable iComparable)
	{
		PropertyName = propertyName;
		CompareType = compareType;
		Value = iComparable;
	}

	public PropertyComparison()
	{
	}
}
