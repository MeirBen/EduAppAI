using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Describes only the plan and content staged for the same atomic commit, excluding provenance and acceptance changes.</summary>
internal static class ActivityChangeNotice
{
    private const string More = "עודכנו גם פרטים נוספים בפעילות.";

    /// <summary>Complete statements whose joined text stays within the reply contract.</summary>
    internal static string[] Describe(LearningPlan before, TaskDocument previous, LearningPlan after, TaskDocument current)
    {
        var notices = new List<string>();
        if (before.Name != after.Name) notices.Add($"שם הטיוטה שונה ל־„{after.Name}”.");
        var labels = after.Materials.Where(m => before.Materials.Any(old => old.Id == m.Id && old.Label != m.Label)).ToArray();
        if (labels.Length == 1) notices.Add($"שם הטקסט בהגדרות שונה ל־„{labels[0].Label}”.");
        else AddCount("שמות טקסטים שעודכנו", labels.Length);

        var changes = PlanChanges.Compare(before, after);
        if (changes.Any(c => c.Path is "goal" or "guidance" or "settings" or "totalLength"))
            notices.Add("עודכנו דרישות הפעילות.");
        if (changes.Any(c => c.Path == "questions")) notices.Add("עודכנו דרישות השאלות.");
        if (changes.Any(c => c.Path == "documentGuidance")) notices.Add("עודכנו ההנחיות לכותרת ולהוראות.");
        if (after.Materials.Any(m => before.Materials.FirstOrDefault(old => old.Id == m.Id) is { } old &&
            !Equal(old with { Label = m.Label }, m))) notices.Add("עודכנו דרישות הטקסטים.");
        AddCount("דרישות לטקסטים שנוספו", after.Materials.Count(m => !before.Materials.Any(old => old.Id == m.Id) && !current.Materials.Any(c => c.Id == m.Id)));
        AddCount("דרישות לטקסטים שהוסרו", before.Materials.Count(m => !after.Materials.Any(next => next.Id == m.Id) && !previous.Materials.Any(c => c.Id == m.Id)));

        AddCount("טקסטים שנוספו", current.Materials.Count(m => !previous.Materials.Any(old => old.Id == m.Id)));
        AddCount("טקסטים שהוסרו", previous.Materials.Count(m => !current.Materials.Any(next => next.Id == m.Id)));
        AddCount("טקסטים שעודכנו", current.Materials.Count(m => previous.Materials.Any(old => old.Id == m.Id && (old.Title != m.Title || old.Body != m.Body))));
        if (RetainedOrderChanged(before.Materials.Select(m => m.Id!), after.Materials.Select(m => m.Id!)))
            notices.Add("סדר הטקסטים שונה.");

        var added = current.Questions.Count(q => !previous.Questions.Any(old => old.Id == q.Id));
        var removed = previous.Questions.Count(q => !current.Questions.Any(next => next.Id == q.Id));
        if (previous.Questions.Length > 0 && current.Questions.Length > 0 && added == current.Questions.Length && removed == previous.Questions.Length)
            // A rebuild assigns new IDs even when the model returns the same questions; say so rather than claim a change.
            notices.Add(current.Questions.Length == previous.Questions.Length && current.Questions.Zip(previous.Questions).All(p => Same(p.Second, p.First))
                ? "השאלות נוצרו מחדש ויצאו זהות." : $"השאלות נוצרו מחדש ({current.Questions.Length}).");
        else
        {
            AddCount("שאלות שנוספו", added);
            AddCount("שאלות שהוסרו", removed);
            AddCount("שאלות שעודכנו", current.Questions.Count(q => previous.Questions.FirstOrDefault(old => old.Id == q.Id) is { } old && !Same(old, q)));
            if (RetainedOrderChanged(previous.Questions.Select(q => q.Id), current.Questions.Select(q => q.Id)))
                notices.Add("סדר השאלות שונה.");
        }
        if (previous.Title != current.Title) notices.Add("כותרת התוכן עודכנה.");
        if (previous.Instructions != current.Instructions) notices.Add("ההוראות לילדים עודכנו.");
        if (notices.Count == 0) notices.Add("הטקסטים והשאלות נשארו ללא שינוי.");

        // Keep complete statements within the reply contract, even when several maximum-length names change together.
        if (string.Join(" ", notices).Length > RevisionReplyLength)
        {
            do notices.RemoveAt(notices.Count - 1);
            while (string.Join(" ", notices.Append(More)).Length > RevisionReplyLength);
            notices.Add(More);
        }
        return notices.ToArray();

        void AddCount(string label, int count)
        {
            if (count > 0) notices.Add($"{label}: {count}.");
        }
    }

    private static bool Equal<T>(T before, T after) => StoredJson.Write(before) == StoredJson.Write(after);

    /// <summary>Same learner-facing question; identity, origin and acceptance are bookkeeping.</summary>
    private static bool Same(DocumentQuestion before, DocumentQuestion after) =>
        Equal(before with { Id = after.Id, Origin = after.Origin, Acceptance = after.Acceptance }, after);

    private static bool RetainedOrderChanged(IEnumerable<string> before, IEnumerable<string> after)
    {
        var prior = before.ToArray();
        var next = after.ToArray();
        return !prior.Where(next.Contains).SequenceEqual(next.Where(prior.Contains));
    }
}
