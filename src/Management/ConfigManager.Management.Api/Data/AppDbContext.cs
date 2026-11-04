using ConfigManager.Contracts;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ConfigManager.Management.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<EnvironmentEntity> Environments => Set<EnvironmentEntity>();
    public DbSet<NamespaceEntity> Namespaces => Set<NamespaceEntity>();
    public DbSet<NamespacePolicy> Policies => Set<NamespacePolicy>();
    public DbSet<RoleBinding> RoleBindings => Set<RoleBinding>();
    public DbSet<FlagEntity> Flags => Set<FlagEntity>();
    public DbSet<FlagEnvironmentConfig> FlagConfigs => Set<FlagEnvironmentConfig>();
    public DbSet<PublishedSnapshot> PublishedSnapshots => Set<PublishedSnapshot>();
    public DbSet<ServiceAccessGrant> AccessGrants => Set<ServiceAccessGrant>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ChangeRequestReview> Reviews => Set<ChangeRequestReview>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<EnvironmentEntity>(e => { e.ToTable("environments"); e.HasKey(x => x.Key); });
        b.Entity<NamespaceEntity>(e => { e.ToTable("namespaces"); e.HasKey(x => x.Key); });
        b.Entity<NamespacePolicy>(e =>
        {
            e.ToTable("namespace_policies");
            e.HasKey(x => new { x.NamespaceKey, x.EnvironmentKey });
        });
        b.Entity<RoleBinding>(e =>
        {
            e.ToTable("namespace_role_bindings");
            e.Property(x => x.Role).HasConversion(LowerEnum<NamespaceRole>());
            e.HasIndex(x => new { x.NamespaceKey, x.SubjectType, x.Subject }).IsUnique();
        });
        b.Entity<FlagEntity>(e =>
        {
            e.ToTable("flags");
            e.HasKey(x => new { x.NamespaceKey, x.Key });
            e.Property(x => x.Type).HasConversion(LowerEnum<FlagType>());
        });
        b.Entity<FlagEnvironmentConfig>(e =>
        {
            e.ToTable("flag_environment_configs");
            e.HasKey(x => new { x.NamespaceKey, x.EnvironmentKey, x.FlagKey });
            e.Property(x => x.Content).HasColumnType("jsonb");
        });
        b.Entity<PublishedSnapshot>(e =>
        {
            e.ToTable("published_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Content).HasColumnType("jsonb");
            e.Property(x => x.NamespaceKey).HasColumnType("text");
            e.Property(x => x.EnvironmentKey).HasColumnType("text");
            e.Property(x => x.PublishedBy).HasColumnType("text");
            e.Property(x => x.Message).HasColumnType("text");
            e.HasAlternateKey(x => new { x.NamespaceKey, x.EnvironmentKey, x.Version });
        });
        b.Entity<ServiceAccessGrant>(e =>
        {
            e.ToTable("service_access_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ClientId).HasColumnType("text");
            e.Property(x => x.NamespaceKey).HasColumnType("text");
            e.Property(x => x.EnvironmentKey).HasColumnType("text");
            e.Property(x => x.CreatedBy).HasColumnType("text");
            e.HasAlternateKey(x => new { x.ClientId, x.NamespaceKey, x.EnvironmentKey });
        });
        b.Entity<ChangeRequest>(e =>
        {
            e.ToTable("change_requests");
            e.Property(x => x.Status).HasConversion(LowerEnum<ChangeRequestStatus>());
            e.Property(x => x.Operations).HasColumnType("jsonb");
            e.HasIndex(x => new { x.NamespaceKey, x.EnvironmentKey, x.Status });
        });
        b.Entity<ChangeRequestReview>(e =>
        {
            e.ToTable("change_request_reviews");
            e.HasIndex(x => x.ChangeRequestId);
        });
        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.HasIndex(x => x.At);
        });
    }

    private static ValueConverter<T, string> LowerEnum<T>() where T : struct, Enum =>
        new(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<T>(s, true));
}

public static class AuditExtensions
{
    public static void AddAudit(this AppDbContext db, string actor, string action, string? ns = null, string? env = null, string? details = null) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            Actor = actor,
            Action = action,
            NamespaceKey = ns,
            EnvironmentKey = env,
            Details = details,
        });
}
