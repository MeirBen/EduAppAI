using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Infrastructure.Persistence;

public sealed class LearningDbContext(DbContextOptions<LearningDbContext> options)
    : IdentityUserContext<ParentUser>(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();
    public DbSet<TaskTemplateVersion> TaskTemplateVersions => Set<TaskTemplateVersion>();
    public DbSet<TaskInstance> TaskInstances => Set<TaskInstance>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ParentUser>().HasOne<Family>().WithMany().HasForeignKey(p => p.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<TaskTemplate>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(100);
            entity.Property(t => t.CurrentVersion).IsConcurrencyToken();
            entity.HasIndex(t => new { t.FamilyId, t.CreatedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(t => t.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskTemplateVersion>(entity =>
        {
            entity.HasIndex(v => new { v.TemplateId, v.Version }).IsUnique();
            entity.HasOne<TaskTemplate>().WithMany().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskInstance>(entity =>
        {
            entity.Property(i => i.Title).HasMaxLength(100);
            entity.HasIndex(i => new { i.FamilyId, i.CreatedAtUtc });
            entity.HasOne<Family>().WithMany().HasForeignKey(i => i.FamilyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaskTemplateVersion>().WithMany().HasForeignKey(i => i.TemplateVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
