using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Evaluation;

/// <summary>Fixed, synthetic acceptance protocol; never used by the application or normal evaluation runs.</summary>
internal static class ActivityContractProbe
{
    internal static readonly ActivityProbeScenario[] Protocol =
    [
        new("empty-clarification", "שנה את זה", ["revise"]),
        new("author-grade3-create", "צור פעילות הבנת הנקרא לכיתה ג׳ על גינה: סיפור אחד חדש באורך של בדיוק 120 מילים ושתי שאלות עם תשובה קצרה. הוסף הסבר לכל תשובה במפתח התשובות בלבד.", ["author", "material-ideas", "materials", "material-polish", "questions"]),
        new("author-grade4", "צור פעילות הבנת הנקרא לכיתה ד׳ על גינה: סיפור אחד חדש באורך של בדיוק 120 מילים ושתי שאלות עם תשובה קצרה.", ["author"]),
        new("answer", "על מה הפעילות הזאת?", ["revise"]),
        new("unsupported-refusal", "הוסף הסבר לכל תשובה במפתח התשובות בלבד.", ["revise"]),
        new("label-only", "שנה רק את שם הטקסט הראשון בהגדרות לטקסט הגינה. השאר את התוכן והשאלות ללא שינוי.", ["revise"]),
        new("new-text-only", "הוסף טקסט סיפורי חדש נוסף על גינה, באורך משוער של 60 מילים. השאר את שני הטקסטים הקיימים בדיוק כפי שהם ואת יתר הדרישות ללא שינוי. עדכן את שתי השאלות כך שיתאימו לכל הטקסטים.", ["revise", "material-ideas", "materials", "material-polish", "questions"]),
        new("append-two", "הוסף עוד שתי שאלות מאותו סוג, בלי לשנות או להסיר את שתי השאלות הקיימות ובלי לשנות שום דרישה אחרת.", ["revise", "append-questions"])
    ];

