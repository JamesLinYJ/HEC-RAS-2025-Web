using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("Ras.Backend.Tests")]

namespace Ras.Backend;

internal sealed record InitialConditionsTransactionRequest(
    string ProjectPath,
    string ModelPath,
    string StagedModelPath,
    string OperationID,
    string RequestDigest,
    string ExpectedProjectRevision,
    string ExpectedModelRevision,
    string NewProjectXml,
    string ConnectionName,
    string? DependencyPath = null,
    string? ExpectedDependencyRevision = null);

internal sealed record InitialConditionsTransactionOutcome(
    string State,
    string OperationID,
    string RequestDigest,
    string ProjectPath,
    string ModelPath,
    string? ProjectRevision,
    string? ModelRevision,
    string? ProjectXml,
    string? ConnectionName,
    bool AlreadyCommitted,
    string? ErrorMessage);

internal sealed record InitialConditionsTransactionStatus(
    string State,
    InitialConditionsTransactionOutcome? Receipt,
    string? ErrorMessage);

internal enum InitialConditionsTransactionFailpoint
{
    AfterPrepare,
    AfterModelPublish,
    AfterProjectPublish
}

internal enum InitialConditionsTransactionFailpointAction
{
    Continue,
    Interrupt,
    Fail
}

/// <summary>
/// Journals the two source-file replacements needed to attach an initial condition.
/// The files are published individually; this deliberately does not claim a POSIX
/// atomic pair. Recovery only writes a target whose current SHA is the recorded old
/// or new SHA, so it never replaces an independently changed source file.
/// </summary>
internal static class InitialConditionsTransaction
{
    private const int JournalVersion = 1;
    private const string NotFound = "notFound";
    private const string Prepared = "prepared";
    private const string Committed = "committed";
    private const string RolledBack = "rolledBack";
    private const string Conflict = "conflict";
    private const string StateDirectoryName = ".hecras-initial-conditions";
    private const string JournalName = "journal.json";
    private const string JournalNextName = "journal.next";
    private const string OldProjectName = "project.old";
    private const string OldModelName = "model.old";
    private const string NewProjectName = "project.new";
    private const string NewModelName = "model.new";
    private const int LockWaitMilliseconds = 60_000;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record Journal(
        int Version,
        string State,
        string Phase,
        string OperationID,
        string RequestDigest,
        string ProjectPath,
        string ModelPath,
        string ExpectedProjectRevision,
        string ExpectedModelRevision,
        string? DependencyPath,
        string? ExpectedDependencyRevision,
        string ProjectRevision,
        string ModelRevision,
        string ProjectXml,
        string ConnectionName,
        string? ErrorMessage);

    internal static InitialConditionsTransactionOutcome Commit(
        InitialConditionsTransactionRequest request,
        Func<InitialConditionsTransactionFailpoint, InitialConditionsTransactionFailpointAction>? failpoint = null)
    {
        NormalizedRequest normalized = Normalize(request);
        using var transactionLock = AcquireProjectLock(normalized.ProjectPath);
        string operationDirectory = OperationDirectory(normalized.ProjectPath, normalized.OperationID);

        Journal? existing = ReadJournalIfPresent(operationDirectory, out string? journalError);
        if (journalError is not null)
            return ConflictOutcome(normalized, journalError);
        if (existing is not null)
        {
            if (!JournalMatchesRequest(existing, normalized))
                return ConflictOutcome(normalized, "An initial-conditions operation ID was reused with a different request.");
            if (existing.State == Conflict)
                return Outcome(existing, Conflict, alreadyCommitted: false, existing.ErrorMessage);
            InitialConditionsTransactionOutcome recovered = RecoverUnderLock(
                operationDirectory, existing, failpoint);
            if (recovered.State != NotFound) return recovered;
        }
        else if (Directory.Exists(operationDirectory))
        {
            if (!TryRemoveInterruptedPreparation(operationDirectory, normalized))
                return ConflictOutcome(normalized, "An incomplete initial-conditions preparation conflicts with the current source files.");
        }

        string currentProjectRevision = FileRevision(normalized.ProjectPath);
        string currentModelRevision = FileRevision(normalized.ModelPath);
        if (!EqualsRevision(currentProjectRevision, normalized.ExpectedProjectRevision) ||
            !EqualsRevision(currentModelRevision, normalized.ExpectedModelRevision) ||
            !DependencyMatches(normalized.DependencyPath, normalized.ExpectedDependencyRevision))
        {
            Journal conflictJournal = CreateJournal(normalized,
                errorMessage: "The project, model, or consumed result changed before the initial-conditions transaction began.");
            CreateOperationDirectory(operationDirectory);
            WriteJournal(operationDirectory, conflictJournal);
            return Outcome(conflictJournal, Conflict, alreadyCommitted: false, conflictJournal.ErrorMessage);
        }

        byte[] newProjectBytes;
        try
        {
            newProjectBytes = StrictUtf8.GetBytes(normalized.NewProjectXml);
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("The saved project XML is not valid Unicode text.", nameof(request), error);
        }

        string stagedModelRevision = FileRevision(normalized.StagedModelPath);
        string newProjectRevision = Revision(newProjectBytes);
        Journal journal = CreateJournal(normalized, projectRevision: newProjectRevision, modelRevision: stagedModelRevision);
        CreateOperationDirectory(operationDirectory);
        try
        {
            CopyDurable(normalized.ProjectPath, Path.Combine(operationDirectory, OldProjectName));
            CopyDurable(normalized.ModelPath, Path.Combine(operationDirectory, OldModelName));
            WriteDurable(Path.Combine(operationDirectory, NewProjectName), newProjectBytes);
            CopyDurable(normalized.StagedModelPath, Path.Combine(operationDirectory, NewModelName));
            if (!EqualsRevision(FileRevision(Path.Combine(operationDirectory, OldProjectName)), normalized.ExpectedProjectRevision) ||
                !EqualsRevision(FileRevision(Path.Combine(operationDirectory, OldModelName)), normalized.ExpectedModelRevision) ||
                !EqualsRevision(FileRevision(Path.Combine(operationDirectory, NewProjectName)), journal.ProjectRevision) ||
                !EqualsRevision(FileRevision(Path.Combine(operationDirectory, NewModelName)), journal.ModelRevision))
                throw new IOException("The initial-conditions transaction staging files changed while being prepared.");
            WriteJournal(operationDirectory, journal);
        }
        catch
        {
            if (!File.Exists(Path.Combine(operationDirectory, JournalName)))
                TryDeleteInterruptedPreparation(operationDirectory);
            throw;
        }

        return CompletePreparedTransaction(operationDirectory, journal, failpoint, alreadyCommitted: false);
    }

