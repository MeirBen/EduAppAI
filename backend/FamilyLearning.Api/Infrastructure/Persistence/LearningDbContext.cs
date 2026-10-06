using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Features.Children;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FamilyLearning.Api.Infrastructure.Persistence;

/// <summary>Scoped persistence for Identity and learning data in one SQLite database.</summary>
/// <remarks>No global ownership filter is installed; feature queries must constrain access by family.</remarks>
public sealed class LearningDbContext(DbContextOptions<LearningDbContext> options)
    : IdentityUserContext<ParentUser>(options)
{
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<TaskSession> TaskSessions => Set<TaskSession>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<ChildActivation> ChildActivations => Set<ChildActivation>();
    public DbSet<ChildDeviceGrant> ChildDeviceGrants => Set<ChildDeviceGrant>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();
    public DbSet<TaskTemplateVersion> TaskTemplateVersions => Set<TaskTemplateVersion>();
    public DbSet<ActivityDraft> ActivityDrafts => Set<ActivityDraft>();
    public DbSet<TaskSnapshot> TaskSnapshots => Set<TaskSnapshot>();
    public DbSet<GenerationOperation> GenerationOperations => Set<GenerationOperation>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        // SQLite loses DateTime.Kind; these timestamp columns always contain UTC values.
        var utcTimestamp = new ValueConverter<DateTime, DateTime>(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        model.Entity<Family>().Property(f => f.CreatedAtUtc).HasConversion(utcTimestamp);
        model.Entity<ParentUser>().HasOne<Family>().WithMany().HasForeignKey(p => p.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<Child>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(EngineValidation.NameLength);
            entity.Property(c => c.Revision).IsConcurrencyToken();
            entity.Property(c => c.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(c => new { c.FamilyId, c.CreatedAtUtc, c.Id });
            entity.HasOne<Family>().WithMany().HasForeignKey(c => c.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ChildActivation>(entity =>
        {
            entity.HasKey(a => a.ChildId);
            entity.Property(a => a.CodeHash).HasMaxLength(64);
            entity.Property(a => a.DeviceLabel).HasMaxLength(EngineValidation.NameLength);
            entity.Property(a => a.ExpiresAtUtc).HasConversion(utcTimestamp);
            entity.Property(a => a.ConsumedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(a => a.CodeHash).IsUnique();
            entity.HasOne<Child>().WithOne().HasForeignKey<ChildActivation>(a => a.ChildId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ChildDeviceGrant>(entity =>
        {
            entity.Property(g => g.DeviceLabel).HasMaxLength(EngineValidation.NameLength);
            entity.Property(g => g.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(g => g.ExpiresAtUtc).HasConversion(utcTimestamp);
            entity.Property(g => g.RevokedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(g => new { g.ChildId, g.CreatedAtUtc, g.Id });
            entity.HasOne<Child>().WithMany().HasForeignKey(g => g.ChildId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskTemplate>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(EngineValidation.NameLength);
            entity.Property(t => t.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(t => t.UpdatedAtUtc).HasConversion(utcTimestamp);
            // Reject a stale writer even when both requests passed the initial revision check.
            entity.Property(t => t.CurrentVersion).IsConcurrencyToken();
            entity.HasIndex(t => new { t.FamilyId, t.CreatedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(t => t.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskTemplateVersion>(entity =>
        {
            entity.Property(v => v.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(v => new { v.TemplateId, v.Version }).IsUnique();
            entity.HasOne<TaskTemplate>().WithMany().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ActivityDraft>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(EngineValidation.NameLength);
            entity.Property(d => d.Revision).IsConcurrencyToken();
            // Starting work does not advance content revision, but must still fence an already-read release.
            entity.Property(d => d.ActiveOperationId).IsConcurrencyToken();
            entity.Property(d => d.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(d => d.UpdatedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(d => new { d.FamilyId, d.UpdatedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(d => d.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskSnapshot>(entity =>
        {
            entity.Property(s => s.Title).HasMaxLength(EngineValidation.TitleLength);
            entity.Property(s => s.DraftCreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(s => s.ReviewedAtUtc).HasConversion(utcTimestamp);
            entity.Property(s => s.ArchivedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(s => s.SourceDraftId).IsUnique();
            entity.HasIndex(s => new { s.FamilyId, s.ReviewedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(s => s.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Assignment>(entity =>
        {
            entity.Property(a => a.Status).HasMaxLength(20);
            entity.Property(a => a.Revision).IsConcurrencyToken();
            entity.Property(a => a.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(a => a.WithdrawnAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(a => new { a.ChildId, a.SnapshotId }).IsUnique();
            entity.HasIndex(a => new { a.FamilyId, a.CreatedAtUtc, a.Id });
            entity.HasIndex(a => new { a.FamilyId, a.Status, a.CreatedAtUtc, a.Id });
            entity.HasIndex(a => new { a.ChildId, a.CreatedAtUtc, a.Id });
            entity.HasIndex(a => new { a.ChildId, a.Status, a.CreatedAtUtc, a.Id });
            // Composite foreign keys enforce the same family even if a future caller misses an ownership check.
            entity.HasOne(a => a.Child).WithMany().HasForeignKey(a => new { a.FamilyId, a.ChildId })
                .HasPrincipalKey(c => new { c.FamilyId, c.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.Snapshot).WithMany().HasForeignKey(a => new { a.FamilyId, a.SnapshotId })
                .HasPrincipalKey(s => new { s.FamilyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskSession>(entity =>
        {
            entity.HasKey(s => s.AssignmentId);
            entity.Property(s => s.Revision).IsConcurrencyToken();
            entity.Property(s => s.StartedAtUtc).HasConversion(utcTimestamp);
            entity.Property(s => s.SavedAtUtc).HasConversion(utcTimestamp);
            entity.Property(s => s.SubmittedAtUtc).HasConversion(utcTimestamp);
            entity.Property(s => s.ReviewedAtUtc).HasConversion(utcTimestamp);
            entity.HasOne(s => s.Assignment).WithOne(a => a.Session).HasForeignKey<TaskSession>(s => s.AssignmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GenerationOperation>(entity =>
        {
            entity.Property(o => o.Status).IsConcurrencyToken();
            entity.Property(o => o.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(o => o.FinishedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(o => new { o.FamilyId, o.OperationKey }).IsUnique();
            entity.HasIndex(o => o.DraftId).IsUnique().HasFilter("Status IN ('queued', 'calling')");
            entity.HasIndex(o => new { o.DraftId, o.CreatedAtUtc });
            entity.HasIndex(o => new { o.Status, o.CreatedAtUtc });
            entity.HasIndex(o => o.FinishedAtUtc).HasFilter("ArtifactsJson IS NOT NULL");
            entity.HasOne<ActivityDraft>().WithMany().HasForeignKey(o => o.DraftId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
