using System.Text.Json;
using ConfigManager.Contracts;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Services;

public sealed class ChangeRequestService(AppDbContext db, PublishingService publishing, NamespaceAuthorizer auth, ICurrentUser user)
{
    public async Task<List<ChangeRequestSummary>> ListAsync(string? ns, string? env, string? status, CancellationToken ct)
    {
        var query = db.ChangeRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(ns))
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            query = query.Where(c => c.NamespaceKey == ns);
        }
        else if (!user.IsAdmin)
        {
            var visible = (await auth.GetRolesAsync(ct)).Keys.ToList();
            query = query.Where(c => visible.Contains(c.NamespaceKey));
        }
        if (!string.IsNullOrEmpty(env)) query = query.Where(c => c.EnvironmentKey == env);
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<ChangeRequestStatus>(status, true, out var parsed))
                throw ApiException.BadRequest($"Unknown status '{status}'.");
            query = query.Where(c => c.Status == parsed);
        }

        var crs = await query.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(ct);
        var ids = crs.Select(c => c.Id).ToList();
        var approvals = await db.Reviews.AsNoTracking()
            .Where(r => ids.Contains(r.ChangeRequestId) && r.Decision == ReviewDecisions.Approved)
            .GroupBy(r => r.ChangeRequestId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return crs.Select(c => new ChangeRequestSummary(
            c.Id, c.NamespaceKey, c.EnvironmentKey, c.Title, Lower(c.Status), c.Author, c.CreatedAt,
            c.BaseVersion, c.AppliedVersion, c.RequiredApprovals, approvals.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<ChangeRequestDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var cr = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
                 ?? throw ApiException.NotFound($"Change request '{id}' does not exist.");
        await auth.RequireAsync(cr.NamespaceKey, NamespaceRole.Viewer, ct);
        return await BuildDetailAsync(cr, ct);
    }

    public async Task<ChangeRequestDetail> BuildDetailAsync(ChangeRequest cr, CancellationToken ct)
    {
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.ChangeRequestId == cr.Id).OrderBy(r => r.At).ToListAsync(ct);
        var ops = JsonSerializer.Deserialize<List<ChangeOperation>>(cr.Operations, SnapshotJson.Options) ?? [];
        var role = await auth.GetRoleAsync(cr.NamespaceKey, ct);

        var before = cr.Status == ChangeRequestStatus.Pending
            ? await publishing.GetCurrentFlagsAsync(cr.NamespaceKey, cr.EnvironmentKey, ct)
            : await publishing.GetFlagsAtVersionAsync(cr.NamespaceKey, cr.EnvironmentKey, cr.BaseVersion, ct);
        var diff = ops.Select(o => new DiffEntry(
            o.FlagKey, o.Type, before.GetValueOrDefault(o.FlagKey), o.Type == ChangeOperation.Upsert ? o.Flag : null)).ToList();

        var canApprove = ChangeRequestWorkflow.CheckCanReview(cr.Status, cr.Author, user.Username, role, reviews.Select(r => r.Reviewer)) == ReviewCheck.Ok;
        return new ChangeRequestDetail(
            cr.Id, cr.NamespaceKey, cr.EnvironmentKey, cr.Title, cr.Description, Lower(cr.Status), cr.Author, cr.CreatedAt,
            cr.BaseVersion, cr.AppliedVersion, cr.RequiredApprovals, ops,
            reviews.Select(r => new ReviewDto(r.Reviewer, r.Decision, r.Comment, r.At)).ToList(), diff,
            canApprove, ChangeRequestWorkflow.CanCancel(cr.Status, cr.Author, user.Username, role));
    }

    public async Task<ChangeRequestDetail> ApproveAsync(Guid id, string? comment, CancellationToken ct)
    {
        var head = await LoadHeadAsync(id, ct);
        var role = await auth.RequireAsync(head.NamespaceKey, NamespaceRole.Viewer, ct);

        string? conflictMessage = await publishing.RunLockedAsync(head.NamespaceKey, head.EnvironmentKey, async () =>
        {
            var cr = await db.ChangeRequests.SingleAsync(c => c.Id == id, ct);
            var reviews = await db.Reviews.Where(r => r.ChangeRequestId == id).ToListAsync(ct);
            ThrowIfNotAllowed(ChangeRequestWorkflow.CheckCanReview(cr.Status, cr.Author, user.Username, role, reviews.Select(r => r.Reviewer)));

            db.Reviews.Add(NewReview(id, ReviewDecisions.Approved, comment));
            db.AddAudit(user.Username, "changerequest.approved", cr.NamespaceKey, cr.EnvironmentKey, JsonSerializer.Serialize(new { id }));
            cr.UpdatedAt = DateTimeOffset.UtcNow;

            var approvals = reviews.Count(r => r.Decision == ReviewDecisions.Approved) + 1;
            if (!ChangeRequestWorkflow.HasEnoughApprovals(approvals, cr.RequiredApprovals))
                return null;

            var ops = JsonSerializer.Deserialize<List<ChangeOperation>>(cr.Operations, SnapshotJson.Options) ?? [];
            var atBase = await publishing.GetFlagsAtVersionAsync(cr.NamespaceKey, cr.EnvironmentKey, cr.BaseVersion, ct);
            var current = await publishing.GetCurrentFlagsAsync(cr.NamespaceKey, cr.EnvironmentKey, ct);
            var conflicts = ChangeRequestWorkflow.FindConflicts(atBase, current, ops);
            if (conflicts.Count > 0)
            {
                cr.Status = ChangeRequestStatus.Conflict;
                db.AddAudit(user.Username, "changerequest.conflict", cr.NamespaceKey, cr.EnvironmentKey,
                    JsonSerializer.Serialize(new { id, conflicts }));
                return $"Change request conflicts with newer changes to: {string.Join(", ", conflicts)}.";
            }

            cr.AppliedVersion = await publishing.ApplyAsync(cr.NamespaceKey, cr.EnvironmentKey, ops, cr.Author, cr.Title, cr.Id,
                "changerequest.applied", ct);
            cr.Status = ChangeRequestStatus.Applied;
            return null;
        }, ct);

        if (conflictMessage is not null) throw ApiException.Conflict(conflictMessage);
        return await GetAsync(id, ct);
    }

    public async Task<ChangeRequestDetail> RejectAsync(Guid id, string? comment, CancellationToken ct)
    {
        var head = await LoadHeadAsync(id, ct);
        var role = await auth.RequireAsync(head.NamespaceKey, NamespaceRole.Viewer, ct);
        var cr = await db.ChangeRequests.SingleAsync(c => c.Id == id, ct);
        var reviewers = await db.Reviews.Where(r => r.ChangeRequestId == id).Select(r => r.Reviewer).ToListAsync(ct);
        ThrowIfNotAllowed(ChangeRequestWorkflow.CheckCanReview(cr.Status, cr.Author, user.Username, role, reviewers));

        db.Reviews.Add(NewReview(id, ReviewDecisions.Rejected, comment));
        cr.Status = ChangeRequestStatus.Rejected;
        cr.UpdatedAt = DateTimeOffset.UtcNow;
        db.AddAudit(user.Username, "changerequest.rejected", cr.NamespaceKey, cr.EnvironmentKey, JsonSerializer.Serialize(new { id, comment }));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ChangeRequestDetail> CancelAsync(Guid id, CancellationToken ct)
    {
        var head = await LoadHeadAsync(id, ct);
        var role = await auth.RequireAsync(head.NamespaceKey, NamespaceRole.Viewer, ct);
        var cr = await db.ChangeRequests.SingleAsync(c => c.Id == id, ct);
        if (cr.Status != ChangeRequestStatus.Pending)
            throw ApiException.Conflict($"Change request is {Lower(cr.Status)}, not pending.");
        if (!ChangeRequestWorkflow.CanCancel(cr.Status, cr.Author, user.Username, role))
            throw ApiException.Forbidden("Only the author or a namespace owner can cancel a change request.");

        cr.Status = ChangeRequestStatus.Cancelled;
        cr.UpdatedAt = DateTimeOffset.UtcNow;
        db.AddAudit(user.Username, "changerequest.cancelled", cr.NamespaceKey, cr.EnvironmentKey, JsonSerializer.Serialize(new { id }));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task<ChangeRequest> LoadHeadAsync(Guid id, CancellationToken ct) =>
        await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw ApiException.NotFound($"Change request '{id}' does not exist.");

    private ChangeRequestReview NewReview(Guid crId, string decision, string? comment) => new()
    {
        Id = Guid.NewGuid(), ChangeRequestId = crId, Reviewer = user.Username, Decision = decision, Comment = comment, At = DateTimeOffset.UtcNow,
    };

    private static void ThrowIfNotAllowed(ReviewCheck check)
    {
        switch (check)
        {
            case ReviewCheck.NotPending: throw ApiException.Conflict("Change request is not pending.");
            case ReviewCheck.InsufficientRole: throw ApiException.Forbidden("Requires role 'approver' in the namespace.");
            case ReviewCheck.IsAuthor: throw ApiException.Forbidden("Authors cannot review their own change request.");
            case ReviewCheck.AlreadyReviewed: throw ApiException.Conflict("You already reviewed this change request.");
        }
    }

    private static string Lower(ChangeRequestStatus s) => s.ToString().ToLowerInvariant();
}