    /// <summary>Reads one operation's durable state without creating locks or changing files.</summary>
    internal static InitialConditionsTransactionStatus ReadStatus(
        string projectPath,
        string operationID,
        string requestDigest)
    {
        string canonicalProject = CanonicalPath(projectPath);
        string normalizedDigest = NormalizeSHA256(requestDigest, nameof(requestDigest));
        ValidateOperationID(operationID);
        string operationDirectory = OperationDirectory(canonicalProject, operationID);
        Journal? journal = ReadJournalIfPresent(operationDirectory, out string? journalError);
        if (journalError is not null)
            return new(Conflict, null, journalError);
        if (journal is null) return new(NotFound, null, null);
        if (!PathComparer.Equals(journal.ProjectPath, canonicalProject) ||
            !string.Equals(journal.OperationID, operationID, StringComparison.Ordinal) ||
            !EqualsRevision(journal.RequestDigest, normalizedDigest))
            return new(Conflict, null, "The saved initial-conditions operation does not match this request.");

        string state = ObservedState(journal, out string? errorMessage);
        InitialConditionsTransactionOutcome? receipt = state == Committed
            ? Outcome(journal, Committed, alreadyCommitted: true, errorMessage: null)
            : null;
        return new(state, receipt, errorMessage ?? (state == Conflict ? journal.ErrorMessage : null));
    }

    /// <summary>
    /// Completes or safely rolls forward a previously prepared operation under the
    /// project-scoped cross-process lock. It never invokes source model operations.
    /// </summary>
    internal static InitialConditionsTransactionOutcome Recover(
        string projectPath,
        string operationID,
        string requestDigest,
        Func<InitialConditionsTransactionFailpoint, InitialConditionsTransactionFailpointAction>? failpoint = null)
    {
        string canonicalProject = CanonicalExistingFile(projectPath, nameof(projectPath));
        string normalizedDigest = NormalizeSHA256(requestDigest, nameof(requestDigest));
        ValidateOperationID(operationID);
        using var transactionLock = AcquireProjectLock(canonicalProject);
        string operationDirectory = OperationDirectory(canonicalProject, operationID);
        Journal? journal = ReadJournalIfPresent(operationDirectory, out string? journalError);
        if (journalError is not null)
            return BareOutcome(Conflict, operationID, normalizedDigest, canonicalProject, "", journalError);
        if (journal is null)
            return BareOutcome(NotFound, operationID, normalizedDigest, canonicalProject, "", null);
        if (!PathComparer.Equals(journal.ProjectPath, canonicalProject) ||
            !string.Equals(journal.OperationID, operationID, StringComparison.Ordinal) ||
            !EqualsRevision(journal.RequestDigest, normalizedDigest))
            return Outcome(journal, Conflict, alreadyCommitted: false,
                "The saved initial-conditions operation does not match this request.");
        if (journal.State == Conflict)
            return Outcome(journal, Conflict, alreadyCommitted: false, journal.ErrorMessage);
        if (journal.State == Committed)
            return VerifyCommittedAndCleanup(operationDirectory, journal, alreadyCommitted: true);
        if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
            return RollBackForChangedDependency(operationDirectory, journal);
        return RecoverUnderLock(operationDirectory, journal, failpoint);
    }

    private static InitialConditionsTransactionOutcome RecoverUnderLock(
        string operationDirectory,
        Journal journal,
        Func<InitialConditionsTransactionFailpoint, InitialConditionsTransactionFailpointAction>? failpoint)
    {
        if (journal.State == Committed) return VerifyCommittedAndCleanup(operationDirectory, journal, alreadyCommitted: true);
        if (journal.State == Conflict) return Outcome(journal, Conflict, false, journal.ErrorMessage);
        if (journal.State is not (Prepared or RolledBack))
            return Outcome(journal, Conflict, false, "The initial-conditions transaction journal has an unsupported state.");
        if (!File.Exists(journal.ProjectPath) || !File.Exists(journal.ModelPath))
            return MarkConflict(operationDirectory, journal, "The project or model disappeared during initial-conditions recovery.");

        return CompletePreparedTransaction(operationDirectory, journal, failpoint, alreadyCommitted: false);
    }

