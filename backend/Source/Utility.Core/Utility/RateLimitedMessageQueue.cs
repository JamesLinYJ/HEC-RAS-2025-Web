using System.Collections.Generic;

namespace Utility;

public class RateLimitedMessageQueue
{
	public Dictionary<string, List<string>> _messageQueue;

	public Dictionary<string, string> _messages;

	public RateLimitedMessageQueue()
	{
		_messageQueue = new Dictionary<string, List<string>>();
		_messages = new Dictionary<string, string>();
	}

	public void QueueMessage(string queueName, string msg)
	{
		lock (_messageQueue)
		{
			List<string> messageQueue = null;
			if (_messageQueue.TryGetValue(queueName, out messageQueue))
			{
				messageQueue.Add(msg);
				return;
			}
			messageQueue = new List<string>();
			_messageQueue[queueName] = messageQueue;
			messageQueue.Add(msg);
		}
	}

	public void AddNewQueue(string queueName, bool recentMessageSupercedesAllPrior)
	{
	}
}