    internal static async Task RunAsync(AiGenerationService service, ActivityProbeReport report, Func<Task> save, CancellationToken ct)
    {
        var numeric = Numeric();
        var (mixedPlan, mixedDocument) = Mixed();
        var numericDocument = Questions(numeric, TaskAssembly.CreateDocument(Resolve(numeric)));
        report.Fixtures = JsonSerializer.SerializeToElement(new { numeric, numericDocument, mixedPlan, mixedDocument }, EvaluationFiles.Json);
        var current = Begin("empty-clarification", numeric, TaskAssembly.CreateDocument(Resolve(numeric)));
        var decision = await Revise();
        Require(decision.Clarification is not null && decision.Change is null, "empty-clarification");
        await Finish(numeric, current.BeforeDocument);

        current = Begin("author-grade3-create");
        var authored = await Call("author", evidence => service.AuthorAsync(new(current.Message), ct, evidence));
        var plan = RequireAuthored(authored.Value, "easy");
        Require(authored.Value.Assumptions.Length > 0, "unsupported-extra-assumption");
        var document = TaskAssembly.CreateDocument(Resolve(plan));
        document = await Generate(plan, document, RevisionScope.ForCreate(plan, document));
        await Finish(plan, document);

        current = Begin("author-grade4");
        authored = await Call("author", evidence => service.AuthorAsync(new(current.Message), ct, evidence));
        await Finish(RequireAuthored(authored.Value, "medium"), null);

        foreach (var id in new[] { "answer", "unsupported-refusal" })
        {
            current = Begin(id, mixedPlan, mixedDocument);
            decision = await Revise();
            Require(decision.Answer is not null && decision.Change is null, id);
            await Finish(mixedPlan, mixedDocument);
        }

        current = Begin("label-only", mixedPlan, mixedDocument);
        decision = await Revise();
        var change = RequireChange(decision);
        var expected = mixedPlan with { Materials = [mixedPlan.Materials[0] with { Label = "טקסט הגינה" }, .. mixedPlan.Materials.Skip(1)] };
        Require(Equal(expected, change.Plan), "label-only-plan");
        var work = RevisionScope.Derive(mixedPlan, mixedDocument, change);
        Require(work.NewMaterials.Length == 0 && work.Rewrites.Length == 0 && work.Questions == "none" && work.Clarification is null, "label-only-scope");
        document = RevisionScope.PrepareDocument(mixedPlan, mixedDocument, change.Plan, work);
        Require(SameContent(mixedDocument, document), "label-only-content");
        await Finish(change.Plan, document);

        current = Begin("new-text-only", mixedPlan, mixedDocument);
        decision = await Revise();
        change = RequireChange(decision);
        work = RevisionScope.Derive(mixedPlan, mixedDocument, change);
        Require(work.NewMaterials.Length == 1 && work.Rewrites.Length == 0 && work.Questions == "all" && work.Clarification is null, "new-text-only-scope");
        Require(Equal(mixedPlan, change.Plan with { Materials = change.Plan.Materials.Where(m => !work.NewMaterials.Contains(m.Id)).ToArray() }), "retained-plan");
        document = RevisionScope.PrepareDocument(mixedPlan, mixedDocument, change.Plan, work);
        document = await Generate(change.Plan, document, work);
        Require(mixedDocument.Materials.All(before => document.Materials.Any(after =>
            after.Id == before.Id && after.Body == before.Body && after.Title == before.Title && after.Revision == before.Revision)), "retained-materials");
        await Finish(change.Plan, document);

        current = Begin("append-two", numeric, numericDocument);
        decision = await Revise();
        change = RequireChange(decision);
        work = RevisionScope.Derive(numeric, numericDocument, change);
        Require(work.Questions == "append" && work.Rewrites.Length == 0 && work.NewMaterials.Length == 0 && work.Clarification is null &&
            change.Plan.Settings.QuestionCount == 4, "append-scope");
        document = RevisionScope.PrepareDocument(numeric, numericDocument, change.Plan, work);
        var addition = new QuestionAdditionInput(Resolve(change.Plan), document, 2, work.Instruction);
        var appended = await Call("append-questions", evidence => service.AppendQuestionsAsync(addition, [], ct, evidence));
        document = TaskAssembly.AppendQuestions(addition, appended.Value, appended.Metadata);
        Require(document.Questions.Length == 4 && SameContent(numericDocument, document with { Questions = document.Questions[..2] }), "append-originals");
        await Finish(change.Plan, document);

        ActivityProbeCase Begin(string id, LearningPlan? beforePlan = null, TaskDocument? before = null)
        {
            var result = new ActivityProbeCase(id, Protocol.Single(item => item.Id == id).Message, beforePlan, before);
            report.Cases.Add(result);
            return result;
        }

        async Task Finish(LearningPlan acceptedPlan, TaskDocument? acceptedDocument)
        {
            if (acceptedDocument is not null && current.Id != "empty-clarification")
                Require(TaskDocumentValidator.ValidateRelease(Resolve(acceptedPlan), acceptedDocument).Count == 0, "release-validation");
            current.Plan = acceptedPlan;
            current.Document = acceptedDocument;
            current.Passed = true;
            await save();
        }

        async Task<RevisionDecision> Revise() => (await Call("revise", evidence => service.ReviseAsync(
            new(current.BeforePlan!, current.BeforeDocument!, current.Message), ct, evidence))).Value;

        async Task<AiResult<T>> Call<T>(string stage, Func<AiCallEvidence, Task<AiResult<T>>> operation)
        {
            ct.ThrowIfCancellationRequested();
            if (report.Steps.Count >= ActivityProbeTransport.MaxCalls) throw new InvalidOperationException("Probe call limit reached.");
            var step = new ActivityProbeStage(current.Id, stage);
            report.Steps.Add(step);
            await save();
            try { var result = await operation(step.Evidence); step.Outcome = "accepted"; return result; }
            catch (Exception error)
            {
                step.Outcome = "failed";
                step.Failure = error is AiGenerationException ai ? ai.Category : error is TaskValidationException ? "validation" : "stopped";
                step.ValidationErrors = error is AiGenerationException aiError ? aiError.ValidationErrors : (error as TaskValidationException)?.Errors;
                throw;
            }
            finally { await save(); }
        }

        async Task<TaskDocument> Generate(LearningPlan requirements, TaskDocument before, RevisionWork scope)
        {
            var request = Resolve(requirements);
            var materials = new MaterialGenerationInput(request, before.Materials, scope.NewMaterials);
            var ideas = await Call("material-ideas", evidence => service.GenerateMaterialIdeasAsync(materials, [], ct, evidence));
            var idea = MaterialIdeas.Select(ideas.Value, 0);
            var written = await Call("materials", evidence => service.GenerateMaterialsAsync(materials, idea, ct, evidence));
            var materialOnly = before with { Title = "", Instructions = null, Questions = [] };
            var accepted = TaskAssembly.AcceptMaterials(request, materialOnly, written.Value, written.Metadata, idea, scope.NewMaterials);
            if (accepted.Document is null) throw new TaskValidationException(accepted.Diagnostics);
            var polishInput = new PolishInput(request, accepted.Document, scope.NewMaterials);
            var polished = await Call("material-polish", evidence => service.PolishMaterialsAsync(polishInput, ct, evidence));
            materialOnly = TaskAssembly.PolishMaterials(polishInput, polished.Value, polished.Metadata);
            var working = before with { Materials = materialOnly.Materials };
            var questionInput = TaskAssembly.PrepareQuestions(request, materialOnly) with { Current = working, Instruction = scope.Instruction };
            var questions = await Call("questions", evidence => service.GenerateQuestionsAsync(questionInput, [], ct, evidence));
            return TaskAssembly.AcceptQuestions(request, working, questions.Value, questions.Metadata);
        }
    }