    private static InitialConditionsTransactionOutcome CompletePreparedTransaction(
        string operationDirectory,
        Journal initialJournal,
        Func<InitialConditionsTransactionFailpoint, InitialConditionsTransactionFailpointAction>? failpoint,
        bool alreadyCommitted)
    {
        Journal journal = initialJournal;
        if (!ArtifactsMatch(operationDirectory, journal, includeBackups: true))
            return MarkConflict(operationDirectory, journal, "The initial-conditions transaction staging or backup files are incomplete.");

        if (!TargetsAreKnown(journal, out string? currentProjectRevision, out string? currentModelRevision))
        {
            _ = TryRollback(operationDirectory, journal, out string? rollbackError);
            return MarkConflict(operationDirectory, journal,
                "The project or model changed during initial-conditions recovery." +
                (rollbackError is null ? "" : " " + rollbackError));
        }
        if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
            return RollBackForChangedDependency(operationDirectory, journal);

        if (EqualsRevision(currentProjectRevision!, journal.ProjectRevision) &&
            EqualsRevision(currentModelRevision!, journal.ModelRevision))
        {
            if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
                return RollBackForChangedDependency(operationDirectory, journal);
            journal = journal with { State = Committed, Phase = "committed", ErrorMessage = null };
            WriteJournal(operationDirectory, journal);
            CleanupCommittedArtifacts(operationDirectory, journal);
            return Outcome(journal, Committed, alreadyCommitted, null);
        }

        try
        {
            if (TakeFailpoint(failpoint, InitialConditionsTransactionFailpoint.AfterPrepare) == InitialConditionsTransactionFailpointAction.Interrupt)
                return Outcome(journal, Prepared, alreadyCommitted, "The transaction is prepared and can be recovered by retrying attach.");

            if (!EqualsRevision(currentModelRevision!, journal.ModelRevision))
            {
                Publish(operationDirectory, journal, isProject: false, currentModelRevision!);
                currentModelRevision = journal.ModelRevision;
                journal = journal with { State = Prepared, Phase = "modelPublished", ErrorMessage = null };
                WriteJournal(operationDirectory, journal);
            }
            InitialConditionsTransactionFailpointAction afterModel = TakeFailpoint(
                failpoint, InitialConditionsTransactionFailpoint.AfterModelPublish);
            if (afterModel == InitialConditionsTransactionFailpointAction.Interrupt)
                return Outcome(journal, Prepared, alreadyCommitted, "The transaction is prepared and can be recovered by retrying attach.");
            if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
                throw new IOException("The consumed result file changed while the staged model was being published.");

            currentProjectRevision = FileRevision(journal.ProjectPath);
            if (!EqualsRevision(currentProjectRevision, journal.ProjectRevision))
            {
                if (!EqualsRevision(currentProjectRevision, journal.ExpectedProjectRevision))
                {
                    _ = TryRollback(operationDirectory, journal, out string? rollbackError);
                    return MarkConflict(operationDirectory, journal,
                        "The project changed before its staged initial-condition connection could be published." +
                        (rollbackError is null ? "" : " " + rollbackError));
                }
                Publish(operationDirectory, journal, isProject: true, currentProjectRevision);
                currentProjectRevision = journal.ProjectRevision;
                journal = journal with { State = Prepared, Phase = "projectPublished", ErrorMessage = null };
                WriteJournal(operationDirectory, journal);
            }
            InitialConditionsTransactionFailpointAction afterProject = TakeFailpoint(
                failpoint, InitialConditionsTransactionFailpoint.AfterProjectPublish);
            if (afterProject == InitialConditionsTransactionFailpointAction.Interrupt)
                return Outcome(journal, Prepared, alreadyCommitted, "The transaction is prepared and can be recovered by retrying attach.");

            if (!EqualsRevision(FileRevision(journal.ProjectPath), journal.ProjectRevision) ||
                !EqualsRevision(FileRevision(journal.ModelPath), journal.ModelRevision))
            {
                _ = TryRollback(operationDirectory, journal, out string? rollbackError);
                return MarkConflict(operationDirectory, journal,
                    "The project or model changed while the initial-condition pair was being published." +
                    (rollbackError is null ? "" : " " + rollbackError));
            }
            if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
                throw new IOException("The consumed result file changed while the initial-conditions pair was being published.");

            journal = journal with { State = Committed, Phase = "committed", ErrorMessage = null };
            WriteJournal(operationDirectory, journal);
            CleanupCommittedArtifacts(operationDirectory, journal);
            return Outcome(journal, Committed, alreadyCommitted, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (TryRollback(operationDirectory, journal, out string? rollbackError))
            {
                journal = journal with { State = RolledBack, Phase = "rolledBack", ErrorMessage = error.Message };
                try { WriteJournal(operationDirectory, journal); }
                catch (Exception journalWriteError) when (journalWriteError is IOException or UnauthorizedAccessException)
                {
                    return Outcome(initialJournal, Prepared, false,
                        error.Message + " Rollback completed but its journal update failed: " + journalWriteError.Message);
                }
                return Outcome(journal, RolledBack, false, error.Message);
            }
            return MarkConflict(operationDirectory, journal,
                rollbackError ?? "The initial-conditions transaction could not safely roll back.");
        }
    }

    private static bool TargetsAreKnown(Journal journal, out string? projectRevision, out string? modelRevision)
    {
        projectRevision = ExistingRevision(journal.ProjectPath);
        modelRevision = ExistingRevision(journal.ModelPath);
        return projectRevision is not null && modelRevision is not null &&
            (EqualsRevision(projectRevision, journal.ExpectedProjectRevision) || EqualsRevision(projectRevision, journal.ProjectRevision)) &&
            (EqualsRevision(modelRevision, journal.ExpectedModelRevision) || EqualsRevision(modelRevision, journal.ModelRevision));
    }

    private static void Publish(string operationDirectory, Journal journal, bool isProject, string currentRevision)
    {
        string target = isProject ? journal.ProjectPath : journal.ModelPath;
        string expectedOld = isProject ? journal.ExpectedProjectRevision : journal.ExpectedModelRevision;
        string expectedNew = isProject ? journal.ProjectRevision : journal.ModelRevision;
        string stagedName = isProject ? NewProjectName : NewModelName;
        if (EqualsRevision(currentRevision, expectedNew)) return;
        if (!EqualsRevision(currentRevision, expectedOld))
            throw new IOException("A source file changed before the initial-conditions transaction could publish it.");

        string staged = Path.Combine(operationDirectory, stagedName);
        if (!EqualsRevision(FileRevision(staged), expectedNew))
            throw new IOException("A staged initial-conditions source file failed its SHA-256 check.");
        string targetTemporary = target + ".initial-conditions-" + OperationKey(journal.OperationID) + ".pending";
        if (File.Exists(targetTemporary) || new FileInfo(targetTemporary).LinkTarget is not null)
        {
            if (new FileInfo(targetTemporary).LinkTarget is not null || !EqualsRevision(FileRevision(targetTemporary), expectedNew))
                throw new IOException("A conflicting initial-conditions publish temporary already exists.");
        }
        else
        {
            CopyDurable(staged, targetTemporary);
        }

        // Recheck as close as possible to the per-file atomic rename. The project
        // lock serializes our processes; SHA guards protect recovery from outside edits.
        string immediatelyBeforeMove = FileRevision(target);
        if (!EqualsRevision(immediatelyBeforeMove, expectedOld) && !EqualsRevision(immediatelyBeforeMove, expectedNew))
            throw new IOException("A source file changed immediately before initial-conditions publication.");
        if (EqualsRevision(immediatelyBeforeMove, expectedOld)) File.Move(targetTemporary, target, overwrite: true);
        else File.Delete(targetTemporary);
        if (!EqualsRevision(FileRevision(target), expectedNew))
            throw new IOException("A published initial-conditions source file failed its SHA-256 check.");
    }

    private static bool TryRollback(string operationDirectory, Journal journal, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            string? projectRevision = ExistingRevision(journal.ProjectPath);
            bool projectKnown = projectRevision is not null &&
                (EqualsRevision(projectRevision, journal.ExpectedProjectRevision) || EqualsRevision(projectRevision, journal.ProjectRevision));
            string? modelRevision = ExistingRevision(journal.ModelPath);
            bool modelKnown = modelRevision is not null &&
                (EqualsRevision(modelRevision, journal.ExpectedModelRevision) || EqualsRevision(modelRevision, journal.ModelRevision));

            if (projectKnown && EqualsRevision(projectRevision, journal.ProjectRevision))
                RestoreOldFile(operationDirectory, journal, isProject: true);
            if (modelKnown && EqualsRevision(modelRevision, journal.ModelRevision))
                RestoreOldFile(operationDirectory, journal, isProject: false);
            if (!projectKnown || !modelKnown)
            {
                errorMessage = "Known transaction outputs were rolled back; a newer external source file was left untouched.";
                return false;
            }
            if (!EqualsRevision(FileRevision(journal.ProjectPath), journal.ExpectedProjectRevision) ||
                !EqualsRevision(FileRevision(journal.ModelPath), journal.ExpectedModelRevision))
            {
                errorMessage = "The original source files could not be fully restored.";
                return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            errorMessage = error.Message;
            return false;
        }
    }

    private static void RestoreOldFile(string operationDirectory, Journal journal, bool isProject)
    {
        string target = isProject ? journal.ProjectPath : journal.ModelPath;
        string backup = Path.Combine(operationDirectory, isProject ? OldProjectName : OldModelName);
        string expectedOld = isProject ? journal.ExpectedProjectRevision : journal.ExpectedModelRevision;
        string expectedNew = isProject ? journal.ProjectRevision : journal.ModelRevision;
        string current = FileRevision(target);
        if (EqualsRevision(current, expectedOld)) return;
        if (!EqualsRevision(current, expectedNew) || !EqualsRevision(FileRevision(backup), expectedOld))
            throw new IOException("A source file or backup changed before rollback.");
        string temporary = target + ".initial-conditions-rollback-" + OperationKey(journal.OperationID) + ".pending";
        if (File.Exists(temporary) || new FileInfo(temporary).LinkTarget is not null)
        {
            if (new FileInfo(temporary).LinkTarget is not null || !EqualsRevision(FileRevision(temporary), expectedOld))
                throw new IOException("A conflicting initial-conditions rollback temporary already exists.");
        }
        else
        {
            CopyDurable(backup, temporary);
        }
        if (!EqualsRevision(FileRevision(target), expectedNew))
            throw new IOException("A source file changed immediately before initial-conditions rollback.");
        File.Move(temporary, target, overwrite: true);
        if (!EqualsRevision(FileRevision(target), expectedOld))
            throw new IOException("A rolled-back source file failed its SHA-256 check.");
    }

    private static InitialConditionsTransactionOutcome VerifyCommitted(Journal journal, bool alreadyCommitted)
    {
        if (!File.Exists(journal.ProjectPath) || !File.Exists(journal.ModelPath) ||
            !EqualsRevision(FileRevision(journal.ProjectPath), journal.ProjectRevision) ||
            !EqualsRevision(FileRevision(journal.ModelPath), journal.ModelRevision))
            return Outcome(journal, Conflict, false,
                "A source file changed after the initial-conditions transaction committed; no file was overwritten.");
        return Outcome(journal, Committed, alreadyCommitted, null);
    }

    private static InitialConditionsTransactionOutcome VerifyCommittedAndCleanup(
        string operationDirectory,
        Journal journal,
        bool alreadyCommitted)
    {
        InitialConditionsTransactionOutcome outcome = VerifyCommitted(journal, alreadyCommitted);
        if (outcome.State == Committed) CleanupCommittedArtifacts(operationDirectory, journal);
        return outcome;
    }

    private static string ObservedState(Journal journal, out string? errorMessage)
    {
        errorMessage = null;
        if (journal.State == Committed)
        {
            if (!File.Exists(journal.ProjectPath) || !File.Exists(journal.ModelPath) ||
                !EqualsRevision(FileRevision(journal.ProjectPath), journal.ProjectRevision) ||
                !EqualsRevision(FileRevision(journal.ModelPath), journal.ModelRevision))
            {
                errorMessage = "A source file changed after the initial-conditions transaction committed; no file was overwritten.";
                return Conflict;
            }
            return Committed;
        }
        if (journal.State == Conflict) return Conflict;
        if (journal.State == RolledBack)
        {
            if (!File.Exists(journal.ProjectPath) || !File.Exists(journal.ModelPath) ||
                !EqualsRevision(FileRevision(journal.ProjectPath), journal.ExpectedProjectRevision) ||
                !EqualsRevision(FileRevision(journal.ModelPath), journal.ExpectedModelRevision))
            {
                errorMessage = "The project or model changed after the initial-conditions transaction was rolled back.";
                return Conflict;
            }
            if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
            {
                errorMessage = "The consumed result file changed before the initial-conditions transaction could be retried.";
                return Conflict;
            }
            return RolledBack;
        }
        if (journal.State != Prepared)
        {
            errorMessage = "The project or model changed during initial-conditions recovery.";
            return Conflict;
        }
        if (!TargetsAreKnown(journal, out _, out _))
        {
            errorMessage = "The project or model changed during initial-conditions recovery. Retry attach to safely roll back this prepared operation.";
            return Prepared;
        }
        if (!DependencyMatches(journal.DependencyPath, journal.ExpectedDependencyRevision))
        {
            errorMessage = "The consumed result file changed before the initial-conditions transaction could be recovered. Retry attach to safely roll back this prepared operation.";
            return Prepared;
        }
        return Prepared;
    }

    private static InitialConditionsTransactionOutcome MarkConflict(string operationDirectory, Journal journal, string message)
    {
        Journal conflict = journal with { State = Conflict, ErrorMessage = message };
        try { WriteJournal(operationDirectory, conflict); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Outcome(journal, Conflict, false, message + " The conflict could not be journaled: " + error.Message);
        }
        return Outcome(conflict, Conflict, false, message);
    }

    private static InitialConditionsTransactionOutcome RollBackForChangedDependency(string operationDirectory, Journal journal)
    {
        const string message = "The consumed result file changed before the initial-conditions transaction could be completed.";
        if (!ArtifactsMatch(operationDirectory, journal, includeBackups: true))
            return MarkConflict(operationDirectory, journal,
                message + " The transaction backups are incomplete, so no rollback was attempted.");
        if (!TryRollback(operationDirectory, journal, out string? rollbackError))
            return MarkConflict(operationDirectory, journal,
                message + " The paired files could not be safely rolled back: " + rollbackError);

        Journal rolledBack = journal with { State = RolledBack, Phase = "rolledBack", ErrorMessage = message };
        try { WriteJournal(operationDirectory, rolledBack); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Outcome(journal, Conflict, false,
                message + " The source files were restored, but the rollback journal update failed: " + error.Message);
        }
        return Outcome(rolledBack, RolledBack, false, message);
    }

    private static InitialConditionsTransactionOutcome Outcome(
        Journal journal,
        string state,
        bool alreadyCommitted,
        string? errorMessage) => new(
            state,
            journal.OperationID,
            journal.RequestDigest,
            journal.ProjectPath,
            journal.ModelPath,
            journal.ProjectRevision,
            journal.ModelRevision,
            journal.ProjectXml,
            journal.ConnectionName,
            alreadyCommitted,
            errorMessage);

    private static InitialConditionsTransactionOutcome BareOutcome(
        string state,
        string operationID,
        string digest,
        string projectPath,
        string modelPath,
        string? errorMessage) => new(
            state, operationID, digest, projectPath, modelPath, null, null, null, null, false, errorMessage);

    private static InitialConditionsTransactionOutcome ConflictOutcome(NormalizedRequest request, string message) =>
        BareOutcome(Conflict, request.OperationID, request.RequestDigest, request.ProjectPath, request.ModelPath, message);

    private static Journal CreateJournal(
        NormalizedRequest request,
        string? projectRevision = null,
        string? modelRevision = null,
        string? errorMessage = null) => new(
        JournalVersion,
        projectRevision is null ? Conflict : Prepared,
        projectRevision is null ? "conflict" : "prepared",
        request.OperationID,
        request.RequestDigest,
        request.ProjectPath,
        request.ModelPath,
        request.ExpectedProjectRevision,
        request.ExpectedModelRevision,
        request.DependencyPath,
        request.ExpectedDependencyRevision,
        projectRevision ?? "",
        modelRevision ?? "",
        request.NewProjectXml,
        request.ConnectionName,
        errorMessage ?? (projectRevision is null
            ? "The project, model, or consumed result changed before the initial-conditions transaction began."
            : null));

    private static bool JournalMatchesRequest(Journal journal, NormalizedRequest request) =>
        journal.Version == JournalVersion &&
        string.Equals(journal.OperationID, request.OperationID, StringComparison.Ordinal) &&
        EqualsRevision(journal.RequestDigest, request.RequestDigest) &&
        PathComparer.Equals(journal.ProjectPath, request.ProjectPath) &&
        PathComparer.Equals(journal.ModelPath, request.ModelPath) &&
        PathComparerEqualsNullable(journal.DependencyPath, request.DependencyPath) &&
        EqualsRevision(journal.ExpectedProjectRevision, request.ExpectedProjectRevision) &&
        EqualsRevision(journal.ExpectedModelRevision, request.ExpectedModelRevision) &&
        EqualsRevisionNullable(journal.ExpectedDependencyRevision, request.ExpectedDependencyRevision) &&
        string.Equals(journal.ProjectXml, request.NewProjectXml, StringComparison.Ordinal) &&
        string.Equals(journal.ConnectionName, request.ConnectionName, StringComparison.Ordinal) &&
        (journal.State == Conflict || EqualsRevision(FileRevision(request.StagedModelPath), journal.ModelRevision));

    private sealed record NormalizedRequest(
        string ProjectPath,
        string ModelPath,
        string StagedModelPath,
        string OperationID,
        string RequestDigest,
        string ExpectedProjectRevision,
        string ExpectedModelRevision,
        string? DependencyPath,
        string? ExpectedDependencyRevision,
        string NewProjectXml,
        string ConnectionName);

    private static NormalizedRequest Normalize(InitialConditionsTransactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string projectPath = CanonicalExistingFile(request.ProjectPath, nameof(request.ProjectPath));
        string modelPath = CanonicalExistingFile(request.ModelPath, nameof(request.ModelPath));
        string stagedModelPath = CanonicalExistingFile(request.StagedModelPath, nameof(request.StagedModelPath));
        if (PathComparer.Equals(projectPath, modelPath) || PathComparer.Equals(modelPath, stagedModelPath) ||
            PathComparer.Equals(projectPath, stagedModelPath))
            throw new ArgumentException("The project, model, and staged model paths must be distinct.", nameof(request));
        if ((request.DependencyPath is null) != (request.ExpectedDependencyRevision is null))
            throw new ArgumentException("The consumed dependency path and revision must be provided together.", nameof(request));
        string? dependencyPath = request.DependencyPath is null
            ? null
            : CanonicalExistingFile(request.DependencyPath, nameof(request.DependencyPath));
        string? dependencyRevision = request.ExpectedDependencyRevision is null
            ? null
            : NormalizeSHA256(request.ExpectedDependencyRevision, nameof(request.ExpectedDependencyRevision));
        if (dependencyPath is not null &&
            (PathComparer.Equals(dependencyPath, projectPath) || PathComparer.Equals(dependencyPath, modelPath) ||
             PathComparer.Equals(dependencyPath, stagedModelPath)))
            throw new ArgumentException("The consumed dependency must be distinct from the project and model files.", nameof(request));
        ValidateOperationID(request.OperationID);
        if (string.IsNullOrWhiteSpace(request.NewProjectXml))
            throw new ArgumentException("The staged project XML is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ConnectionName))
            throw new ArgumentException("The initial-condition connection name is required.", nameof(request));
        return new(
            projectPath,
            modelPath,
            stagedModelPath,
            request.OperationID,
            NormalizeSHA256(request.RequestDigest, nameof(request.RequestDigest)),
            NormalizeSHA256(request.ExpectedProjectRevision, nameof(request.ExpectedProjectRevision)),
            NormalizeSHA256(request.ExpectedModelRevision, nameof(request.ExpectedModelRevision)),
            dependencyPath,
            dependencyRevision,
            request.NewProjectXml,
            request.ConnectionName);
    }

    private static string CanonicalExistingFile(string path, string parameter)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new ArgumentException("A fully qualified existing file path is required.", parameter);
        return SourceFilePaths.Canonical(path);
    }

    private static string CanonicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("A fully qualified project path is required.", nameof(path));
        return SourceFilePaths.Canonical(path);
    }

