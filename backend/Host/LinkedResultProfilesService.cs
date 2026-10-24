using System.Globalization;
using System.Security.Cryptography;
using Geospatial.Rendering;
using Ras.Helpers;
using Ras.Layers;
using Ras.Output;

namespace Ras.Backend;

public sealed record LinkedResultProfilesRequest(string ProjectPath, string ProjectXml,
    string ProjectRevision, ResultPlanCandidate Anchor, ResultPlanCandidateGroup[] SelectedGroups,
    int ProfileIndex = 0, CoreLayerRequestContext? CoreLayerContext = null);
public sealed record LinkedResultProfileStep(int ProfileIndex, string Name, string Time);
public sealed record LinkedResultProfileFrame(string ResultPath, string Revision, string Name,
    string? OutputBlockName, int? ProfileIndex, int? NearestProfileIndex, string? Time,
    string? UnavailableReason);
public sealed record LinkedResultProfilesSnapshot(string ProjectPath, string ProjectRevision,
    string AnchorPath, int ProfileIndex, LinkedResultProfileStep[] Steps,
    LinkedResultProfileFrame[] Frames);

/// <summary>
/// The Link Plans picker consumes whole source plan groups. The first Result
/// remains the timeline key, and the retained KeyedProfileState determines
/// every child index with its original 10 ms tolerance. No interpolation,
/// current-project Plan substitution or nearest-frame fallback is performed.
/// </summary>
public static class LinkedResultProfilesService
{
    public static LinkedResultProfilesSnapshot Read(LinkedResultProfilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Anchor);
        ArgumentNullException.ThrowIfNull(request.SelectedGroups);
        ResultPlanContextSnapshot context = ResultPlanContextService.Inspect(request.ProjectPath,
            request.ProjectXml, request.Anchor.ResultPath, request.Anchor.Revision,
            request.CoreLayerContext);
        if (!string.Equals(context.ProjectRevision, request.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (request.ProfileIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose an available linked result profile.");

        var selected = new List<ResultPlanCandidate> { new(context.ResultPath, context.Revision,
            context.AnchorName, context.AnchorPlanName, context.AnchorHasPlan) };
        var groups = new HashSet<(bool HasPlan, string? Name)>();
        foreach (ResultPlanCandidateGroup proposed in request.SelectedGroups)
        {
            if (!groups.Add((proposed.HasPlan, proposed.PlanName)))
                throw new ArgumentException("A linked result plan group was selected more than once.");
            ResultPlanCandidateGroup? actual = context.CandidateGroups.SingleOrDefault(group =>
                group.HasPlan == proposed.HasPlan && group.PlanName == proposed.PlanName);
            if (actual is null || proposed.Results is null || actual.Results.Length != proposed.Results.Length)
                throw new SourceFileChangedException();
            // Only identities participate in this comparison. UI display
            // labels are never trusted to select a file or a Plan.
            var identities = proposed.Results.Select(item =>
                (SourceFilePaths.Canonical(item.ResultPath), item.Revision.ToUpperInvariant())).ToHashSet();
            if (identities.Count != actual.Results.Length || actual.Results.Any(item =>
                !identities.Contains((SourceFilePaths.Canonical(item.ResultPath), item.Revision.ToUpperInvariant()))))
                throw new SourceFileChangedException();
            selected.AddRange(actual.Results);
        }
        if (selected.Select(item => item.ResultPath).Distinct(StringComparer.Ordinal).Count() != selected.Count)
            throw new ArgumentException("A linked Result source was selected more than once.");

        var opened = new List<Result>();
        try
        {
            foreach (ResultPlanCandidate candidate in selected)
            {
                EnsureUnchanged(candidate.ResultPath, candidate.Revision);
                if (!Result.TryLoad(candidate.ResultPath, out Result? result) || result is null)
                    throw new InvalidDataException("A selected linked Result could not be opened.");
                opened.Add(result);
            }
            IHorizontalOutputBlock? anchorBlock = opened[0].BaseOutputBlock;
            if (anchorBlock is null || anchorBlock.ProfileCount == 0)
            {
                VerifySources();
                return new(context.ProjectPath, context.ProjectRevision, context.ResultPath, 0, [],
                    selected.Select(item => new LinkedResultProfileFrame(item.ResultPath, item.Revision,
                        item.Name, null, null, null, null,
                        "The anchor Result has no stored horizontal output profiles.")).ToArray());
            }
            ValidateTimeline(anchorBlock);
            if (request.ProfileIndex >= anchorBlock.ProfileCount)
                throw new ArgumentOutOfRangeException(nameof(request), "Choose an available linked result profile.");
            LinkedResultProfileStep[] steps = Enumerable.Range(0, anchorBlock.ProfileCount)
                .Select(index => new LinkedResultProfileStep(index, anchorBlock.ProfileNames[index],
                    FormatTime(anchorBlock.Times[index]))).ToArray();
            ProfileState key = anchorBlock.CreateTrackedProfileState();
            ProfileState?[] states = opened.Select(result => result.BaseOutputBlock?.CreateTrackedProfileState()).ToArray();
            foreach (Result result in opened)
                if (result.BaseOutputBlock is { } block) ValidateTimeline(block);
            using var linked = new KeyedProfileState(key, states.OfType<ProfileState>(), TimeSpan.FromMilliseconds(10));
            linked.CurrentProfileIndex = request.ProfileIndex;
            linked.ForcePushStateToChildren();

            LinkedResultProfileFrame[] frames = selected.Select((candidate, index) =>
            {
                IHorizontalOutputBlock? block = opened[index].BaseOutputBlock;
                ProfileState? state = states[index];
                int? profile = index == 0 ? request.ProfileIndex : state?.CurrentProfileIndex;
                if (profile is < 0 || block is null || profile >= block.ProfileCount) profile = null;
                int? nearest = state is not null && linked.NearestIndexHints.TryGetValue(state, out int hint)
                    && hint >= 0 ? hint : null;
                string? unavailable = profile is null
                    ? "This Result has no profile within the 10 millisecond MultiResult timestamp tolerance." : null;
                return new LinkedResultProfileFrame(candidate.ResultPath, candidate.Revision, candidate.Name,
                    block?.Name, profile, nearest, profile is int matched ? FormatTime(block!.Times[matched]) : null,
                    unavailable);
            }).ToArray();
            VerifySources();
            return new(context.ProjectPath, context.ProjectRevision, context.ResultPath,
                request.ProfileIndex, steps, frames);
        }
        finally { foreach (Result result in opened) result.Dispose(); }

        void VerifySources()
        {
            EnsureUnchanged(request.ProjectPath, context.ProjectRevision);
            foreach (ResultPlanCandidate candidate in selected) EnsureUnchanged(candidate.ResultPath, candidate.Revision);
        }
    }

    private static void ValidateTimeline(IHorizontalOutputBlock block)
    {
        if (block.Times.Count != block.ProfileCount || block.ProfileNames.Count != block.ProfileCount)
            throw new InvalidDataException("The linked Result profile timestamps are inconsistent.");
    }
    private static string FormatTime(DateTime time) => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    private static void EnsureUnchanged(string path, string expected)
    {
        using FileStream stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }
}
