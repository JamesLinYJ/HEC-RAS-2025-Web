using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers.BoundaryConditions;

namespace Ras.Observables;

public class ObservableStageHydroParams : ObservableValidator
{
	private StageHydroParams _stageHydroParams;

	public StageHydroParams StageHydroParams => _stageHydroParams;

	public bool UseInitialStage
	{
		get
		{
			return _stageHydroParams.UseInitialStage;
		}
		set
		{
			SetProperty(UseInitialStage, value, _stageHydroParams, (StageHydroParams model, bool newVal) =>
			{
				model.UseInitialStage = newVal;
			}, "UseInitialStage");
		}
	}

	public bool TailwaterCheck
	{
		get
		{
			return _stageHydroParams.TailwaterCheck;
		}
		set
		{
			SetProperty(TailwaterCheck, value, _stageHydroParams, (StageHydroParams model, bool newVal) =>
			{
				model.TailwaterCheck = newVal;
			}, "TailwaterCheck");
		}
	}

	public ObservableStageHydroParams(StageHydroParams stageHydroParams)
	{
		_stageHydroParams = stageHydroParams;
	}
}
