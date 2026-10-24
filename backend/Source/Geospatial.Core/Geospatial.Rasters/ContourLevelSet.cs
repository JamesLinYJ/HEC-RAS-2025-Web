using System;
using System.Collections.Generic;
using Utility;

namespace Geospatial.Rasters;

public static class ContourLevelSet
{
	public enum HumanLadderFamily
	{
		FiveChain,
		TwoChain
	}

	public const int CliffValveMaxLevels = 512;

	public static double NiceInterval(double target)
	{
		return NiceNumbers.Ceiling125(target);
	}

	public static double NiceIntervalAtMost(double target)
	{
		return NiceNumbers.Floor125(target);
	}

	public static double HumanInterval(double target)
	{
		return HumanInterval(target, HumanLadderFamily.FiveChain);
	}

	public static double HumanInterval(double target, HumanLadderFamily family)
	{
		if (!(target > 0.0) || double.IsInfinity(target))
		{
			return 1.0;
		}
		double y = Math.Floor(Math.Log10(target));
		double computed = Math.Pow(10.0, y);
		double targetTerm = target / computed;
		double unity;
		if (family != HumanLadderFamily.TwoChain)
		{
			if (targetTerm < 2.2360679775)
			{
				unity = 1.0;
			}
			else
			{
				unity = ((targetTerm < 7.0710678119) ? 5.0 : 10.0);
			}
		}
		else if (targetTerm < 1.4142135624)
		{
			unity = 1.0;
		}
		else
		{
			unity = ((targetTerm < 4.472135955) ? 2.0 : 10.0);
		}
		return unity * computed;
	}

	public static double HumanIntervalBelow(double ladderValue, HumanLadderFamily family = HumanLadderFamily.FiveChain)
	{
		if (!(ladderValue > 0.0) || double.IsInfinity(ladderValue))
		{
			return 1.0;
		}
		double y = Math.Floor(Math.Log10(ladderValue) + 1E-09);
		double computed = Math.Pow(10.0, y);
		double ladderValueTerm = ladderValue / computed;
		double familyTwoChainTerm = ((family == HumanLadderFamily.TwoChain) ? 2.0 : 5.0);
		if (!(ladderValueTerm >= familyTwoChainTerm))
		{
			return familyTwoChainTerm / 10.0 * computed;
		}
		return computed;
	}

	public static HumanLadderFamily ChooseLadderFamily(double layerRange)
	{
		if (!(layerRange > 0.0) || double.IsInfinity(layerRange))
		{
			return HumanLadderFamily.FiveChain;
		}
		double scaledLayerRange = layerRange / 4.0;
		double humanInterval = HumanInterval(scaledLayerRange, HumanLadderFamily.FiveChain);
		double humanInterval2 = HumanInterval(scaledLayerRange, HumanLadderFamily.TwoChain);
		double humanIntervalTerm = Math.Max(humanInterval / scaledLayerRange, scaledLayerRange / humanInterval);
		if (!(Math.Max(humanInterval2 / scaledLayerRange, scaledLayerRange / humanInterval2) < humanIntervalTerm))
		{
			return HumanLadderFamily.FiveChain;
		}
		return HumanLadderFamily.TwoChain;
	}

	public static double WalkDownToFit(double interval, double valueRange, int minLevels)
	{
		if (!(valueRange > 0.0) || minLevels < 1 || !(interval > 0.0))
		{
			return interval;
		}
		if (interval * (double)minLevels <= valueRange)
		{
			return interval;
		}
		return NiceIntervalAtMost(valueRange / (double)minLevels);
	}

	public static void SelectLevels(double valueMin, double valueMax, double baseInterval, double multiplier, int indexEvery, List<ContourLevel> results)
	{
		if (results == null)
		{
			return;
		}
		if (!(multiplier > 0.0))
		{
			multiplier = 1.0;
		}
		double baseIntervalTimesMultiplier = baseInterval * multiplier;
		if (!(baseIntervalTimesMultiplier > 0.0) || double.IsInfinity(baseIntervalTimesMultiplier) || double.IsNaN(valueMin) || double.IsNaN(valueMax) || valueMax < valueMin)
		{
			return;
		}
		long valueMinTerm = (long)Math.Ceiling(valueMin / baseIntervalTimesMultiplier);
		long count = (long)Math.Floor(valueMax / baseIntervalTimesMultiplier);
		if (count < valueMinTerm)
		{
			return;
		}
		long countTerm = count - valueMinTerm + 1;
		bool condition = countTerm > 512;
		bool indexEveryTerm = indexEvery > 1;
		if (condition && (!indexEveryTerm || countTerm / indexEvery > 512))
		{
			return;
		}
		for (long ordinal = valueMinTerm; ordinal <= count; ordinal++)
		{
			bool indexEveryTerm2 = indexEveryTerm && ordinal % indexEvery == 0;
			if (!condition || indexEveryTerm2)
			{
				results.Add(new ContourLevel((float)((double)ordinal * baseIntervalTimesMultiplier), indexEveryTerm2));
			}
		}
	}
}
