using System;
using System.IO;
using Utility.Extensions;
using Utility.Systems;

namespace RasMapperLib;

public class PlanAttributes : IStreamableObject
{
	public string PlanFilename;

	public string PlanTitle;

	public string PlanShortID;

	public string GeometryFilename;

	public string GeometryTitle;

	public string FlowFilename;

	public string FlowTitle;

	public string StartTimeString;

	public string EndTimeString;

	public DateTime StartTime;

	public DateTime EndTime;

	public string ComputeTimestepBaseString;

	public float ComputeTimestepBaseSeconds;

	public string OutputTimestepBaseString;

	public float OutputTimestepBaseSeconds;

	public PlanAttributes()
	{
		PlanFilename = "";
		PlanTitle = "";
		PlanShortID = "";
		GeometryFilename = "";
		GeometryTitle = "";
		FlowFilename = "";
		FlowTitle = "";
		StartTimeString = "";
		EndTimeString = "";
		ComputeTimestepBaseString = "";
		OutputTimestepBaseString = "";
	}

	public void StreamOut(BinaryWriter bw)
	{
		bw.WriteStringA(PlanFilename);
		bw.WriteStringA(PlanTitle);
		bw.WriteStringA(PlanShortID);
		bw.WriteStringA(GeometryFilename);
		bw.WriteStringA(GeometryTitle);
		bw.WriteStringA(FlowFilename);
		bw.WriteStringA(FlowTitle);
		bw.WriteStringA(StartTimeString);
		bw.WriteStringA(EndTimeString);
		bw.Write(StartTime);
		bw.Write(EndTime);
		bw.WriteStringA(ComputeTimestepBaseString);
		bw.Write(ComputeTimestepBaseSeconds);
		bw.WriteStringA(OutputTimestepBaseString);
		bw.Write(OutputTimestepBaseSeconds);
	}

	void IStreamableObject.StreamOut(BinaryWriter bw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamOut
		this.StreamOut(bw);
	}

	public void StreamIn(BinaryReader br)
	{
		PlanFilename = br.ReadStringA();
		PlanTitle = br.ReadStringA();
		PlanShortID = br.ReadStringA();
		GeometryFilename = br.ReadStringA();
		GeometryTitle = br.ReadStringA();
		FlowFilename = br.ReadStringA();
		FlowTitle = br.ReadStringA();
		StartTimeString = br.ReadStringA();
		EndTimeString = br.ReadStringA();
		StartTime = br.ReadDate();
		EndTime = br.ReadDate();
		ComputeTimestepBaseString = br.ReadStringA();
		ComputeTimestepBaseSeconds = br.ReadSingle();
		OutputTimestepBaseString = br.ReadStringA();
		OutputTimestepBaseSeconds = br.ReadSingle();
	}

	void IStreamableObject.StreamIn(BinaryReader br)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamIn
		this.StreamIn(br);
	}
}
