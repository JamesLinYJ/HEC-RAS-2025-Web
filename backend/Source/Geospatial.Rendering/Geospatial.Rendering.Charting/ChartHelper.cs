using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Utility.Systems;

namespace Geospatial.Rendering.Charting;

public static class ChartHelper
{
	public static void Serialize(IChartRequest req, BinaryWriter bw)
	{
		Pipe.WriteString(bw, req.Title);
		bw.Write(req.Series.Count);
		for (int i = 0; i < req.Series.Count; i++)
		{
			IList<ISeriesRequest> sery = req.Series[i];
			bw.Write(sery.Count);
			for (int j = 0; j < sery.Count; j++)
			{
				SerializeSeriesRequest(sery[j], bw);
			}
		}
	}

	public static IChartRequest Deserialize(BinaryReader br)
	{
		string title = Pipe.ReadString(br);
		int readInt32 = br.ReadInt32();
		ChartRequest chartRequest = new ChartRequest(title);
		for (int i = 0; i < readInt32; i++)
		{
			List<ISeriesRequest> iSeriesRequests = new List<ISeriesRequest>();
			chartRequest.Series.Add(iSeriesRequests);
			int readInt322 = br.ReadInt32();
			for (int j = 0; j < readInt322; j++)
			{
				ISeriesRequest deserializeSeriesRequest = DeserializeSeriesRequest(br);
				iSeriesRequests.Add(deserializeSeriesRequest);
			}
		}
		return chartRequest;
	}

	private static void SerializeSeriesRequest(ISeriesRequest series, BinaryWriter bw)
	{
		Pipe.WriteString(bw, series.Name);
		Pipe.WriteString(bw, series.XAxisLabel);
		Pipe.WriteString(bw, series.XAxisUnits);
		Pipe.WriteString(bw, series.YAxisLabel);
		Pipe.WriteString(bw, series.YAxisUnits);
		SerializeSymbology(series.Symbology, bw);
		if (series is XYSeriesRequest xYSeriesRequest)
		{
			Pipe.WriteString(bw, "XY");
			Pipe.WriteArray(bw, xYSeriesRequest.XData);
			Pipe.WriteArray(bw, xYSeriesRequest.YData);
			return;
		}
		if (series is TimeSeriesRequest timeSeriesRequest)
		{
			Pipe.WriteString(bw, "TS");
			Pipe.WriteArray(bw, timeSeriesRequest.Times);
			Pipe.WriteArray(bw, timeSeriesRequest.YData);
			return;
		}
		throw new Exception("Could not serialize " + series.GetType().ToString());
	}

	private static ISeriesRequest DeserializeSeriesRequest(BinaryReader br)
	{
		string name = Pipe.ReadString(br);
		string xLabel = Pipe.ReadString(br);
		string xUnits = Pipe.ReadString(br);
		string yLabel = Pipe.ReadString(br);
		string yUnits = Pipe.ReadString(br);
		VectorSymbology symbology = DeserializeSymbology(br);
		string readString = Pipe.ReadString(br);
		if (readString == "XY")
		{
			double[] xData = Pipe.ReadArray<double>(br);
			double[] yData = Pipe.ReadArray<double>(br);
			return new XYSeriesRequest(name, xLabel, xUnits, xData, yLabel, yUnits, yData)
			{
				Symbology = symbology
			};
		}
		if (readString == "TS")
		{
			DateTime[] times = Pipe.ReadArray<DateTime>(br);
			double[] yData2 = Pipe.ReadArray<double>(br);
			return new TimeSeriesRequest(name, yLabel, yUnits, times, yData2);
		}
		return null;
	}

	private static void SerializeSymbology(VectorSymbology symb, BinaryWriter bw)
	{
		SerializeColor(symb.PenColor, bw);
		bw.Write(symb.PenWidth);
		SerializeColor(symb.FillColor, bw);
	}

	private static VectorSymbology DeserializeSymbology(BinaryReader br)
	{
		Color penColor = DeserializeColor(br);
		double penWidth = br.ReadDouble();
		Color fillColor = DeserializeColor(br);
		return new VectorSymbology(penColor, penWidth, fillColor);
	}

	private static void SerializeColor(Color color, BinaryWriter bw)
	{
		bw.Write(color.R);
		bw.Write(color.G);
		bw.Write(color.B);
	}

	private static Color DeserializeColor(BinaryReader br)
	{
		byte red = br.ReadByte();
		byte green = br.ReadByte();
		byte blue = br.ReadByte();
		return Color.FromArgb(red, green, blue);
	}
}