    private static void ValidateOperationID(string operationID)
    {
        if (string.IsNullOrWhiteSpace(operationID) || operationID.Length > 256 || operationID.Any(char.IsControl))
            throw new ArgumentException("A stable operation identity is required.", nameof(operationID));
    }

    private static string NormalizeSHA256(string value, string parameter)
    {
        if (!IsSHA256(value))
            throw new ArgumentException("A SHA-256 revision must contain exactly 64 hexadecimal characters.", parameter);
        return value.ToUpperInvariant();
    }

    private static bool IsSHA256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string ProjectStateRoot(string canonicalProjectPath)
    {
        string projectDirectory = Path.GetDirectoryName(canonicalProjectPath)
            ?? throw new IOException("The project has no parent directory for transaction recovery data.");
        string stateDirectory = Path.Combine(projectDirectory, StateDirectoryName);
        string stateRoot = Path.Combine(stateDirectory, HashText(canonicalProjectPath));
        if (new DirectoryInfo(stateDirectory).LinkTarget is not null || new DirectoryInfo(stateRoot).LinkTarget is not null)
            throw new IOException("The initial-conditions transaction state path is a symbolic link.");
        return stateRoot;
    }

    private static string OperationDirectory(string canonicalProjectPath, string operationID)
    {
        string operationsDirectory = Path.Combine(ProjectStateRoot(canonicalProjectPath), "operations");
        if (new DirectoryInfo(operationsDirectory).LinkTarget is not null)
            throw new IOException("The initial-conditions operation directory is a symbolic link.");
        string operationDirectory = Path.Combine(operationsDirectory, OperationKey(operationID));
        if (new DirectoryInfo(operationDirectory).LinkTarget is not null)
            throw new IOException("The initial-conditions operation directory is a symbolic link.");
        return operationDirectory;
    }

