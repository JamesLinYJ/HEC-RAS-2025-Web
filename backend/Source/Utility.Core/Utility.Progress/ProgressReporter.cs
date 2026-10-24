using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Logging;

namespace Utility.Progress;

public class ProgressReporter : IProgress<int>
{
	public const LogLevel DefaultLogLevel = LogLevel.Information;

	public const string DefaultTaskName = "Task";

	private bool _allowMutation = true;

	private int _prevProg = int.MinValue;

	private bool _cancelRequested;

	private CompactList<ProgressReporter> _childProgressReporters;

	private readonly IProgressTarget _target;

	private static readonly ProgressReporter _nullProg = CreateImmutableInstance();

	public bool IsMutable => _allowMutation;

	public int MostRecentProgress => _prevProg;

	public int ProgressOffset { get; init; }

	public float ProgressScale { get; init; } = 1f;

	public int TaskLevel
	{
		get
		{
			if (Parent != null)
			{
				return Parent.TaskLevel + 1;
			}
			return 0;
		}
	}

	public string TaskName { get; }

	public string FullyQualifiedTaskName { get; }

	public LogLevel MinLogLevel { get; set; }

	public bool CancelRequested => _cancelRequested;

	public ProgressReporter Parent { get; }

	public IProgressTarget Target
	{
		get
		{
			IProgressTarget target = _target;
			if (target == null)
			{
				ProgressReporter parent = Parent;
				if (parent == null)
				{
					return null;
				}
				target = parent.Target;
			}
			return target;
		}
	}

	public Func<string, string> MessageModifier { get; set; }

	public event ProgressReportedEventHandler ProgressReported;

	public event MessageReportedEventHandler MessageReported;

	public int GetFullScaledProgress(int baseProgress)
	{
		int localScaledProgress = GetLocalScaledProgress(baseProgress);
		if (Parent == null)
		{
			return localScaledProgress;
		}
		return Parent.GetFullScaledProgress(localScaledProgress);
	}

	public int GetLocalScaledProgress(int baseProgress)
	{
		return (int)((float)ProgressOffset + (float)baseProgress * ProgressScale);
	}

	public string ModifyMessage(string baseMessage)
	{
		Func<string, string> messageModifier = MessageModifier;
		if (messageModifier == null)
		{
			return baseMessage;
		}
		string messageModifier2 = messageModifier(baseMessage);
		if (Parent != null)
		{
			messageModifier2 = Parent.ModifyMessage(messageModifier2);
		}
		return messageModifier2;
	}

	public ProgressReporter()
		: this(null, null, "Task")
	{
	}

	public ProgressReporter(IProgressTarget target)
		: this(null, target, "Task")
	{
	}

	public ProgressReporter(ProgressReporter parent)
		: this(parent, null, "Task")
	{
	}

	private ProgressReporter(ProgressReporter parent, IProgressTarget target, string taskName, LogLevel logLevel = LogLevel.Information)
	{
		Parent = parent;
		_target = target;
		TaskName = taskName;
		MinLogLevel = logLevel;
	}

	void IProgress<int>.Report(int ordinal)
	{
		ReportProgress(ordinal);
	}

	public void ReportProgress(int prog)
	{
		if (_allowMutation && _prevProg != prog)
		{
			_prevProg = prog;
			int fullScaledProgress = GetFullScaledProgress(prog);
			Target?.WriteProgress(this, prog, fullScaledProgress);
			RaiseProg(fullScaledProgress);
		}
	}

	private void RaiseProg(int scaledProg)
	{
		ProgressReported?.Invoke(this, scaledProg);
		Parent?.RaiseProg(scaledProg);
	}

	public void ReportProgressFraction(float prog)
	{
		ReportProgress((int)(100f * prog));
	}

	public void ReportProgressFraction(int numerator, int denominator)
	{
		ReportProgress((int)(100f * ((float)numerator / (float)denominator)));
	}

	public void ReportMessage(string msg, params object[] data)
	{
		ReportMessage(msg, LogLevel.Information, data);
	}