    private static LearningPlan RequireAuthored(AuthoringReply reply, string difficulty)
    {
        var plan = reply.Proposal ?? throw new InvalidOperationException("Probe authoring unexpectedly requested clarification.");
        Require(plan.Settings.Difficulty == difficulty && plan.Settings.QuestionCount == 2 && plan.Materials is [{ Source: "generated", Length: { Mode: "target", Count: 120 } }] &&
            plan.Questions.Formats.SequenceEqual(["text-input"]), "authoring-requirements");
        return plan;
    }
    private static RevisionChange RequireChange(RevisionDecision decision) => decision.Change ?? throw new InvalidOperationException("Probe revision did not propose a change.");
    private static void Require(bool condition, string check) { if (!condition) throw new ActivityProbeCheckException(check); }
    private static bool Equal<T>(T left, T right) => JsonSerializer.Serialize(left, EvaluationFiles.Json) == JsonSerializer.Serialize(right, EvaluationFiles.Json);
    private static bool SameContent(TaskDocument left, TaskDocument right) => Equal(Project(left), Project(right));
    private static object Project(TaskDocument document) => new
    {
        document.Title,
        document.Instructions,
        materials = document.Materials.Select(m => new { m.Id, m.Revision, m.Title, m.Body }),
        questions = document.Questions.Select(q => new { q.Id, q.Prompt, q.Interaction, q.Answer, q.Points })
    };
    private static ResolvedTaskRequest Resolve(LearningPlan plan) => TaskRequestResolver.ResolveOrThrow(plan);
    private static LearningPlan Numeric() => new("תרגול חיבור", "חיבור מספרים עד 10", "", new("חיבור", "כיתה ג׳", "easy", 2), [], new(["numeric-input"], null, ""));
    private static TaskDocument Questions(LearningPlan plan, TaskDocument document) => TaskAssembly.AcceptQuestions(Resolve(plan), document,
        plan.Materials.Length == 0
            ? new("תרגול חיבור", "ענו על השאלות", [new("כמה הם אחת ועוד אחת?", new("numeric-input"), new("2"), 1), new("כמה הם אחת ועוד שתיים?", new("numeric-input"), new("3"), 1)])
            : new("הגינה", "קראו וענו", [new("מי שתל פרח בגינה?", new("text-input"), new("דני"), 1), new("כמה פרחים יש בגינה של נועה?", new("text-input"), new("שלושה"), 1)]));
    private static (LearningPlan, TaskDocument) Mixed()
    {
        var plan = Numeric() with
        {
            Name = "הגינה",
            Goal = "הבנת הנקרא",
            Settings = new("גינה", "כיתה ג׳", "hard", 2),
            Questions = new(["text-input"], null, ""),
            Materials = [new("11111111111111111111111111111111", "סיפור", "generated", "סיפור על גינה", null, null),
                new("22222222222222222222222222222222", "מקור", "supplied", "", "בגינה של נועה יש שלושה פרחים. נועה משקה את הפרחים בבוקר.", null)]
        };
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), new([new(plan.Materials[0].Id!, "הגינה", "דני שתל פרח בגינה. הוא השקה אותו בכל בוקר.")])).Document!;
        return (plan, Questions(plan, document));
    }
}

internal sealed record ActivityProbeScenario(string Id, string Message, string[] Stages);
internal sealed class ActivityProbeCheckException(string check) : Exception { internal string Check { get; } = check; }

internal sealed class ActivityProbeReport
{
    public int EngineRevision { get; } = EngineVersions.Revision;
    public int SchemaVersion { get; } = EngineVersions.SchemaVersion;
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public string Status { get; set; } = "running";
    public string? Failure { get; set; }
    public JsonElement? Fixtures { get; set; }
    public Dictionary<string, string?> Profile { get; set; } = [];
    public List<ActivityProbeCase> Cases { get; } = [];
    public List<ActivityProbeStage> Steps { get; } = [];
}

internal sealed record ActivityProbeCase(string Id, string Message, LearningPlan? BeforePlan, TaskDocument? BeforeDocument)
{
    public bool Passed { get; set; }
    public LearningPlan? Plan { get; set; }
    public TaskDocument? Document { get; set; }
}
internal sealed record ActivityProbeStage(string CaseId, string Stage)
{
    public string Outcome { get; set; } = "pending";
    public string? Failure { get; set; }
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; set; }
    public AiCallEvidence Evidence { get; } = new();
}
