using System.Globalization;
using System.Reflection;
using Ras.Hydraulics.HydraulicDesign;

namespace Ras.Backend;

public sealed record WilcockLoadRequest(string? NumberCulture = null);

public sealed record WilcockEvaluateRequest(
    WilcockDraft? Draft,
    string? Action = "calculate",
    string? CaseID = null,
    bool ConfirmImport = false);

/// <summary>Editable calculator values are kept as the exact text supplied by the UI.</summary>
public sealed record WilcockDraft(
    string? NumberCulture,
    string? BtmMode,
    WilcockSbtmDraft? Sbtm,
    WilcockIsbtmDraft? Isbtm,
    WilcockStabDraft? Stab,
    WilcockStateDraft? State,
    string? SelectedCaseID = null,
    int? SelectedPlotIndex = null);

public sealed record WilcockSbtmDraft(
    string? ShearVelocity,
    string? WaterDensity,
    string? SedimentDensity,
    string[]? GrainDiameter,
    string[]? SurfaceCdf);

public sealed record WilcockIsbtmDraft(
    string? TotalTransportRate,
    string[]? GrainDiameter,
    string[]? TransportCdf);

public sealed record WilcockStabDraft(
    string[]? WaterDischarges,
    string[]? SedimentSupplyRates,
    string[][]? TransportGrainSizes,
    string? BMin,
    string? BMax,
    string? Z,
    string? Ns,
    string? Nspec,
    bool UseThresholdSlope,
    string? TcTauStarC,
    string? TcD50,
    string? TcD84,
    string? TcM,
    string[]? GrainDiameter);

public sealed record WilcockStateDraft(
    string[]? GrainDiameter,
    string[]? FirstTransportCdfGrainSize,
    string[]? SecondTransportCdfGrainSize,
    string[]? TransportRates,
    string[]? Slopes);

public sealed record WilcockWorkspaceSnapshot(
    WilcockDraft Draft,
    string BtmMode,
    WilcockCaseChoice[] Cases,
    string? SelectedCaseID,
    int SelectedPlotIndex,
    bool CanAddCase,
    bool CanRemoveCase,
    bool CanImportCase,
    string BtmImportPolicy,
    string? BtmImportReasonKey,
    bool ImportConfirmationRequired,
    bool ReplacedExistingCases,
    Dictionary<string, object?>? SbtmResults,
    Dictionary<string, object?>? IsbtmResults,
    Dictionary<string, object?>? StabResults,
    Dictionary<string, object?>? StateResults,
    WilcockSourceTable[] Tables,
    WilcockChart[] Charts,
    WilcockInputIssue[] InputIssues,
    string? SbtmErrorKey,
    string? IsbtmErrorKey,
    string? StabErrorKey,
    string? StateErrorKey);

public sealed record WilcockCaseChoice(string ID, int Index, string WaterDischarge, string SedimentSupplyRate);
public sealed record WilcockInputIssue(string Section, string Path, string MessageKey);

public sealed record WilcockSourceTable(
    string ID,
    string Section,
    string Role,
    string TitleKey,
    WilcockTableColumn[] Columns,
    WilcockTableRow[] Rows);

public sealed record WilcockTableColumn(
    string ID,
    string TitleKey,
    string SourceType,
    bool IsEditable,
    int? CaseIndex = null);

public sealed record WilcockTableRow(string ID, string[] Cells, string? TitleKey = null, int? CaseIndex = null);

public sealed record WilcockChart(
    string ID,
    string Section,
    string TitleKey,
    string XAxisKey,
    string YAxisKey,
    WilcockChartSeries[] Series);

public sealed record WilcockChartSeries(
    int? CaseIndex,
    string LabelKey,
    string ColorARGB,
    double StrokeWidth,
    WilcockChartPoint[] Points);

public sealed record WilcockChartPoint(double? X, double? Y);

/// <summary>
/// Stateless adapter over the retained Wilcock calculators and observable-derived behavior.
/// It does not mutate a project or persist calculator inputs.
/// </summary>
public static class WilcockService
{
    private const string InvalidInput = "The Wilcock calculation input is invalid.";
    private const string InvalidCase = "The selected Wilcock case is no longer available.";
    private const string KeepOneCase = "At least one Wilcock case must remain.";
    private const string InvalidOperation = "The Wilcock operation is not supported.";
    private const string UnsupportedNumberCulture = "The requested number culture is not supported.";

    private const string InvalidNumberKey = "Enter a valid number.";
    private const string InvalidShapeKey = "The input arrays do not have the required shape.";
    private const string CalculationFailedKey = "The calculator could not produce a result from these inputs.";
    private const string NonFiniteResultKey = "Some calculated values are outside the finite numeric range.";
    private const string ImportNeedsValidInputsKey = "Valid BTM and STAB inputs are required to import a case.";
    private const string NoImportDataKey = "The BTM does not contain grain diameter or surface transport data to import.";

    public static WilcockWorkspaceSnapshot Load(WilcockLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CultureInfo culture = ResolveCulture(request.NumberCulture);
        return BuildSnapshot(FromSourceDefaults(new WilcockMethod(), culture), false, false);
    }

    public static WilcockWorkspaceSnapshot Evaluate(WilcockEvaluateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WilcockDraft draft = request.Draft ?? throw new ArgumentException(InvalidInput);
        string action = request.Action ?? "calculate";
        switch (action)
        {
            case "calculate":
                return BuildSnapshot(draft, false, false);
            case "addCase":
            {
                WilcockDraft edited = CompleteForMutation(draft);
                WilcockStabDraft stab = edited.Stab!;
                string[][] cdfs = stab.TransportGrainSizes ?? [];
                int zeroCount = stab.GrainDiameter?.Length ?? 0;
                string[] zeros = Enumerable.Repeat("0", zeroCount).ToArray();
                edited = edited with
                {
                    Stab = stab with
                    {
                        WaterDischarges = [.. (stab.WaterDischarges ?? []), "0"],
                        SedimentSupplyRates = [.. (stab.SedimentSupplyRates ?? []), "0"],
                        TransportGrainSizes = [.. cdfs, zeros]
                    },
                    SelectedCaseID = "case:0"
                };
                return BuildSnapshot(edited, false, false);
            }
            case "removeCase":
            {
                WilcockDraft edited = CompleteForMutation(draft);
                WilcockStabDraft stab = edited.Stab!;
                string[][] cdfs = stab.TransportGrainSizes ?? [];
                int count = Math.Max(cdfs.Length, Math.Max(stab.WaterDischarges?.Length ?? 0,
                    stab.SedimentSupplyRates?.Length ?? 0));
                int index = ParseCaseIndex(request.CaseID, count);
                if (count <= 1) throw new ArgumentException(KeepOneCase);
                edited = edited with
                {
                    Stab = stab with
                    {
                        WaterDischarges = RemoveAtIfPresent(stab.WaterDischarges ?? [], index),
                        SedimentSupplyRates = RemoveAtIfPresent(stab.SedimentSupplyRates ?? [], index),
                        TransportGrainSizes = RemoveAtIfPresent(cdfs, index)
                    },
                    SelectedCaseID = "case:0"
                };
                return BuildSnapshot(edited, false, false);
            }
            case "importCaseFromBTM":
                return ImportCase(draft, request.ConfirmImport);
            default:
                throw new ArgumentException(InvalidOperation);
        }
    }

