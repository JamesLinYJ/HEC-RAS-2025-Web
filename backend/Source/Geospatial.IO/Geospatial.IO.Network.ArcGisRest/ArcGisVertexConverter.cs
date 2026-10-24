using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Vectors;

namespace Geospatial.IO.Network.ArcGisRest;

public sealed class ArcGisVertexConverter : JsonConverter<Point3D>
{
	public override Point3D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
		{
			throw new JsonException("Expected an ArcGIS vertex as a JSON array.");
		}
		double x = ReadNumber(ref reader);
		double y = ReadNumber(ref reader);
		double z = double.NaN;
		reader.Read();
		if (reader.TokenType == JsonTokenType.Number)
		{
			z = reader.GetDouble();
			reader.Read();
		}
		while (reader.TokenType != JsonTokenType.EndArray)
		{
			reader.Read();
		}
		return new Point3D(x, y, z);
	}

	private static double ReadNumber(ref Utf8JsonReader reader)
	{
		reader.Read();
		if (reader.TokenType != JsonTokenType.Number)
		{
			throw new JsonException("Expected a number in an ArcGIS vertex array.");
		}
		return reader.GetDouble();
	}

	public override void Write(Utf8JsonWriter writer, Point3D value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.X);
		writer.WriteNumberValue(value.Y);
		if (!double.IsNaN(value.Z))
		{
			writer.WriteNumberValue(value.Z);
		}
		writer.WriteEndArray();
	}
}
