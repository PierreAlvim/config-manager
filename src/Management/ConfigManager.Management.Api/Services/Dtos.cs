using ConfigManager.Contracts;
using ConfigManager.Management.Api.Domain;

namespace ConfigManager.Management.Api.Services;

public sealed record ReviewDto(string Reviewer, string Decision, string? Comment, DateTimeOffset At);

public sealed record DiffEntry(string FlagKey, string Type, FlagDefinition? Before, FlagDefinition? After);

public sealed record ChangeRequestDetail(
    Guid Id, string Namespace, string Environment, string Title, string Description, string Status,
    string Author, DateTimeOffset CreatedAt, long BaseVersion, long? AppliedVersion, int RequiredApprovals,
    IReadOnlyList<ChangeOperation> Operations, IReadOnlyList<ReviewDto> Reviews, IReadOnlyList<DiffEntry> Diff,
    bool CanApprove, bool CanCancel);

public sealed record ChangeRequestSummary(
    Guid Id, string Namespace, string Environment, string Title, string Status, string Author,
    DateTimeOffset CreatedAt, long BaseVersion, long? AppliedVersion, int RequiredApprovals, int Approvals);

/// <summary>Outcome of submitting a change: applied (version) or a change request was opened.</summary>
public sealed record SubmitResult(long? AppliedVersion, ChangeRequestDetail? ChangeRequest);

public sealed record ChangeSubmission(string Title, string? Description, bool ForceChangeRequest, List<ChangeOperation> Operations);