	public void ReportMessage(string msg, LogLevel level, params object[] data)
	{
		if (_allowMutation && level >= MinLogLevel)
		{
			msg = ModifyMessage(msg);
			ReportMessage(new Message(msg, data));
		}
	}

	public void ReportMessage(Message msg, LogLevel level = LogLevel.Information)
	{
		if (_allowMutation && level >= MinLogLevel)
		{
			Target?.WriteMessage(this, msg);
			RaiseMessage(msg);
		}
	}

	private void RaiseMessage(Message msg)
	{
		MessageReported?.Invoke(this, msg);
		Parent?.RaiseMessage(msg);
	}

	public void ReportMessages(OperationResult res)
	{
		foreach (Message message in res.Messages)
		{
			ReportMessage(message);
		}
	}

	public void ReportOperationCancelled()
	{
		ReportMessage("Operation cancelled.");
	}

	public void ReportTaskCompleted(TimeSpan ts)
	{
		ReportMessage(TaskName.InSingleQuotes() + " completed in " + ts.ToPrettyString());
	}

	public ProgressReporter WithProgressModifier(int startProgressValue, float fractionOfTotal)
	{
		return SubTask("Task", (float)startProgressValue / 100f, fractionOfTotal);
	}

	public ProgressReporter WithMessageModifier(string prefix, string suffix)
	{
		if (!_allowMutation)
		{
			return this;
		}
		if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith(" "))
		{
			prefix += " ";
		}
		if (!string.IsNullOrEmpty(suffix) && !suffix.StartsWith(" "))
		{
			suffix = " " + suffix;
		}
		ProgressReporter progressReporter = new ProgressReporter(this, null, "Task", MinLogLevel)
		{
			MessageModifier = (string msg) => prefix + msg + suffix
		};
		_childProgressReporters.Add(progressReporter);
		return progressReporter;
	}

	public ProgressReporter SubTask(string task, float startProg, float fractionOfTotal)
	{
		if (!_allowMutation)
		{
			return this;
		}
		int progressOffset = (int)(startProg * 100f);
		ProgressReporter progressReporter = new ProgressReporter(this, null, task, MinLogLevel)
		{
			ProgressOffset = progressOffset,
			ProgressScale = fractionOfTotal
		};
		if (CancelRequested)
		{
			progressReporter.RequestCancel();
		}
		_childProgressReporters.Add(progressReporter);
		return progressReporter;
	}

	public void ReportError(string msg)
	{
		ReportMessage(msg, LogLevel.Error);
	}

	public void ReportWarning(string msg)
	{
		ReportMessage(msg, LogLevel.Warning);
	}

	public ProgressReporter LimitProgress(int everyNPercent)
	{
		if (!_allowMutation)
		{
			return this;
		}
		throw new NotImplementedException();
	}

	public void RequestCancel()
	{
		if (!_allowMutation)
		{
			return;
		}
		_cancelRequested = true;
		foreach (ProgressReporter childProgressReporter in _childProgressReporters)
		{
			childProgressReporter.RequestCancel();
		}
	}

	public void ClearCancellationRequest()
	{
		if (_allowMutation)
		{
			_cancelRequested = false;
		}
	}

	public static ProgressReporter None()
	{
		return _nullProg;
	}

	private static ProgressReporter CreateImmutableInstance()
	{
		return new ProgressReporter
		{
			_allowMutation = false
		};
	}

	public static ProgressReporter ConsoleWrite(bool progressOverwritesLine = true)
	{
		return new ProgressReporter(new ConsoleProgressTarget(progressOverwritesLine, tabbedTasks: true));
	}

	public static ProgressReporter ConsoleAndFile(string filePath, bool progressOverwritesLine = false)
	{
		return new ProgressReporter(new CompositeProgressTarget(new ConsoleProgressTarget(progressOverwritesLine, tabbedTasks: true), new FileProgressTarget(filePath)));
	}

	public static ProgressReporter DebugPrint()
	{
		return new ProgressReporter(new DebugProgressTarget());
	}

	public static ProgressReporter BuildString(StringBuilder sb)
	{
		return new ProgressReporter(new StringBuilderTarget(sb));
	}
}
