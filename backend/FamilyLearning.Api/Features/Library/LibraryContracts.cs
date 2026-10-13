namespace FamilyLearning.Api.Features.Library;

/// <summary>Unreleased drafts, ready snapshots in each view, and archived snapshots.</summary>
public sealed record LibraryCounts(int Drafts, int Assigned, int Unassigned, int Completed, int Archived);
