using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FamilyLearning.Api.Infrastructure.Persistence;

/// <summary>Scoped persistence for Identity and learning data in one SQLite database.</summary>
/// <remarks>No global ownership filter is installed; feature queries must constrain access by family.</remarks>
public sealed class LearningDbContext(DbContextOptions<LearningDbContext> options)
    : IdentityUserContext<ParentUser>(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();
    public DbSet<TaskTemplateVersion> TaskTemplateVersions => Set<TaskTemplateVersion>();
    public DbSet<TaskInstance> TaskInstances => Set<TaskInstance>();
    public DbSet<ActivityDraft> ActivityDrafts => Set<ActivityDraft>();
    public DbSet<TaskSnapshot> TaskSnapshots => Set<TaskSnapshot>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        // SQLite loses DateTime.Kind; these timestamp columns always contain UTC values.
        var utcTimestamp = new ValueConverter<DateTime, DateTime>(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        model.Entity<Family>().Property(f => f.CreatedAtUtc).HasConversion(utcTimestamp);
        model.Entity<ParentUser>().HasOne<Family>().WithMany().HasForeignKey(p => p.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TaskTemplate>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(100);
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
        model.Entity<TaskInstance>(entity =>
        {
            entity.Property(i => i.Title).HasMaxLength(100);
            entity.Property(i => i.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(i => new { i.FamilyId, i.CreatedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(i => i.FamilyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaskTemplateVersion>().WithMany().HasForeignKey(i => i.TemplateVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ActivityDraft>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(100);
            entity.Property(d => d.Revision).IsConcurrencyToken();
            entity.Property(d => d.CreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(d => d.UpdatedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(d => new { d.FamilyId, d.UpdatedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(d => d.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskSnapshot>(entity =>
        {
            entity.Property(s => s.Title).HasMaxLength(100);
            entity.Property(s => s.DraftCreatedAtUtc).HasConversion(utcTimestamp);
            entity.Property(s => s.ReviewedAtUtc).HasConversion(utcTimestamp);
            entity.HasIndex(s => s.SourceDraftId).IsUnique();
            entity.HasIndex(s => new { s.FamilyId, s.ReviewedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(s => s.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