    private static WilcockWorkspaceSnapshot ImportCase(WilcockDraft draft, bool confirmed)
    {
        WilcockWorkspaceSnapshot before = BuildSnapshot(draft, false, false);
        if (before.BtmImportPolicy == "unavailable") return before;
        // The retained control asks for confirmation for both compatible append
        // and incompatible-grid replacement. The request carries the same exact
        // raw draft shown by the preflight, so a changed draft must be confirmed
        // by a fresh request from the session.
        if (!confirmed)
            return before with { ImportConfirmationRequired = true };

        CultureInfo culture = ResolveCulture(draft.NumberCulture);
        WilcockDraft edited = CompleteForMutation(draft);
        WilcockStabDraft stab = edited.Stab!;
        (double[] grains, double[] surfaceCDF, double transportRate) = ImportValues(draft, culture);
        double[] stabGrains = ParseArrayForAction(stab.GrainDiameter, culture);
        bool sameGrid = grains.Length == stabGrains.Length;
        if (sameGrid)
        {
            for (int i = 0; i < grains.Length; i++)
            {
                if (Math.Abs(grains[i] - stabGrains[i]) > 0.0001)
                {
                    sameGrid = false;
                    break;
                }
            }
        }

        string[] importedGrains = grains.Select(value => Raw(value, culture)).ToArray();
        string[] importedCDF = surfaceCDF.Select(value => Raw(value, culture)).ToArray();
        bool replaced = false;
        if (!sameGrid)
        {
            edited = edited with
            {
                Stab = stab with
                {
                    GrainDiameter = importedGrains,
                    WaterDischarges = ["80"],
                    SedimentSupplyRates = [Raw(transportRate > 0.0 ? transportRate : 0.008, culture)],
                    TransportGrainSizes = [importedCDF]
                },
                SelectedCaseID = "case:0"
            };
            replaced = true;
        }
        else
        {
            string[] water = stab.WaterDischarges ?? [];
            string[] supply = stab.SedimentSupplyRates ?? [];
            string waterDischarge = water.Length > 0 ? water[^1] : "0";
            string sedimentSupply = transportRate > 0.0
                ? Raw(transportRate, culture)
                : supply.Length > 0 ? supply[^1] : "0";
            edited = edited with
            {
                Stab = stab with
                {
                    WaterDischarges = [.. water, waterDischarge],
                    SedimentSupplyRates = [.. supply, sedimentSupply],
                    TransportGrainSizes = [.. (stab.TransportGrainSizes ?? []), importedCDF]
                },
                SelectedCaseID = "case:0"
            };
        }
        return BuildSnapshot(edited, false, replaced);
    }

    private static (double[] GrainDiameter, double[] SurfaceCdf, double TotalTransportRate) ImportValues(
        WilcockDraft draft, CultureInfo culture)
    {
        List<WilcockInputIssue> issues = [];
        string mode = NormalizeMode(draft.BtmMode);
        if (mode == "ISBTM")
        {
            WilcockIsbtmDraft raw = draft.Isbtm ?? EmptyIsbtm();
            WilcockMethod.ISBTM? input = ParseIsbtm(raw, culture, issues);
            if (issues.Count != 0 || input is null)
                throw new ArgumentException(InvalidInput);
            ValidateIsbtm(input);
            ISBTMResults result = WilcockIsbtmCalculator.ComputeIsbtm(input);
            return (input.GrainDiameter, result.SurfaceCdf, input.TotalTransportRate);
        }
        else
        {
            WilcockSbtmDraft raw = draft.Sbtm ?? EmptySbtm();
            WilcockMethod.SBTM? input = ParseSbtmImportInputs(raw, culture, issues);
            if (issues.Count != 0 || input is null)
                throw new ArgumentException(InvalidInput);
            ValidateSbtm(input);
            SBTMResults result = WilcockSbtmCalculator.ComputeSbtm(input);
            // The retained Import button copies ObservableMergedBTM.SurfaceCdf.
            return (input.GrainDiameter, input.SurfaceCdf, result.TransportRate);
        }
    }

