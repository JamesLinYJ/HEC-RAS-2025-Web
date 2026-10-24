using System;
using Utility.Files;

namespace Ras.Layers.Cleanup;

public static class CleanupPolicyDefaults
{
	public static class Geometry
	{
		public static readonly TimeSpan KeepAll = TimeSpan.FromDays(7);

		public static readonly TimeSpan Hourly = TimeSpan.FromDays(30);

		public static readonly TimeSpan Daily = TimeSpan.FromDays(365);

		public static CleanupPolicy Default => new CleanupPolicy
		{
			KeepAll = KeepAll,
			KeepHourly = Hourly,
			KeepDaily = Daily
		};
	}

	public static class Plans
	{
		public static readonly TimeSpan KeepAll = TimeSpan.FromDays(7);

		public static readonly TimeSpan Hourly = TimeSpan.FromDays(30);

		public static readonly TimeSpan Daily = TimeSpan.FromDays(365);

		public static CleanupPolicy Default => new CleanupPolicy
		{
			KeepAll = KeepAll,
			KeepHourly = Hourly,
			KeepDaily = Daily
		};
	}

	public static class Results
	{
		public static readonly TimeSpan KeepAll = TimeSpan.FromDays(3);

		public static readonly TimeSpan Hourly = TimeSpan.FromDays(14);

		public static readonly TimeSpan Daily = TimeSpan.FromDays(90);

		public static bool CleanAuxiliaryFiles => true;

		public static CleanupPolicy Default => new CleanupPolicy
		{
			KeepAll = KeepAll,
			KeepHourly = Hourly,
			KeepDaily = Daily
		};
	}
}