    private static string OperationKey(string operationID) => HashText(operationID);

    private static FileStream AcquireProjectLock(string canonicalProjectPath)
    {
        string root = ProjectStateRoot(canonicalProjectPath);
        EnsureDirectory(root);
        string lockPath = Path.Combine(root, "transaction.lock");
        if (new FileInfo(lockPath).LinkTarget is not null)
            throw new IOException("The initial-conditions transaction lock path is a symbolic link.");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.ElapsedMilliseconds < LockWaitMilliseconds)
            {
                Thread.Sleep(25);
            }
            if (timer.ElapsedMilliseconds >= LockWaitMilliseconds)
                throw new IOException("Timed out waiting for the project initial-conditions transaction lock.");
        }
    }

    private static void CreateOperationDirectory(string operationDirectory)
    {
        string parent = Path.GetDirectoryName(operationDirectory)
            ?? throw new IOException("The transaction journal has no parent directory.");
        EnsureDirectory(parent);
        if (Directory.Exists(operationDirectory) || File.Exists(operationDirectory))
            throw new IOException("The initial-conditions operation journal already exists.");
        Directory.CreateDirectory(operationDirectory);
        if (new DirectoryInfo(operationDirectory).LinkTarget is not null)
            throw new IOException("The initial-conditions operation directory is a symbolic link.");
    }

    private static void EnsureDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        DirectoryInfo? parent = Directory.GetParent(fullPath);
        if (parent is not null && parent.Exists && parent.LinkTarget is not null)
            throw new IOException("The initial-conditions transaction parent directory is a symbolic link.");
        Directory.CreateDirectory(fullPath);
        if (new DirectoryInfo(fullPath).LinkTarget is not null)
            throw new IOException("The initial-conditions transaction directory is a symbolic link.");
    }

    private static Journal? ReadJournalIfPresent(string operationDirectory, out string? errorMessage)
    {
        errorMessage = null;
        if (!Directory.Exists(operationDirectory)) return null;
        if (new DirectoryInfo(operationDirectory).LinkTarget is not null)
        {
            errorMessage = "The initial-conditions operation directory is a symbolic link.";
            return null;
        }
        string journalPath = Path.Combine(operationDirectory, JournalName);
        if (new FileInfo(journalPath).LinkTarget is not null)
        {
            errorMessage = "The initial-conditions journal is a symbolic link.";
            return null;
        }
        if (!File.Exists(journalPath)) return null;
        try
        {
            byte[] bytes = File.ReadAllBytes(journalPath);
            Journal? journal = JsonSerializer.Deserialize<Journal>(bytes, BackendJson.Options);
            if (journal is null || journal.Version != JournalVersion ||
                journal.State is not (Prepared or Committed or RolledBack or Conflict) ||
                journal.ProjectXml is null || journal.ConnectionName is null ||
                string.IsNullOrWhiteSpace(journal.OperationID) || string.IsNullOrWhiteSpace(journal.ProjectPath) ||
                string.IsNullOrWhiteSpace(journal.ModelPath) ||
                !Path.IsPathFullyQualified(journal.ProjectPath) || !Path.IsPathFullyQualified(journal.ModelPath) ||
                !PathComparer.Equals(SourceFilePaths.Canonical(journal.ProjectPath), journal.ProjectPath) ||
                !PathComparer.Equals(SourceFilePaths.Canonical(journal.ModelPath), journal.ModelPath) ||
                !IsSHA256(journal.RequestDigest) || !IsSHA256(journal.ExpectedProjectRevision) ||
                !IsSHA256(journal.ExpectedModelRevision) ||
                (journal.State != Conflict && (!IsSHA256(journal.ProjectRevision) || !IsSHA256(journal.ModelRevision))) ||
                (journal.State == Conflict &&
                 ((!string.IsNullOrEmpty(journal.ProjectRevision) && !IsSHA256(journal.ProjectRevision)) ||
                  (!string.IsNullOrEmpty(journal.ModelRevision) && !IsSHA256(journal.ModelRevision)))) ||
                ((journal.DependencyPath is null) != (journal.ExpectedDependencyRevision is null)) ||
                (journal.DependencyPath is not null &&
                 (!Path.IsPathFullyQualified(journal.DependencyPath) ||
                  !PathComparer.Equals(SourceFilePaths.Canonical(journal.DependencyPath), journal.DependencyPath) ||
                  !IsSHA256(journal.ExpectedDependencyRevision))))
            {
                errorMessage = "The initial-conditions transaction journal is invalid.";
                return null;
            }
            return journal;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            errorMessage = "The initial-conditions transaction journal could not be read: " + error.Message;
            return null;
        }
    }

    private static bool TryRemoveInterruptedPreparation(string operationDirectory, NormalizedRequest request)
    {
        if (!File.Exists(request.ProjectPath) || !File.Exists(request.ModelPath) ||
            !EqualsRevision(FileRevision(request.ProjectPath), request.ExpectedProjectRevision) ||
            !EqualsRevision(FileRevision(request.ModelPath), request.ExpectedModelRevision) ||
            !DependencyMatches(request.DependencyPath, request.ExpectedDependencyRevision))
            return false;
        string[] allowed = [JournalNextName, OldProjectName, OldModelName, NewProjectName, NewModelName];
        foreach (string entry in Directory.EnumerateFileSystemEntries(operationDirectory))
        {
            string name = Path.GetFileName(entry);
            FileInfo info = new(entry);
            if (!allowed.Contains(name, StringComparer.Ordinal) || info.LinkTarget is not null || (info.Attributes & FileAttributes.Directory) != 0)
                return false;
        }
        TryDeleteInterruptedPreparation(operationDirectory);
        return !Directory.Exists(operationDirectory);
    }

    private static void TryDeleteInterruptedPreparation(string operationDirectory)
    {
        if (!Directory.Exists(operationDirectory)) return;
        foreach (string name in new[] { JournalNextName, OldProjectName, OldModelName, NewProjectName, NewModelName })
        {
            string path = Path.Combine(operationDirectory, name);
            if (new FileInfo(path).LinkTarget is null && File.Exists(path)) File.Delete(path);
        }
        if (!Directory.EnumerateFileSystemEntries(operationDirectory).Any()) Directory.Delete(operationDirectory);
    }

    private static bool ArtifactsMatch(string operationDirectory, Journal journal, bool includeBackups)
    {
        bool newMatches = EqualsRevision(ExistingRevision(Path.Combine(operationDirectory, NewProjectName)), journal.ProjectRevision) &&
            EqualsRevision(ExistingRevision(Path.Combine(operationDirectory, NewModelName)), journal.ModelRevision);
        if (!newMatches) return false;
        return !includeBackups ||
            (EqualsRevision(ExistingRevision(Path.Combine(operationDirectory, OldProjectName)), journal.ExpectedProjectRevision) &&
             EqualsRevision(ExistingRevision(Path.Combine(operationDirectory, OldModelName)), journal.ExpectedModelRevision));
    }

    private static bool DependencyMatches(string? dependencyPath, string? expectedRevision)
    {
        if (dependencyPath is null || expectedRevision is null) return dependencyPath is null && expectedRevision is null;
        return File.Exists(dependencyPath) && EqualsRevision(FileRevision(dependencyPath), expectedRevision);
    }

    private static bool EqualsRevisionNullable(string? left, string? right) =>
        left is null ? right is null : right is not null && EqualsRevision(left, right);

    private static bool PathComparerEqualsNullable(string? left, string? right) =>
        left is null ? right is null : right is not null && PathComparer.Equals(left, right);

    private static void WriteJournal(string operationDirectory, Journal journal)
    {
        EnsureDirectory(operationDirectory);
        string nextPath = Path.Combine(operationDirectory, JournalNextName);
        if (new FileInfo(nextPath).LinkTarget is not null)
            throw new IOException("The initial-conditions journal temporary is a symbolic link.");
        if (File.Exists(nextPath)) File.Delete(nextPath);
        WriteDurable(nextPath, JsonSerializer.SerializeToUtf8Bytes(journal, BackendJson.Options));
        File.Move(nextPath, Path.Combine(operationDirectory, JournalName), overwrite: true);
    }

    private static void CleanupCommittedArtifacts(string operationDirectory, Journal journal)
    {
        foreach (string name in new[] { OldProjectName, OldModelName, NewProjectName, NewModelName, JournalNextName })
        {
            string path = Path.Combine(operationDirectory, name);
            try
            {
                if (new FileInfo(path).LinkTarget is null && File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var targetArtifacts = new[]
        {
            (journal.ProjectPath, journal.ExpectedProjectRevision, journal.ProjectRevision),
            (journal.ModelPath, journal.ExpectedModelRevision, journal.ModelRevision)
        };
        foreach ((string target, string oldRevision, string newRevision) in targetArtifacts)
        {
            foreach ((string suffix, string expectedRevision) in new[]
                     {
                         (".initial-conditions-" + OperationKey(journal.OperationID) + ".pending", newRevision),
                         (".initial-conditions-rollback-" + OperationKey(journal.OperationID) + ".pending", oldRevision)
                     })
            {
                string path = target + suffix;
                try
                {
                    FileInfo info = new(path);
                    if (info.LinkTarget is not null || !info.Exists) continue;
                    string revision = FileRevision(path);
                    if (EqualsRevision(revision, expectedRevision))
                        File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void CopyDurable(string source, string destination)
    {
        if (File.Exists(destination) || new FileInfo(destination).LinkTarget is not null)
            throw new IOException("An initial-conditions transaction artifact already exists.");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void WriteDurable(string destination, byte[] bytes)
    {
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private static string FileRevision(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Revision(stream);
    }

    private static string? ExistingRevision(string path) =>
        File.Exists(path) ? FileRevision(path) : null;

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Revision(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool EqualsRevision(string? left, string right) =>
        left is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static InitialConditionsTransactionFailpointAction TakeFailpoint(
        Func<InitialConditionsTransactionFailpoint, InitialConditionsTransactionFailpointAction>? failpoint,
        InitialConditionsTransactionFailpoint point)
    {
        InitialConditionsTransactionFailpointAction action = failpoint?.Invoke(point) ?? InitialConditionsTransactionFailpointAction.Continue;
        if (action == InitialConditionsTransactionFailpointAction.Fail)
            throw new IOException("Injected initial-conditions transaction failure at " + point + ".");
        return action;
    }
}