    private static WilcockMethod.SBTM? ParseSbtmImportInputs(WilcockSbtmDraft? raw,
        CultureInfo culture, List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue("btm", "sbtm", InvalidShapeKey));
            return null;
        }

        // The retained SBTM calculator uses shear velocity and the two
        // distribution arrays. It does not read WaterDensity or SedimentDensity;
        // those model fields are still kept verbatim in the editable draft and
        // are parsed by the normal calculator workflow, but they do not gate the
        // source import action.
        return new WilcockMethod.SBTM
        {
            ShearVelocity = ParseNumber(raw.ShearVelocity, culture, "btm", "sbtm.shearVelocity", issues),
            WaterDensity = 1000.0,
            SedimentDensity = 2650.0,
            GrainDiameter = ParseArray(raw.GrainDiameter, culture, "btm", "sbtm.grainDiameter", issues),
            SurfaceCdf = ParseArray(raw.SurfaceCdf, culture, "btm", "sbtm.surfaceCdf", issues)
        };
    }

    private static WilcockWorkspaceSnapshot BuildSnapshot(
        WilcockDraft sourceDraft,
        bool importConfirmationRequired,
        bool replacedExistingCases)
    {
        CultureInfo culture = ResolveCulture(sourceDraft.NumberCulture);
        WilcockDraft draft = CompleteForWire(sourceDraft, culture);
        List<WilcockInputIssue> issues = [];
        string mode = NormalizeMode(draft.BtmMode);

        SectionOutcome<WilcockMethod.SBTM, SBTMResults> sbtm =
            EvaluateSbtm(sourceDraft.Sbtm, culture, issues);
        SectionOutcome<WilcockMethod.ISBTM, ISBTMResults> isbtm =
            EvaluateIsbtm(sourceDraft.Isbtm, culture, issues);
        SectionOutcome<WilcockMethod.STAB, StabResults> stab =
            EvaluateStab(sourceDraft.Stab, culture, issues);
        SectionOutcome<WilcockMethod.STATE, StateResults> state =
            EvaluateState(sourceDraft.State, culture, issues);

        bool sbtmNonFinite = false;
        bool isbtmNonFinite = false;
        bool stabNonFinite = false;
        bool stateNonFinite = false;
        Dictionary<string, object?>? sbtmJSON = sbtm.Result is null ? null : ToJSON(sbtm.Result, out sbtmNonFinite);
        Dictionary<string, object?>? isbtmJSON = isbtm.Result is null ? null : ToJSON(isbtm.Result, out isbtmNonFinite);
        Dictionary<string, object?>? stabJSON = stab.Result is null ? null : ToJSON(stab.Result, out stabNonFinite);
        Dictionary<string, object?>? stateJSON = state.Result is null ? null : ToJSON(state.Result, out stateNonFinite);

        string? sbtmError = sbtm.ErrorKey ?? (sbtmNonFinite ? NonFiniteResultKey : null);
        string? isbtmError = isbtm.ErrorKey ?? (isbtmNonFinite ? NonFiniteResultKey : null);
        string? stabError = stab.ErrorKey ?? (stabNonFinite ? NonFiniteResultKey : null);
        string? stateError = state.ErrorKey ?? (stateNonFinite ? NonFiniteResultKey : null);

        string importPolicy = DetermineImportPolicy(draft, mode, culture, out string? importReason);

        string[] waterDischarges = draft.Stab?.WaterDischarges ?? [];
        string[] supplyRates = draft.Stab?.SedimentSupplyRates ?? [];
        string[][] grainCases = draft.Stab?.TransportGrainSizes ?? [];
        int caseCount = Math.Max(grainCases.Length, Math.Max(waterDischarges.Length, supplyRates.Length));
        WilcockCaseChoice[] cases = Enumerable.Range(0, caseCount)
            .Select(index => new WilcockCaseChoice($"case:{index}", index,
                AtOrDefault(waterDischarges, index, "0"), AtOrDefault(supplyRates, index, "0")))
            .ToArray();
        string selectedCaseID = draft.SelectedCaseID is not null && cases.Any(item => item.ID == draft.SelectedCaseID)
            ? draft.SelectedCaseID
            : cases.FirstOrDefault()?.ID ?? "case:0";
        int selectedPlotIndex = draft.SelectedPlotIndex is >= 0 and <= 7 ? draft.SelectedPlotIndex.Value : 0;
        WilcockDraft resultDraft = draft with { SelectedCaseID = selectedCaseID, SelectedPlotIndex = selectedPlotIndex };

        var charts = BuildCharts(mode, sbtm.Input, sbtm.Result, isbtm.Input, isbtm.Result,
            draft.Stab!, stab.Result, draft.State!, state.Result, culture);
        var tables = BuildTables(draft, mode, culture, sbtm.Result, isbtm.Result, stab.Result, state.Result);

        return new WilcockWorkspaceSnapshot(
            resultDraft, mode, cases, selectedCaseID, selectedPlotIndex,
            CanAddCase: true,
            CanRemoveCase: caseCount > 1,
            CanImportCase: importPolicy != "unavailable",
            BtmImportPolicy: importPolicy,
            BtmImportReasonKey: importReason,
            ImportConfirmationRequired: importConfirmationRequired,
            ReplacedExistingCases: replacedExistingCases,
            SbtmResults: sbtmJSON,
            IsbtmResults: isbtmJSON,
            StabResults: stabJSON,
            StateResults: stateJSON,
            Tables: tables,
            Charts: charts,
            InputIssues: issues.DistinctBy(item => (item.Section, item.Path, item.MessageKey)).ToArray(),
            SbtmErrorKey: sbtmError,
            IsbtmErrorKey: isbtmError,
            StabErrorKey: stabError,
            StateErrorKey: stateError);
    }

    private static SectionOutcome<WilcockMethod.SBTM, SBTMResults> EvaluateSbtm(
        WilcockSbtmDraft? raw, CultureInfo culture, List<WilcockInputIssue> issues)
    {
        int before = issues.Count;
        WilcockMethod.SBTM? input = ParseSbtm(raw, culture, issues);
        if (issues.Count != before) return new(input, null, InvalidNumberKey);
        try
        {
            if (input is null) throw new InputShapeException("sbtm", InvalidShapeKey);
            ValidateCdf(input.GrainDiameter, input.SurfaceCdf, "sbtm.grainDiameter", "sbtm.surfaceCdf");
            return new(input, WilcockSbtmCalculator.ComputeSbtm(input), null);
        }
        catch (InputShapeException exception)
        {
            issues.Add(new WilcockInputIssue("btm", exception.Path, exception.MessageKey));
            return new(input, null, exception.MessageKey);
        }
        catch (Exception)
        {
            return new(input, null, CalculationFailedKey);
        }
    }

    private static SectionOutcome<WilcockMethod.ISBTM, ISBTMResults> EvaluateIsbtm(
        WilcockIsbtmDraft? raw, CultureInfo culture, List<WilcockInputIssue> issues)
    {
        int before = issues.Count;
        WilcockMethod.ISBTM? input = ParseIsbtm(raw, culture, issues);
        if (issues.Count != before) return new(input, null, InvalidNumberKey);
        try
        {
            if (input is null) throw new InputShapeException("isbtm", InvalidShapeKey);
            ValidateIsbtm(input);
            return new(input, WilcockIsbtmCalculator.ComputeIsbtm(input), null);
        }
        catch (InputShapeException exception)
        {
            issues.Add(new WilcockInputIssue("btm", exception.Path, exception.MessageKey));
            return new(input, null, exception.MessageKey);
        }
        catch (Exception)
        {
            return new(input, null, CalculationFailedKey);
        }
    }

    private static SectionOutcome<WilcockMethod.STAB, StabResults> EvaluateStab(
        WilcockStabDraft? raw, CultureInfo culture, List<WilcockInputIssue> issues)
    {
        int before = issues.Count;
        WilcockMethod.STAB? input = ParseStab(raw, culture, issues);
        if (issues.Count != before) return new(input, null, InvalidNumberKey);
        try
        {
            if (input is null) throw new InputShapeException("stab", InvalidShapeKey);
            ValidateStab(input);
            return new(input, WilcockStabCalculator.ComputeStab(input), null);
        }
        catch (InputShapeException exception)
        {
            issues.Add(new WilcockInputIssue("stab", exception.Path, exception.MessageKey));
            return new(input, null, exception.MessageKey);
        }
        catch (Exception)
        {
            return new(input, null, CalculationFailedKey);
        }
    }

    private static SectionOutcome<WilcockMethod.STATE, StateResults> EvaluateState(
        WilcockStateDraft? raw, CultureInfo culture, List<WilcockInputIssue> issues)
    {
        int before = issues.Count;
        WilcockMethod.STATE? input = ParseState(raw, culture, issues);
        if (issues.Count != before) return new(input, null, InvalidNumberKey);
        try
        {
            if (input is null) throw new InputShapeException("state", InvalidShapeKey);
            ValidateState(input);
            return new(input, WilcockStateCalculator.ComputeState(input), null);
        }
        catch (InputShapeException exception)
        {
            issues.Add(new WilcockInputIssue("state", exception.Path, exception.MessageKey));
            return new(input, null, exception.MessageKey);
        }
        catch (Exception)
        {
            return new(input, null, CalculationFailedKey);
        }
    }

    private static WilcockMethod.SBTM? ParseSbtm(WilcockSbtmDraft? raw, CultureInfo culture,
        List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue("btm", "sbtm", InvalidShapeKey));
            return null;
        }
        return new WilcockMethod.SBTM
        {
            ShearVelocity = ParseNumber(raw.ShearVelocity, culture, "btm", "sbtm.shearVelocity", issues),
            WaterDensity = ParseNumber(raw.WaterDensity, culture, "btm", "sbtm.waterDensity", issues),
            SedimentDensity = ParseNumber(raw.SedimentDensity, culture, "btm", "sbtm.sedimentDensity", issues),
            GrainDiameter = ParseArray(raw.GrainDiameter, culture, "btm", "sbtm.grainDiameter", issues),
            SurfaceCdf = ParseArray(raw.SurfaceCdf, culture, "btm", "sbtm.surfaceCdf", issues)
        };
    }

    private static WilcockMethod.ISBTM? ParseIsbtm(WilcockIsbtmDraft? raw, CultureInfo culture,
        List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue("btm", "isbtm", InvalidShapeKey));
            return null;
        }
        return new WilcockMethod.ISBTM
        {
            TotalTransportRate = ParseNumber(raw.TotalTransportRate, culture, "btm", "isbtm.totalTransportRate", issues),
            GrainDiameter = ParseArray(raw.GrainDiameter, culture, "btm", "isbtm.grainDiameter", issues),
            TransportCdf = ParseArray(raw.TransportCdf, culture, "btm", "isbtm.transportCdf", issues)
        };
    }

    private static WilcockMethod.STAB? ParseStab(WilcockStabDraft? raw, CultureInfo culture,
        List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue("stab", "stab", InvalidShapeKey));
            return null;
        }
        return new WilcockMethod.STAB
        {
            WaterDischarges = ParseArray(raw.WaterDischarges, culture, "stab", "stab.waterDischarges", issues),
            SedimentSupplyRates = ParseArray(raw.SedimentSupplyRates, culture, "stab", "stab.sedimentSupplyRates", issues),
            TransportGrainSizes = ParseJagged(raw.TransportGrainSizes, culture, "stab", "stab.transportGrainSizes", issues),
            BMin = ParseNumber(raw.BMin, culture, "stab", "stab.bMin", issues),
            BMax = ParseNumber(raw.BMax, culture, "stab", "stab.bMax", issues),
            Z = ParseNumber(raw.Z, culture, "stab", "stab.z", issues),
            Ns = ParseNumber(raw.Ns, culture, "stab", "stab.ns", issues),
            Nspec = ParseNumber(raw.Nspec, culture, "stab", "stab.nSpec", issues),
            UseThresholdSlope = raw.UseThresholdSlope,
            TcTauStarC = ParseNumber(raw.TcTauStarC, culture, "stab", "stab.tcTauStarC", issues),
            TcD50 = ParseNumber(raw.TcD50, culture, "stab", "stab.tcD50", issues),
            TcD84 = ParseNumber(raw.TcD84, culture, "stab", "stab.tcD84", issues),
            TcM = ParseNumber(raw.TcM, culture, "stab", "stab.tcM", issues),
            GrainDiameter = ParseArray(raw.GrainDiameter, culture, "stab", "stab.grainDiameter", issues)
        };
    }

    private static WilcockMethod.STATE? ParseState(WilcockStateDraft? raw, CultureInfo culture,
        List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue("state", "state", InvalidShapeKey));
            return null;
        }
        return new WilcockMethod.STATE
        {
            GrainDiameter = ParseArray(raw.GrainDiameter, culture, "state", "state.grainDiameter", issues),
            FirstTransportCDFGrainSize = ParseArray(raw.FirstTransportCdfGrainSize, culture, "state",
                "state.firstTransportCdfGrainSize", issues),
            SecondTransportCDFGrainSize = ParseArray(raw.SecondTransportCdfGrainSize, culture, "state",
                "state.secondTransportCdfGrainSize", issues),
            TransportRates = ParseArray(raw.TransportRates, culture, "state", "state.transportRates", issues),
            Slopes = ParseArray(raw.Slopes, culture, "state", "state.slopes", issues)
        };
    }

    private static void ValidateIsbtm(WilcockMethod.ISBTM input)
    {
        // The retained solver returns a sentinel before using the distribution
        // when rate <= 0 or the selected CDF contains no positive value.
        if (input.TotalTransportRate <= 0.0 || !input.TransportCdf.Any(value => value > 0.0)) return;
        ValidateCdf(input.GrainDiameter, input.TransportCdf, "isbtm.grainDiameter", "isbtm.transportCdf");
    }

    private static void ValidateSbtm(WilcockMethod.SBTM input)
        => ValidateCdf(input.GrainDiameter, input.SurfaceCdf, "sbtm.grainDiameter", "sbtm.surfaceCdf");

    private static void ValidateStab(WilcockMethod.STAB input)
    {
        int caseCount = input.TransportGrainSizes.Length;
        if (input.WaterDischarges.Length < caseCount || input.SedimentSupplyRates.Length < caseCount)
            throw new InputShapeException("stab.caseInputs", InvalidShapeKey);
        for (int index = 0; index < caseCount; index++)
        {
            double[] cdf = input.TransportGrainSizes[index];
            bool active = input.WaterDischarges[index] > 0.0 && input.SedimentSupplyRates[index] > 0.0
                && cdf.Any(value => value > 0.0);
            if (active)
                ValidateCdf(input.GrainDiameter, cdf, "stab.grainDiameter", $"stab.transportGrainSizes[{index}]");
        }
    }

    private static void ValidateState(WilcockMethod.STATE input)
    {
        int firstZero = Array.FindIndex(input.FirstTransportCDFGrainSize, value => value == 0.0);
        int activeLength = firstZero >= 0 ? firstZero + 1 : input.FirstTransportCDFGrainSize.Length;
        if (activeLength < 2 || input.GrainDiameter.Length < activeLength
            || input.SecondTransportCDFGrainSize.Length < activeLength
            || input.TransportRates.Length == 0 || input.Slopes.Length == 0)
            throw new InputShapeException("state", InvalidShapeKey);
    }

    private static void ValidateCdf(double[] grainDiameter, double[] cdf, string grainPath, string cdfPath)
    {
        int firstZero = Array.FindIndex(cdf, value => value == 0.0);
        int activeLength = firstZero >= 0 ? firstZero + 1 : cdf.Length;
        if (activeLength < 2 || grainDiameter.Length < activeLength)
            throw new InputShapeException($"{grainPath}|{cdfPath}", InvalidShapeKey);
    }

    private static double ParseNumber(string? raw, CultureInfo culture, string section, string path,
        List<WilcockInputIssue> issues)
    {
        if (raw is not null && double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, culture,
                out double value) && double.IsFinite(value))
            return value;
        issues.Add(new WilcockInputIssue(section, path, InvalidNumberKey));
        return 0.0;
    }

    private static double[] ParseArray(string[]? raw, CultureInfo culture, string section, string path,
        List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue(section, path, InvalidShapeKey));
            return [];
        }
        var values = new double[raw.Length];
        for (int index = 0; index < raw.Length; index++)
            values[index] = ParseNumber(raw[index], culture, section, $"{path}[{index}]", issues);
        return values;
    }

    private static double[][] ParseJagged(string[][]? raw, CultureInfo culture, string section, string path,
        List<WilcockInputIssue> issues)
    {
        if (raw is null)
        {
            issues.Add(new WilcockInputIssue(section, path, InvalidShapeKey));
            return [];
        }
        var values = new double[raw.Length][];
        for (int index = 0; index < raw.Length; index++)
            values[index] = ParseArray(raw[index], culture, section, $"{path}[{index}]", issues);
        return values;
    }

    private static string DetermineImportPolicy(WilcockDraft draft, string mode,
        CultureInfo culture, out string? reason)
    {
        reason = null;
        (double[] GrainDiameter, double[] SurfaceCdf, double TotalTransportRate) data;
        try
        {
            data = ImportValues(draft, culture);
        }
        catch (Exception)
        {
            reason = ImportNeedsValidInputsKey;
            return "unavailable";
        }

        List<WilcockInputIssue> gridIssues = [];
        double[] stabGrains = ParseArray(draft.Stab?.GrainDiameter, culture, "stab", "stab.grainDiameter", gridIssues);
        if (gridIssues.Count > 0)
        {
            reason = ImportNeedsValidInputsKey;
            return "unavailable";
        }

        if (data.GrainDiameter.Length == 0 || data.SurfaceCdf.Length == 0)
        {
            reason = NoImportDataKey;
            return "unavailable";
        }
        bool sameGrid = data.GrainDiameter.Length == stabGrains.Length;
        if (sameGrid)
            for (int index = 0; index < data.GrainDiameter.Length; index++)
                if (Math.Abs(data.GrainDiameter[index] - stabGrains[index]) > 0.0001)
                {
                    sameGrid = false;
                    break;
                }
        return sameGrid ? "append" : "replaceExistingCases";
    }

    private static WilcockSourceTable[] BuildTables(WilcockDraft draft, string mode, CultureInfo culture,
        SBTMResults? sbtm, ISBTMResults? isbtm, StabResults? stab, StateResults? state)
    {
        var tables = new List<WilcockSourceTable>
        {
            PairInputTable("btm.sbtm.input", "btm", "SBTM distribution",
                "grainDiameter", "Grain diameter (mm)", draft.Sbtm?.GrainDiameter,
                "surfaceCdf", "Surface CDF (%)", draft.Sbtm?.SurfaceCdf),
            PairInputTable("btm.isbtm.input", "btm", "ISBTM distribution",
                "grainDiameter", "Grain diameter (mm)", draft.Isbtm?.GrainDiameter,
                "transportCdf", "Transport CDF (%)", draft.Isbtm?.TransportCdf),
            BuildStabCaseInputs(draft.Stab),
            BuildStabGrainTable(draft.Stab),
            PairInputTable("state.grainDistributions", "state", "STATE grain-size distributions",
                "grainDiameter", "Grain diameter (mm)", draft.State?.GrainDiameter,
                "firstCDF", "First transport CDF (%)", draft.State?.FirstTransportCdfGrainSize,
                "secondCDF", "Second transport CDF (%)", draft.State?.SecondTransportCdfGrainSize),
            SingleColumnInputTable("state.transportRates", "state", "STATE transport rates",
                "transportRate", "Transport rate", draft.State?.TransportRates),
            SingleColumnInputTable("state.slopes", "state", "STATE slopes",
                "slope", "Slope", draft.State?.Slopes)
        };
        if (mode == "ISBTM" && isbtm is not null)
            tables.Add(BuildCompleteOutputTable("btm.output", "btm", "ISBTM output and convergence",
                isbtm, culture, caseIndexed: false));
        else if (mode != "ISBTM" && sbtm is not null)
            tables.Add(BuildCompleteOutputTable("btm.output", "btm", "SBTM output statistics",
                sbtm, culture, caseIndexed: false));
        if (stab is not null)
        {
            tables.Add(BuildStabStatistics(stab, culture));
            tables.Add(BuildCompleteOutputTable("stab.completeOutput", "stab", "All STAB calculated values",
                stab, culture, caseIndexed: true));
        }
        if (state is not null)
        {
            tables.Add(BuildStateStatistics(state, culture));
            tables.Add(BuildCompleteOutputTable("state.completeOutput", "state", "All STATE calculated values",
                state, culture, caseIndexed: false));
        }
        return tables.ToArray();
    }

    private static WilcockSourceTable BuildCompleteOutputTable(string id, string section, string title,
        object result, CultureInfo culture, bool caseIndexed)
    {
        var rows = new List<WilcockTableRow>();
        foreach (PropertyInfo property in result.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(item => item.CanRead && item.GetIndexParameters().Length == 0)
                     .OrderBy(item => item.MetadataToken))
        {
            object? value = property.GetValue(result);
            string memberTitle = ResultMemberTitle(property.Name);
            if (value is Array array)
            {
                AppendOutputArrayRows(rows, property.Name, memberTitle, array, culture, caseIndexed);
                continue;
            }

            rows.Add(new WilcockTableRow($"member:{property.Name}", ["", FormatOutputValue(value, culture)], memberTitle));
        }

        return new WilcockSourceTable(id, section, "output", title,
            [new("index", "Index", "text", false), new("value", "Value", "number", false)], rows.ToArray());
    }

    private static void AppendOutputArrayRows(List<WilcockTableRow> rows, string memberName, string memberTitle,
        Array array, CultureInfo culture, bool caseIndexed)
    {
        if (array.Length == 0)
        {
            rows.Add(new WilcockTableRow($"member:{memberName}:empty", ["", ""], memberTitle));
            return;
        }

        int[] indices = new int[array.Rank];
        AppendDimension(0);

        void AppendDimension(int dimension)
        {
            if (dimension == array.Rank)
            {
                object? item = array.GetValue(indices);
                string indexText = string.Join(", ", indices.Select(index => (index + 1).ToString(CultureInfo.InvariantCulture)));
                int? caseIndex = caseIndexed && indices.Length > 0 ? indices[0] : null;
                string rowID = $"member:{memberName}:{string.Join(":", indices)}";
                rows.Add(new WilcockTableRow(rowID, [indexText, FormatOutputValue(item, culture)],
                    memberTitle, caseIndex));
                return;
            }

            for (int index = 0; index < array.GetLength(dimension); index++)
            {
                indices[dimension] = index;
                AppendDimension(dimension + 1);
            }
        }
    }

    private static string FormatOutputValue(object? value, CultureInfo culture)
        => value switch
        {
            null => "",
            double number => double.IsFinite(number) ? Raw(number, culture) : "",
            float number => float.IsFinite(number) ? number.ToString("R", culture) : "",
            IFormattable formatted => formatted.ToString(null, culture) ?? "",
            _ => value.ToString() ?? ""
        };

    private static string ResultMemberTitle(string memberName) => memberName switch
    {
        "TransportRate" => "Total transport rate",
        "CumulativeBedloadDistribution" => "Cumulative bed-load distribution",
        "SurfaceCdf" => "Surface CDF",
        "SurfaceDMin" => "Surface D min",
        "SurfaceDMax" => "Surface D max",
        "Ds50" => "Surface D50",
        "Dsm" => "Surface geometric mean diameter",
        "SSig" => "Surface geometric standard deviation",
        "SFs" => "Surface fine fraction",
        "BedloadDMin" => "Bed-load D min",
        "BedloadDMax" => "Bed-load D max",
        "Db50" => "Bed-load D50",
        "Dbm" => "Bed-load geometric mean diameter",
        "BSig" => "Bed-load geometric standard deviation",
        "BFs" => "Bed-load fine fraction",
        "UStar" => "Shear velocity",
        "MaxIterations" => "Maximum iterations",
        "Iterations" => "Iterations",
        "Tolerance" => "Convergence tolerance",
        "Dres" => "Geometric mean diameter residual",
        "MaxFres" => "Maximum CDF residual",
        "Fsres" => "Fine-fraction residual",
        "Ures" => "Shear-velocity residual",
        "Qres" => "Transport-rate residual",
        "DMin" => "Grain-size D min",
        "DMax" => "Grain-size D max",
        "D50" => "Grain-size D50",
        "Dg" => "Geometric mean diameter",
        "SigmaG" => "Geometric standard deviation",
        "Fs" => "Fine fraction",
        "B" => "Channel width",
        "QT" => "Transport rate by width",
        "Ds50Array" => "Surface D50 by case",
        "Gamma" => "Armor ratio",
        "ND" => "Base Manning's n",
        "TB" => "Boundary shear stress",
        "H" => "Flow depth",
        "S" => "Channel slope",
        "NVP" => "Composite Manning's n",
        "NC" => "Composite Manning's n using specified n",
        "Ds84" => "Surface D84",
        "HSpecN" => "Flow depth using specified n",
        "SSpecN" => "Slope using specified n",
        "F" => "Froude number",
        "RelativeDepth" => "Relative depth",
        "GSDAtMeanWidthD" => "Grain diameter at mean width",
        "GSDAtMeanWidthFi" => "Cumulative finer at mean width",
        "QTKgPerHr" => "Sediment supply rate (kg/hr)",
        "Cutey" => "Threshold transport rate by width",
        "BMean" => "Mean channel width",
        "TUStar" => "Threshold shear velocity",
        "TDsm" => "Threshold surface geometric mean diameter",
        "TDs50" => "Threshold surface D50",
        "TGamma" => "Threshold armor ratio",
        "TND" => "Threshold Manning's n",
        "TTB" => "Threshold boundary shear stress",
        "TH" => "Threshold flow depth",
        "TS" => "Threshold slope",
        "TNVP" => "Threshold composite Manning's n",
        "TNC" => "Threshold composite Manning's n using specified n",
        "TDs84" => "Threshold surface D84",
        "TcS" => "Critical slope",
        "FirstDMin" => "First distribution D min",
        "FirstDMax" => "First distribution D max",
        "FirstDbm" => "First distribution D mean",
        "FirstDg" => "First distribution geometric mean diameter",
        "FirstSigmaG" => "First distribution geometric standard deviation",
        "FirstD50" => "First distribution D50",
        "FirstFs" => "First distribution fine fraction",
        "SecondDMin" => "Second distribution D min",
        "SecondDMax" => "Second distribution D max",
        "SecondDbm" => "Second distribution D mean",
        "SecondDg" => "Second distribution geometric mean diameter",
        "SecondSigmaG" => "Second distribution geometric standard deviation",
        "SecondD50" => "Second distribution D50",
        "SecondFs" => "Second distribution fine fraction",
        "SqT1" => "First distribution dimensionless transport rate",
        "SqT2" => "Second distribution dimensionless transport rate",
        "Ustr1" => "First distribution shear velocity",
        "Ustr2" => "Second distribution shear velocity",
        "Ds1m" => "First distribution geometric mean diameter by transport rate",
        "Ds2m" => "Second distribution geometric mean diameter by transport rate",
        "Ds150_1" => "First distribution surface D50",
        "Ds250_2" => "Second distribution surface D50",
        "Gamma1" => "First distribution armor ratio",
        "Gamma2" => "Second distribution armor ratio",
        "FlowDepth1" => "First distribution flow depth",
        "FlowDepth2" => "Second distribution flow depth",
        "Roughness1" => "First distribution roughness height",
        "Roughness2" => "Second distribution roughness height",
        "FlowVelocity1" => "First distribution flow velocity",
        "FlowVelocity2" => "Second distribution flow velocity",
        "SqW1" => "First distribution dimensionless transport by slope and rate",
        "SqW2" => "Second distribution dimensionless transport by slope and rate",
        "QW1" => "First distribution unit-width flow rate",
        "QW2" => "Second distribution unit-width flow rate",
        "Sb1" => "First distribution dimensionless bed-load transport by width",
        "Sb2" => "Second distribution dimensionless bed-load transport by width",
        "Sd1" => "First distribution dimensionless transport by depth",
        "Sd2" => "Second distribution dimensionless transport by depth",
        "SqWi" => "Dimensionless transport-rate index",
        "MinSqW" => "Minimum dimensionless transport by slope and rate",
        "MaxSqW" => "Maximum dimensionless transport by slope and rate",
        "QWi" => "Unit-width flow rate values",
        "MinQW" => "Minimum unit-width flow rate",
        "MaxQW" => "Maximum unit-width flow rate",
        "TransportRates" => "Transport rate values",
        "Slopes" => "Slope values",
        _ => SplitMemberName(memberName)
    };

    private static string SplitMemberName(string memberName)
    {
        var builder = new System.Text.StringBuilder(memberName.Length + 8);
        for (int index = 0; index < memberName.Length; index++)
        {
            char character = memberName[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(memberName[index - 1]))
                builder.Append(' ');
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static WilcockSourceTable PairInputTable(string id, string section, string titleKey,
        string column1, string title1, string[]? values1, string column2, string title2, string[]? values2)
        => PairInputTableCore(id, section, titleKey,
            [(column1, title1, values1 ?? []), (column2, title2, values2 ?? [])]);

    private static WilcockSourceTable PairInputTable(string id, string section, string titleKey,
        string column1, string title1, string[]? values1, string column2, string title2, string[]? values2,
        string column3, string title3, string[]? values3)
        => PairInputTableCore(id, section, titleKey,
            [(column1, title1, values1 ?? []), (column2, title2, values2 ?? []), (column3, title3, values3 ?? [])]);

    private static WilcockSourceTable PairInputTableCore(string id, string section, string titleKey,
        (string ID, string TitleKey, string[] Values)[] columns)
    {
        int count = columns.Select(column => column.Values.Length).DefaultIfEmpty().Max();
        var rows = new WilcockTableRow[count];
        for (int row = 0; row < count; row++)
            rows[row] = new WilcockTableRow($"grain:{row}",
                columns.Select(column => AtOrBlank(column.Values, row)).ToArray());
        return new WilcockSourceTable(id, section, "input", titleKey,
            columns.Select(column => new WilcockTableColumn(column.ID, column.TitleKey, "number", true)).ToArray(),
            rows);
    }

    private static WilcockSourceTable BuildStabCaseInputs(WilcockStabDraft? stab)
    {
        string[] water = stab?.WaterDischarges ?? [];
        string[] supply = stab?.SedimentSupplyRates ?? [];
        int count = Math.Max(water.Length, supply.Length);
        return new WilcockSourceTable("stab.caseInputs", "stab", "input", "STAB cases",
            [new("waterDischarge", "Water discharge", "number", true),
             new("sedimentSupplyRate", "Sediment supply rate", "number", true)],
            Enumerable.Range(0, count)
                .Select(index => new WilcockTableRow($"case:{index}",
                    [AtOrBlank(water, index), AtOrBlank(supply, index)])).ToArray());
    }

    private static WilcockSourceTable BuildStabGrainTable(WilcockStabDraft? stab)
    {
        string[] grains = stab?.GrainDiameter ?? [];
        string[][] cdfs = stab?.TransportGrainSizes ?? [];
        var columns = new List<WilcockTableColumn>
        {
            new("grainDiameter", "Grain diameter (mm)", "number", true)
        };
        for (int index = 0; index < cdfs.Length; index++)
            columns.Add(new($"case:{index}", "Case", "number", true, index));
        int count = Math.Max(grains.Length, cdfs.Select(row => row?.Length ?? 0).DefaultIfEmpty().Max());
        var rows = new WilcockTableRow[count];
        for (int row = 0; row < count; row++)
        {
            var cells = new string[columns.Count];
            cells[0] = AtOrBlank(grains, row);
            for (int caseIndex = 0; caseIndex < cdfs.Length; caseIndex++)
                cells[caseIndex + 1] = AtOrBlank(cdfs[caseIndex] ?? [], row);
            rows[row] = new WilcockTableRow($"grain:{row}", cells);
        }
        return new WilcockSourceTable("stab.grainSizeDistributions", "stab", "input",
            "STAB grain-size distributions", columns.ToArray(), rows);
    }

    private static WilcockSourceTable SingleColumnInputTable(string id, string section, string titleKey,
        string column, string columnKey, string[]? values)
        => new(id, section, "input", titleKey,
            [new(column, columnKey, "number", true)],
            (values ?? []).Select((value, index) =>
                new WilcockTableRow($"value:{index}", [value])).ToArray());

    private static WilcockSourceTable BuildStabStatistics(StabResults result, CultureInfo culture)
    {
        string[] ids = ["dMin", "dMax", "d50", "dg", "sigmaG", "fineFraction"];
        string[] titles = ["D min", "D max", "D50", "Dg", "Sigma G", "Fine fraction"];
        double[][] arrays = [result.DMin, result.DMax, result.D50, result.Dg, result.SigmaG, result.Fs];
        int count = arrays.Select(array => array?.Length ?? 0).DefaultIfEmpty().Max();
        var rows = new WilcockTableRow[count];
        for (int row = 0; row < count; row++)
            rows[row] = new WilcockTableRow($"case:{row}",
                arrays.Select(array => AtOrBlank(array, row, culture)).ToArray());
        return new WilcockSourceTable("stab.outputStatistics", "stab", "output",
            "STAB output statistics",
            ids.Select((id, index) => new WilcockTableColumn(id, titles[index], "number", false)).ToArray(),
            rows);
    }

    private static WilcockSourceTable BuildStateStatistics(StateResults result, CultureInfo culture)
    {
        (string ID, string TitleKey, double First, double Second)[] metrics =
        [
            ("dMin", "D min", result.FirstDMin, result.SecondDMin),
            ("dMax", "D max", result.FirstDMax, result.SecondDMax),
            ("d50", "D50", result.FirstD50, result.SecondD50),
            ("dg", "Dg", result.FirstDg, result.SecondDg),
            ("sigmaG", "Sigma G", result.FirstSigmaG, result.SecondSigmaG),
            ("fineFraction", "Fine fraction", result.FirstFs, result.SecondFs)
        ];
        return new WilcockSourceTable("state.outputStatistics", "state", "output",
            "STATE output statistics",
            [new("first", "First transport distribution", "number", false),
             new("second", "Second transport distribution", "number", false)],
            metrics.Select(metric => new WilcockTableRow(metric.ID,
                [FormatOutputValue(metric.First, culture), FormatOutputValue(metric.Second, culture)], metric.TitleKey)).ToArray());
    }

    private static WilcockChart[] BuildCharts(string mode,
        WilcockMethod.SBTM? sbtmInput, SBTMResults? sbtmResult,
        WilcockMethod.ISBTM? isbtmInput, ISBTMResults? isbtmResult,
        WilcockStabDraft stabDraft, StabResults? stabResult,
        WilcockStateDraft stateDraft, StateResults? stateResult, CultureInfo culture)
    {
        var charts = new List<WilcockChart>();
        List<WilcockChartSeries> btmSeries = [];
        string btmTitle = mode == "ISBTM" ? "ISBTM bed-load distribution" : "SBTM bed-load distribution";
        if (mode == "ISBTM" && isbtmInput is not null && isbtmResult is not null)
        {
            btmSeries.Add(Series(null, "Surface CDF", "#FFFF4500", 2,
                isbtmInput.GrainDiameter, isbtmResult.SurfaceCdf));
            btmSeries.Add(Series(null, "Transport CDF", "#FF7FFF00", 2,
                isbtmInput.GrainDiameter, isbtmInput.TransportCdf));
        }
        else if (mode != "ISBTM" && sbtmInput is not null && sbtmResult is not null)
        {
            btmSeries.Add(Series(null, "Surface CDF", "#FF7FFF00", 2,
                sbtmInput.GrainDiameter, sbtmInput.SurfaceCdf));
            btmSeries.Add(Series(null, "Transport CDF", "#FFFF4500", 2,
                sbtmInput.GrainDiameter, sbtmResult.CumulativeBedloadDistribution));
        }
        charts.Add(new WilcockChart("btm", "btm", btmTitle, "Grain size (mm)",
            "Cumulative finer (%)", btmSeries.ToArray()));

        var stateSeries = new List<WilcockChartSeries>();
        if (stateResult is not null)
        {
            string[] grains = stateDraft.GrainDiameter ?? [];
            // Plot arrays are the editable source inputs, as in the retained STATE control.
            double[] x = ParseArrayForChart(grains, culture);
            stateSeries.Add(Series(null, "First transport distribution", "#FFFF0000", 2, x,
                ParseArrayForChart(stateDraft.FirstTransportCdfGrainSize ?? [], culture)));
            stateSeries.Add(Series(null, "Second transport distribution", "#FF0000FF", 2, x,
                ParseArrayForChart(stateDraft.SecondTransportCdfGrainSize ?? [], culture)));
        }
        charts.Add(new WilcockChart("state", "state", "STATE transport-size distributions",
            "Grain size (mm)", "Percent finer", stateSeries.ToArray()));

        charts.Add(StabPlot(0, "Slope vs. channel width", "Channel width (m)", "Channel slope",
            BuildStabSlopeSeries(stabDraft, stabResult)));
        charts.Add(StabPlot(1, "Flow depth vs. channel width", "Channel width (m)", "Flow depth (m)",
            BuildStabDepthSeries(stabResult)));
        charts.Add(StabPlot(2, "Sediment supply vs. slope", "Sediment supply rate (kg/hr)", "Channel slope",
            BuildStabSupplySeries(stabDraft, stabResult, culture)));
        charts.Add(StabPlot(3, "Grain-size distributions", "Grain size (mm)", "Percent finer",
            BuildStabGrainSeries(stabDraft, stabResult, culture)));
        charts.Add(StabPlot(4, "Manning's n vs. channel width", "Channel width (m)", "Manning's n",
            BuildStabManningSeries(stabDraft, stabResult, culture)));
        charts.Add(StabPlot(5, "Relative depth vs. channel width", "Channel width (m)", "Relative depth",
            CaseSeries(stabResult?.B, stabResult?.RelativeDepth, "Case", 2)));
        charts.Add(StabPlot(6, "Froude number vs. channel width", "Channel width (m)", "Froude number",
            CaseSeries(stabResult?.B, stabResult?.F, "Case", 1)));
        charts.Add(StabPlot(7, "Armor ratio vs. channel width", "Channel width (m)", "Armor ratio",
            CaseSeries(stabResult?.B, stabResult?.Gamma, "Case", 1)));
        return charts.ToArray();
    }

    private static WilcockChart StabPlot(int index, string title, string xAxis, string yAxis,
        List<WilcockChartSeries> series)
        => new($"stab.{index}", "stab", title, xAxis, yAxis, series.ToArray());

    private static List<WilcockChartSeries> BuildStabSlopeSeries(WilcockStabDraft draft, StabResults? result)
    {
        var series = CaseSeries(result?.B, result?.S, "Case", 2);
        series.AddRange(CaseSeries(result?.B, result?.SSpecN, "Case with specified n", 1, alpha: 128));
        if (draft.UseThresholdSlope && result?.TcS is not null)
            series.AddRange(CaseSeries(result.B, result.TcS, "Case with threshold slope", 1.5,
                alpha: 200, include: values => values.Any(value => value > 0.0)));
        return series;
    }

    private static List<WilcockChartSeries> BuildStabDepthSeries(StabResults? result)
    {
        var series = CaseSeries(result?.B, result?.H, "Case", 2);
        series.AddRange(CaseSeries(result?.B, result?.HSpecN, "Case with specified n", 1, alpha: 128));
        return series;
    }

    private static List<WilcockChartSeries> BuildStabSupplySeries(WilcockStabDraft draft, StabResults? result,
        CultureInfo culture)
    {
        var series = new List<WilcockChartSeries>();
        if (result?.QTKgPerHr is not null && result.TS is not null)
        {
            int count = Math.Min(result.QTKgPerHr.Length, result.TS.Length);
            for (int index = 0; index < count; index++)
                series.Add(Series(index, "Case", CaseColor(index), 2,
                    [result.QTKgPerHr[index]], result.TS[index]));
        }
        if (result is not null)
        {
            double[] water = ParseArrayForChart(draft.WaterDischarges ?? [], culture);
            var userSupply = new WilcockChartPoint[Math.Min(water.Length, result.S?.Length ?? 0)];
            for (int index = 0; index < userSupply.Length; index++)
            {
                double[] slope = result.S![index] ?? [];
                double y = slope.Length > 0 ? slope[slope.Length / 2] : double.NaN;
                double x = water[index] * 2650.0 * 3600.0;
                userSupply[index] = new(double.IsFinite(x) ? x : null, double.IsFinite(y) ? y : null);
            }
            series.Add(new WilcockChartSeries(null, "Your sediment supply",
                "#FF008000", 0, userSupply));
        }
        return series;
    }

    private static List<WilcockChartSeries> BuildStabGrainSeries(WilcockStabDraft draft, StabResults? result,
        CultureInfo culture)
    {
        double[] inputX = ParseArrayForChart(draft.GrainDiameter ?? [], culture);
        double[][] inputY = (draft.TransportGrainSizes ?? []).Select(row => ParseArrayForChart(row ?? [], culture)).ToArray();
        var series = CaseSeries(inputX, inputY, "Case transport distribution", 2);
        if (result is not null)
            series.AddRange(PairedCaseSeries(result.GSDAtMeanWidthD, result.GSDAtMeanWidthFi,
                (draft.TransportGrainSizes ?? []).Length, "Surface distribution at mean width", 2, alpha: 128));
        return series;
    }

    private static List<WilcockChartSeries> BuildStabManningSeries(WilcockStabDraft draft, StabResults? result,
        CultureInfo culture)
    {
        var series = CaseSeries(result?.B, result?.ND, "Case base Manning's n", 2);
        series.AddRange(CaseSeries(result?.B, result?.NC, "Case composite Manning's n", 3));
        if (result is not null
            && double.TryParse(draft.Nspec, NumberStyles.Float | NumberStyles.AllowThousands,
                culture, out double nSpec) && nSpec > 0.0)
            series.Add(Series(null, "Specified n", "#FF008000", 2, result.B ?? [],
                Enumerable.Repeat(nSpec, result.B?.Length ?? 0).ToArray()));
        return series;
    }

    private static List<WilcockChartSeries> CaseSeries(double[]? x, double[][]? y, string labelKey,
        double width, int? alpha = null, Func<double[], bool>? include = null)
    {
        var series = new List<WilcockChartSeries>();
        if (x is null || y is null) return series;
        for (int index = 0; index < y.Length; index++)
        {
            double[] values = y[index] ?? [];
            if (include is not null && !include(values)) continue;
            string color = alpha is null ? CaseColor(index) : WithAlpha(CaseColor(index), alpha.Value);
            series.Add(Series(index, labelKey, color, width, x, values));
        }
        return series;
    }

    private static List<WilcockChartSeries> PairedCaseSeries(double[][]? x, double[][]? y, int caseCount,
        string labelKey, double width, int? alpha = null)
    {
        var series = new List<WilcockChartSeries>();
        if (x is null || y is null) return series;
        int count = Math.Min(caseCount, Math.Min(x.Length, y.Length));
        for (int index = 0; index < count; index++)
        {
            double[]? caseX = x[index];
            double[]? caseY = y[index];
            if (caseX is null || caseY is null) continue;
            string color = alpha is null ? CaseColor(index) : WithAlpha(CaseColor(index), alpha.Value);
            series.Add(Series(index, labelKey, color, width, caseX, caseY));
        }
        return series;
    }

    private static WilcockChartSeries Series(int? caseIndex, string labelKey, string color,
        double width, double[]? x, double[]? y)
    {
        if (x is null || y is null)
            return new WilcockChartSeries(caseIndex, labelKey, color, width, []);
        int count = Math.Min(x.Length, y.Length);
        var points = new List<WilcockChartPoint>(count);
        for (int index = 0; index < count; index++)
            points.Add(new WilcockChartPoint(
                double.IsFinite(x[index]) ? x[index] : null,
                double.IsFinite(y[index]) ? y[index] : null));
        return new WilcockChartSeries(caseIndex, labelKey, color, width, points.ToArray());
    }

    private static string CaseColor(int index) => (index % 8) switch
    {
        0 => "#FF0000FF", 1 => "#FFFF0000", 2 => "#FF008000", 3 => "#FFFFA500",
        4 => "#FF800080", 5 => "#FFA52A2A", 6 => "#FF008B8B", _ => "#FFFF00FF"
    };

    private static string WithAlpha(string color, int alpha) => $"#{alpha:X2}{color[3..]}";

    private static Dictionary<string, object?> ToJSON(object value, out bool hasNonFinite)
    {
        bool found = false;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(item => item.CanRead).OrderBy(item => item.MetadataToken))
        {
            result[ToCamelCase(property.Name)] = ToJSONValue(property.GetValue(value), ref found);
        }
        hasNonFinite = found;
        return result;
    }

    private static object? ToJSONValue(object? value, ref bool hasNonFinite)
    {
        if (value is null) return null;
        if (value is double number)
        {
            if (double.IsFinite(number)) return number;
            hasNonFinite = true;
            return null;
        }
        if (value is float single)
        {
            if (float.IsFinite(single)) return single;
            hasNonFinite = true;
            return null;
        }
        if (value is Array array)
        {
            if (array.Rank == 1)
            {
                var items = new object?[array.Length];
                for (int index = 0; index < array.Length; index++)
                    items[index] = ToJSONValue(array.GetValue(index), ref hasNonFinite);
                return items;
            }
            if (array.Rank == 2)
            {
                var rows = new object?[array.GetLength(0)][];
                for (int row = 0; row < array.GetLength(0); row++)
                {
                    var cells = new object?[array.GetLength(1)];
                    for (int column = 0; column < array.GetLength(1); column++)
                        cells[column] = ToJSONValue(array.GetValue(row, column), ref hasNonFinite);
                    rows[row] = cells;
                }
                return rows;
            }
        }
        return value;
    }

    private static string ToCamelCase(string value)
        => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static WilcockDraft FromSourceDefaults(WilcockMethod source, CultureInfo culture)
        => new(culture.Name, "SBTM",
            new WilcockSbtmDraft(Raw(source.Sbtm.ShearVelocity, culture), Raw(source.Sbtm.WaterDensity, culture),
                Raw(source.Sbtm.SedimentDensity, culture), Raw(source.Sbtm.GrainDiameter, culture),
                Raw(source.Sbtm.SurfaceCdf, culture)),
            new WilcockIsbtmDraft(Raw(source.Isbtm.TotalTransportRate, culture),
                Raw(source.Isbtm.GrainDiameter, culture), Raw(source.Isbtm.TransportCdf, culture)),
            new WilcockStabDraft(Raw(source.Stab.WaterDischarges, culture), Raw(source.Stab.SedimentSupplyRates, culture),
                source.Stab.TransportGrainSizes.Select(row => Raw(row, culture)).ToArray(),
                Raw(source.Stab.BMin, culture), Raw(source.Stab.BMax, culture), Raw(source.Stab.Z, culture),
                Raw(source.Stab.Ns, culture), Raw(source.Stab.Nspec, culture), source.Stab.UseThresholdSlope,
                Raw(source.Stab.TcTauStarC, culture), Raw(source.Stab.TcD50, culture), Raw(source.Stab.TcD84, culture),
                Raw(source.Stab.TcM, culture), Raw(source.Stab.GrainDiameter, culture)),
            new WilcockStateDraft(Raw(source.State.GrainDiameter, culture),
                Raw(source.State.FirstTransportCDFGrainSize, culture), Raw(source.State.SecondTransportCDFGrainSize, culture),
                Raw(source.State.TransportRates, culture), Raw(source.State.Slopes, culture)),
            "case:0", 0);

    private static WilcockDraft CompleteForWire(WilcockDraft draft, CultureInfo culture)
    {
        WilcockSbtmDraft sbtm = draft.Sbtm ?? EmptySbtm();
        WilcockIsbtmDraft isbtm = draft.Isbtm ?? EmptyIsbtm();
        WilcockStabDraft stab = draft.Stab ?? EmptyStab();
        WilcockStateDraft state = draft.State ?? EmptyState();
        return new WilcockDraft(
            string.IsNullOrWhiteSpace(draft.NumberCulture) ? culture.Name : draft.NumberCulture,
            NormalizeMode(draft.BtmMode),
            sbtm with
            {
                ShearVelocity = sbtm.ShearVelocity ?? "",
                WaterDensity = sbtm.WaterDensity ?? "",
                SedimentDensity = sbtm.SedimentDensity ?? "",
                GrainDiameter = sbtm.GrainDiameter ?? [],
                SurfaceCdf = sbtm.SurfaceCdf ?? []
            },
            isbtm with
            {
                TotalTransportRate = isbtm.TotalTransportRate ?? "",
                GrainDiameter = isbtm.GrainDiameter ?? [],
                TransportCdf = isbtm.TransportCdf ?? []
            },
            stab with
            {
                WaterDischarges = stab.WaterDischarges ?? [],
                SedimentSupplyRates = stab.SedimentSupplyRates ?? [],
                TransportGrainSizes = stab.TransportGrainSizes ?? [],
                BMin = stab.BMin ?? "",
                BMax = stab.BMax ?? "",
                Z = stab.Z ?? "",
                Ns = stab.Ns ?? "",
                Nspec = stab.Nspec ?? "",
                TcTauStarC = stab.TcTauStarC ?? "",
                TcD50 = stab.TcD50 ?? "",
                TcD84 = stab.TcD84 ?? "",
                TcM = stab.TcM ?? "",
                GrainDiameter = stab.GrainDiameter ?? []
            },
            state with
            {
                GrainDiameter = state.GrainDiameter ?? [],
                FirstTransportCdfGrainSize = state.FirstTransportCdfGrainSize ?? [],
                SecondTransportCdfGrainSize = state.SecondTransportCdfGrainSize ?? [],
                TransportRates = state.TransportRates ?? [],
                Slopes = state.Slopes ?? []
            },
            draft.SelectedCaseID,
            draft.SelectedPlotIndex);
    }

    private static WilcockDraft CompleteForMutation(WilcockDraft draft)
        => CompleteForWire(draft, ResolveCulture(draft.NumberCulture));

    private static WilcockSbtmDraft EmptySbtm() => new("", "", "", [], []);
    private static WilcockIsbtmDraft EmptyIsbtm() => new("", [], []);
    private static WilcockStabDraft EmptyStab() => new([], [], [], "", "", "", "", "", false, "", "", "", "", []);
    private static WilcockStateDraft EmptyState() => new([], [], [], [], []);

    private static CultureInfo ResolveCulture(string? name)
    {
        if (name is null) return CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(UnsupportedNumberCulture);
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(name.Replace('_', '-'));
            if (string.IsNullOrEmpty(culture.Name)) throw new ArgumentException(UnsupportedNumberCulture);
            return culture;
        }
        catch (CultureNotFoundException)
        {
            throw new ArgumentException(UnsupportedNumberCulture);
        }
    }

    private static string NormalizeMode(string? mode) => mode == "ISBTM" ? "ISBTM" : "SBTM";

    private static int ParseCaseIndex(string? id, int count)
    {
        const string prefix = "case:";
        if (id is null || !id.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index < 0 || index >= count)
            throw new ArgumentException(InvalidCase);
        return index;
    }

    private static T[] RemoveAtIfPresent<T>(T[] source, int index)
        => index >= 0 && index < source.Length
            ? source.Where((_, ordinal) => ordinal != index).ToArray()
            : source;

    private static double[] ParseArrayForAction(string[]? values, CultureInfo culture)
    {
        if (values is null) throw new ArgumentException(InvalidInput);
        var parsed = new double[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(values[index], NumberStyles.Float | NumberStyles.AllowThousands, culture,
                    out parsed[index]) || !double.IsFinite(parsed[index]))
                throw new ArgumentException(InvalidInput);
        }
        return parsed;
    }

    private static double[] ParseArrayForChart(string[] values, CultureInfo culture)
    {
        var parsed = new double[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(values[index], NumberStyles.Float | NumberStyles.AllowThousands,
                    culture, out double item) || !double.IsFinite(item))
                item = double.NaN;
            parsed[index] = item;
        }
        return parsed;
    }

    private static string[] Raw(double[] values, CultureInfo culture) => values.Select(value => Raw(value, culture)).ToArray();
    private static string Raw(double value, CultureInfo culture) => value.ToString("R", culture);
    private static string AtOrBlank(string[] values, int index) => index < values.Length ? values[index] : "";
    private static string AtOrBlank(double[]? values, int index, CultureInfo culture)
        => values is not null && index < values.Length && double.IsFinite(values[index])
            ? Raw(values[index], culture)
            : "";
    private static string AtOrDefault(string[] values, int index, string fallback)
        => index < values.Length ? values[index] : fallback;

    private sealed record SectionOutcome<TInput, TResult>(TInput? Input, TResult? Result, string? ErrorKey)
        where TInput : class where TResult : class;

    private sealed class InputShapeException(string path, string messageKey) : Exception
    {
        public string Path { get; } = path;
        public string MessageKey { get; } = messageKey;
    }
}
