using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;
using Utility.Processes;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public abstract class Command
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__21_002D0
	{
		public string tempFn;

		[SpecialName]
		internal void _Lambda_0024__R1(Task a0)
		{
			_Lambda_0024__0();
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			try
			{
				File.Delete(tempFn);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	public const string XMLTag = "Command";

	private const string XMLTypeTag = "Type";

	public static string RASProcessEXE;

	private static Dictionary<string, Type> _commandNameMap;

	public const string ArgCommandFile = "-CommandFile=";

	public const string ArgCommand = "-Command=";

	public const string ArgDebug = "-debug";

	public abstract string CommandName { get; }

	public abstract bool ValidateArguments(ProgressReporter prog = null);

	public abstract void Execute(ProgressReporter prog = null);

	public abstract void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null);

	protected abstract void XMLSaveTags(XElement ele);

	protected abstract void XMLLoadTags(XElement ele);

	static Command()
	{
		RefreshCommandTypeList();
		RASProcessEXE = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "RasProcess.exe");
	}

	public static void ExecuteWith(string cmdName, Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		Command command = TryConstructFromCommandName(cmdName);
		if (command == null)
		{
			prog.ReportError("Could not find command " + cmdName.InDoubleQuotes());
		}
		else
		{
			command.ExecuteWith(kvpArgs, otherArgs, prog);
		}
	}

	public void ExecuteOutOfProcess(bool showWindow, ProgressReporter prog = null)
	{
		XElement xElement = XMLSave();
		string tempFileName = Path.GetTempFileName();
		xElement.Save(tempFileName);
		ProcessHelper.Run(RASProcessEXE, MakeCommandFileArgs(tempFileName), showWindow);
		try
		{
			File.Delete(tempFileName);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	public static void ParseExecute(string[] args, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		checked
		{
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i].Equals("-debug", StringComparison.InvariantCultureIgnoreCase))
				{
					SharedData.DebugMode = true;
				}
			}
			foreach (string textValue in args)
			{
				if (!textValue.StartsWith("-CommandFile=", StringComparison.InvariantCultureIgnoreCase))
				{
					continue;
				}
				string substring = textValue.Substring("-CommandFile=".Length);
				if (File.Exists(substring))
				{
					Command command = XMLLoad(substring);
					if (command == null)
					{
						throw new Exception("Command file could not be parsed.");
					}
					command.Execute(ProgressReporter.ConsoleWrite());
					return;
				}
				throw new Exception("File not found: " + substring);
			}
			string textValue2 = "";
			Dictionary<string, string> stringByString = new Dictionary<string, string>();
			List<string> stringList = new List<string>();
			foreach (string textValue3 in args)
			{
				if (textValue3.StartsWith("-Command=", StringComparison.InvariantCultureIgnoreCase))
				{
					textValue2 = textValue3.Substring("-Command=".Length);
				}
				else if (textValue3.Contains("="))
				{
					string[] parts = textValue3.Split(new char[1] { '=' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts == null || parts.Length != 2)
					{
						throw new NotImplementedException();
					}
					string part = parts[0];
					if (part.StartsWith("-"))
					{
						part = part.Substring(1);
					}
					string part2 = parts[1];
					if (part2.StartsWith("\""))
					{
						part2 = part2.Substring(1);
					}
					if (part2.EndsWith("\""))
					{
						part2 = part2.Substring(0, part2.Length - 1);
					}
					stringByString[part] = part2;
				}
				else
				{
					stringList.Add(textValue3);
				}
			}
			if (string.IsNullOrEmpty(textValue2))
			{
				if (args.First().Contains("="))
				{
					throw new Exception("Command not found.");
				}
				textValue2 = args.First();
				stringList.Remove(textValue2);
			}
			ExecuteWith(textValue2, stringByString, stringList, prog);
		}
	}

	public static void ParseExecute(string cmdLineArgs, ProgressReporter prog = null)
	{
		bool condition = false;
		ParseExecute((from arg in cmdLineArgs.Split([SpecialName] (char c) =>
			{
				if (Operators.CompareString(Conversions.ToString(c), "\"", TextCompare: false) == 0)
				{
					condition = !condition;
				}
				return !condition && Operators.CompareString(Conversions.ToString(c), " ", TextCompare: false) == 0;
			})
			select arg.Trim().TrimMatchingQuotes('"') into arg
			where !string.IsNullOrEmpty(arg)
			select arg).ToArray(), prog);
	}

	public static string MakeCommandFileArgs(string filename)
	{
		return "-CommandFile=" + filename.InDoubleQuotes();
	}

	public Task ExecuteOutOfProcessAsync(ProgressReporter prog = null)
	{
		_Closure_0024__21_002D0 capturedLocals = new _Closure_0024__21_002D0();
		XElement xElement = XMLSave();
		capturedLocals.tempFn = Path.GetTempFileName();
		xElement.Save(capturedLocals.tempFn);
		Task<Process> task = ProcessHelper.RunAsync(RASProcessEXE, MakeCommandFileArgs(capturedLocals.tempFn));
		task.ContinueWith([SpecialName] (Task a0) =>
		{
			capturedLocals._Lambda_0024__0();
		});
		return task;
	}

	public XElement XMLSave()
	{
		XElement xElement = new XElement("Command");
		xElement.SetAttributeValue("Type", CommandName);
		XMLSaveTags(xElement);
		return xElement;
	}

	public static Command XMLLoad(string file)
	{
		return XMLLoad(XElement.Load(file));
	}

	public static Command XMLLoad(XElement el)
	{
		XAttribute xAttribute = el.Attribute("Type");
		if (xAttribute == null)
		{
			return null;
		}
		string xAttributeTerm = xAttribute.Value;
		if (string.IsNullOrEmpty(xAttributeTerm))
		{
			return null;
		}
		Command command = TryConstructFromCommandName(xAttributeTerm);
		if (command == null)
		{
			return null;
		}
		command.XMLLoadTags(el);
		return command;
	}

	public static void RefreshCommandTypeList()
	{
		_commandNameMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
		List<Type> selectMany = AppDomain.CurrentDomain.GetAssemblies().SelectMany([SpecialName] (Assembly asm) => from t in asm.GetTypes()
			where t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Command))
			select t).ToList();
		foreach (Type selectMany2 in selectMany)
		{
			Command command = ConstructEmpty(selectMany2);
			_commandNameMap[command.CommandName] = selectMany2;
		}
	}

	private static Command ConstructEmpty(Type cmdType)
	{
		return ((cmdType.GetConstructor(Type.EmptyTypes) ?? throw new Exception("Command type " + cmdType.FullName + " needs an explicit empty constructor.")).Invoke(new object[0]) as Command) ?? throw new Exception("Command type " + cmdType.FullName + " could not be constructed with an explicit empty constructor.");
	}

	public static Command TryConstructFromCommandName(string cmdName)
	{
		Type localValue = null;
		if (_commandNameMap.TryGetValue(cmdName, out localValue))
		{
			return ConstructEmpty(localValue);
		}
		return null;
	}
}
