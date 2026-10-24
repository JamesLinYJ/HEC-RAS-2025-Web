using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Utility.Systems;

namespace PipeClient;

public static class ImportMetData
{
	public static MetImporterOutputs Launch(string pipeName, MetImporterInputs args)
	{
		return Client.StartRemoteServer(pipeName, "ImportMetRasterData", (NamedPipeClientStream client) => Compute(client, args));
	}

	private static MetImporterOutputs Compute(NamedPipeClientStream client, MetImporterInputs args)
	{
		new MetImporterOutputs();
		using BinaryReader binaryReader = new BinaryReader(client, Encoding.Default, leaveOpen: true);
		using BinaryWriter writer = new BinaryWriter(client, Encoding.Default, leaveOpen: true);
		Pipe.WriteString(writer, args.OutputFilename);
		Pipe.WriteString(writer, args.OutputDataset);
		Pipe.WriteString(writer, args.Variable);
		Pipe.WriteStringArray(writer, args.CandidateFiles);
		Pipe.WriteString(writer, args.SubDatasetName);
		Pipe.WriteString(writer, args.Title);
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			client.WaitForPipeDrain();
		}
		try
		{
			return new MetImporterOutputs
			{
				Success = binaryReader.ReadBoolean(),
				Summary = binaryReader.ReadString()
			};
		}
		catch (Exception ex)
		{
			return new MetImporterOutputs
			{
				Success = false,
				Summary = ex.Message
			};
		}
	}
}
