using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Utility.Systems;

namespace PipeClient;

public static class GenerateMesh
{
	public static MeshOutputs Launch(string pipeName, MeshInputs args)
	{
		return Client.StartRemoteServer(pipeName, "GenerateMesh", (NamedPipeClientStream client) => Compute(client, args));
	}

	private static MeshOutputs Compute(NamedPipeClientStream client, MeshInputs args)
	{
		MeshOutputs meshOutputs = new MeshOutputs();
		using (BinaryReader binaryReader = new BinaryReader(client, Encoding.Default, leaveOpen: true))
		{
			using BinaryWriter binaryWriter = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
			Pipe.WriteString(binaryWriter, args.GeomFilename);
			Pipe.WriteString(binaryWriter, args.GeomTitle);
			Pipe.WriteString(binaryWriter, args.MeshName);
			Pipe.WriteString(binaryWriter, args.MeshDate);
			binaryWriter.Write(args.ProgXLocation);
			binaryWriter.Write(args.ProgYLocation);
			binaryWriter.Write(Convert.ToInt32(args.IsSIUnits));
			Pipe.WriteArray(binaryWriter, args.PerimeterX);
			Pipe.WriteArray(binaryWriter, args.PerimeterY);
			Pipe.WriteArray(binaryWriter, args.BreaklinesStartIndex);
			Pipe.WriteArray(binaryWriter, args.BreaklineX);
			Pipe.WriteArray(binaryWriter, args.BreaklineY);
			binaryWriter.Write(args.CellCount);
			Pipe.WriteArray(binaryWriter, args.CellX);
			Pipe.WriteArray(binaryWriter, args.CellY);
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				client.WaitForPipeDrain();
			}
			try
			{
				meshOutputs.CellFacesCount = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.CellFacesStartIndex = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.CellFaces = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FacePointX = Pipe.ReadArray<double>(binaryReader);
				meshOutputs.FacePointY = Pipe.ReadArray<double>(binaryReader);
				meshOutputs.FacePointIsPerim = Pipe.ReadArrayBool(binaryReader);
				meshOutputs.FaceFPA = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FaceFPB = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FaceCellA = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FaceCellB = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FaceIsPerimeter = Pipe.ReadArray<bool>(binaryReader);
				meshOutputs.FaceInternalStartIndex = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FaceInternalCount = Pipe.ReadArray<int>(binaryReader);
				meshOutputs.FaceInternalX = Pipe.ReadArray<double>(binaryReader);
				meshOutputs.FaceInternalY = Pipe.ReadArray<double>(binaryReader);
				meshOutputs.CellMaxIndex = binaryReader.ReadInt32();
				meshOutputs.CellMaxSize = binaryReader.ReadSingle();
				meshOutputs.CellMinSize = binaryReader.ReadSingle();
				meshOutputs.CellAvgSize = binaryReader.ReadSingle();
				meshOutputs.Status = binaryReader.ReadInt32();
				meshOutputs.StatusMsg = Pipe.ReadString(binaryReader);
				meshOutputs.BadPtCount = binaryReader.ReadInt32();
				if (meshOutputs.BadPtCount != 0)
				{
					meshOutputs.BadPoints = Pipe.ReadArray<int>(binaryReader);
				}
			}
			catch (OutOfMemoryException)
			{
				meshOutputs = new MeshOutputs();
				meshOutputs.Status = -1;
				meshOutputs.StatusMsg = "System ran out of memory.";
			}
		}
		return meshOutputs;
	}
}
