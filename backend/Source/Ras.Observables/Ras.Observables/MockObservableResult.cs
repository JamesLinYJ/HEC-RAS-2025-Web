using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Utility.Logging;

namespace Ras.Observables;

public class MockObservableResult
{
	public static class MessageCodes
	{
		public static readonly MessageClass BadMeshCellArea = new MessageClass("Mesh_BadCellArea", MessageSeverity.Error, "Mesh {MeshName} has cells with zero/negative area. ({CellIndex})");

		public static readonly MessageClass InformationalComputeMessage = new MessageClass("FYI", MessageSeverity.Information, "Don't worry about your face, mine looks like that too.");

		public static readonly MessageClass WarningComputeMessage = new MessageClass("Warn", MessageSeverity.Warning, "Your mesh {MeshName} isn't wrong, but I don't like it. Try again ({CellIndex})");
	}

	private ObservableCollection<Message> messages = new ObservableCollection<Message>();

	private string _name = "RelentlessMessenger";

	public ObservableCollection<Message> Messages => messages;

	public string Name => _name;

	public int Progress { get; set; }

	public async Task<bool> StartReportingProgress()
	{
		for (int i = 0; i < 100; i++)
		{
			Progress = i;
			await Task.Delay(Random.Shared.Next(100, 1000));
		}
		return true;
	}

	public async Task<bool> StartMessaging(int numMessages)
	{
		for (int i = 0; i < numMessages; i++)
		{
			Message informationalComputeMessage = new Message(MessageCodes.InformationalComputeMessage);
			messages.Add(informationalComputeMessage);
			if (i % 1 == 0)
			{
				await Task.Delay(Random.Shared.Next(250, 2000));
			}
		}
		return true;
	}
}
