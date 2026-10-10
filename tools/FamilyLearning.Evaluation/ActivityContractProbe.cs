using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Evaluation;

/// <summary>Fixed, synthetic acceptance protocols; never used by the application or normal evaluation runs.</summary>
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

    /// <summary>Everyday chat edits on one generated story, ordered by information value because a run stops at its first failed check.</summary>
    internal static readonly ActivityProbeScenario[] EditProtocol =
    [
        new("topic-pirates", "תעשה שהסיפור יהיה על פיראטים במקום על גינה.", ["revise", "rewrite-material", "questions"]),
        new("poem-same-plot", "תהפוך את הסיפור לשיר מחורז, עם אותה עלילה ואותן דמויות.", ["revise", "rewrite-material", "questions"]),
        new("shorten-half", "הסיפור ארוך מדי. תקצר אותו לבערך חצי מהאורך.", ["revise", "rewrite-material", "questions"]),
        new("replace-easier", "השאלה הזאת קשה מדי. תחליף אותה בשאלה קלה יותר.", ["revise", "revise-question"]),
        new("remove-third", "תמחק את השאלה השלישית.", ["revise"]),
        new("add-focused", "תוסיף שאלה על הצבע של פרחי החמנייה.", ["revise", "append-questions"]),
        new("vocabulary-focus", "שהשאלות יתמקדו באוצר מילים מהסיפור.", ["revise", "questions"]),
        new("decrease-unspecified", "תוריד שאלה אחת.", ["revise"])
    ];

    internal static ActivityProbeScenario[] Select(string name) => name switch
    {
        "contract" => Protocol,
        "edits" => EditProtocol,
        _ => throw new ArgumentException("Unknown probe protocol.")
    };

    internal static Task RunAsync(string name, AiGenerationService service, ActivityProbeReport report, Func<Task> save, CancellationToken ct) =>
        name == "edits" ? RunEditsAsync(service, report, save, ct) : RunAsync(service, report, save, ct);

    internal static async Task RunAsync(AiGenerationService service, ActivityProbeReport report, Func<Task> save, CancellationToken ct)
    {
        var session = new ProbeSession(service, report, save, Protocol, ct);
        var numeric = Numeric();
        var (mixedPlan, mixedDocument) = Mixed();
        var (unsupportedPlan, unsupportedDocument) = Mixed("יש להוסיף הסבר לכל תשובה במפתח התשובות.");
        var numericDocument = Questions(numeric, TaskAssembly.CreateDocument(Resolve(numeric)));
        report.Fixtures = JsonSerializer.SerializeToElement(new { numeric, numericDocument, mixedPlan, mixedDocument, unsupportedPlan, unsupportedDocument }, EvaluationFiles.Json);
        var current = session.Begin("empty-clarification", numeric, TaskAssembly.CreateDocument(Resolve(numeric)));
        var decision = await session.Revise();
        Require(decision.Clarification is not null && decision.Change is null, "empty-clarification");
        await session.Finish(numeric, current.BeforeDocument);

        current = session.Begin("author-grade3-create");
        var authored = await session.Call("author", evidence => service.AuthorAsync(new(current.Message), ct, evidence));
        var plan = RequireAuthored(authored.Value, "easy");
        Require(authored.Value.Assumptions.Length > 0, "unsupported-extra-assumption");
        var document = TaskAssembly.CreateDocument(Resolve(plan));
        document = await session.Generate(plan, document, RevisionScope.ForCreate(plan, document));
        await session.Finish(plan, document);

        current = session.Begin("author-grade4");
        authored = await session.Call("author", evidence => service.AuthorAsync(new(current.Message), ct, evidence));
        await session.Finish(RequireAuthored(authored.Value, "medium"), null);

        foreach (var id in new[] { "answer", "unsupported-refusal" })
        {
            current = id == "unsupported-refusal" ? session.Begin(id, unsupportedPlan, unsupportedDocument) : session.Begin(id, mixedPlan, mixedDocument);
            decision = await session.Revise();
            Require(decision.Change is null && (decision.Answer is not null ||
                id == "unsupported-refusal" && decision.Clarification is not null), id);
            await session.Finish(current.BeforePlan!, current.BeforeDocument);
        }

        session.Begin("label-only", mixedPlan, mixedDocument);
        decision = await session.Revise();
        var change = RequireChange(decision);
        var expected = mixedPlan with { Materials = [mixedPlan.Materials[0] with { Label = "טקסט הגינה" }, .. mixedPlan.Materials.Skip(1)] };
        Require(Equal(expected, change.Plan), "label-only-plan");
        var work = RevisionScope.Derive(mixedPlan, mixedDocument, change);
        Require(work.NewMaterials.Length == 0 && work.Rewrites.Length == 0 && work.Questions == "none" && work.Clarification is null, "label-only-scope");
        document = RevisionScope.PrepareDocument(mixedPlan, mixedDocument, change.Plan, work);
        Require(SameContent(mixedDocument, document), "label-only-content");
        await session.Finish(change.Plan, document);

        session.Begin("new-text-only", mixedPlan, mixedDocument);
        decision = await session.Revise();
        change = RequireChange(decision);
        work = RevisionScope.Derive(mixedPlan, mixedDocument, change);
        Require(work.NewMaterials.Length == 1 && work.Rewrites.Length == 0 && work.Questions == "all" && work.Clarification is null, "new-text-only-scope");
        // The requested coverage of all texts may become lasting question guidance.
        var retainedPlan = change.Plan with
        {
            Materials = change.Plan.Materials.Where(m => !work.NewMaterials.Contains(m.Id)).ToArray(),
            Questions = change.Plan.Questions with { Guidance = mixedPlan.Questions.Guidance }
        };
        Require(Equal(mixedPlan, retainedPlan), "retained-plan");
        document = RevisionScope.PrepareDocument(mixedPlan, mixedDocument, change.Plan, work);
        document = await session.Generate(change.Plan, document, work);
        Require(mixedDocument.Materials.All(before => document.Materials.Any(after =>
            after.Id == before.Id && after.Body == before.Body && after.Title == before.Title && after.Revision == before.Revision)), "retained-materials");
        await session.Finish(change.Plan, document);

        session.Begin("append-two", numeric, numericDocument);
        decision = await session.Revise();
        change = RequireChange(decision);
        work = RevisionScope.Derive(numeric, numericDocument, change);
        Require(work.Questions == "append" && work.Rewrites.Length == 0 && work.NewMaterials.Length == 0 && work.Clarification is null &&
            change.Plan.Settings.QuestionCount == 4, "append-scope");
        document = RevisionScope.PrepareDocument(numeric, numericDocument, change.Plan, work);
        var addition = new QuestionAdditionInput(Resolve(change.Plan), document, 2, work.Instruction);
        var appended = await session.Call("append-questions", evidence => service.AppendQuestionsAsync(addition, [], ct, evidence));
        document = TaskAssembly.AppendQuestions(addition, appended.Value, appended.Metadata);
        Require(document.Questions.Length == 4 && SameContent(numericDocument, document with { Questions = document.Questions[..2] }), "append-originals");
        await session.Finish(change.Plan, document);
    }

    /// <summary>Checks the scope each everyday request derives, then runs exactly the stages the worker would and keeps untouched content.</summary>
    internal static async Task RunEditsAsync(AiGenerationService service, ActivityProbeReport report, Func<Task> save, CancellationToken ct)
    {
        var session = new ProbeSession(service, report, save, EditProtocol, ct);
        var (plan, document) = Story();
        report.Fixtures = JsonSerializer.SerializeToElement(new { plan, document }, EvaluationFiles.Json);
        var story = document.Materials[0];
        var questions = document.Questions;

        // Shared and text requirement changes rewrite the existing text from its body, then rebuild every question.
        foreach (var id in new[] { "topic-pirates", "poem-same-plot", "shorten-half" })
        {
            session.Begin(id, plan, document);
            var change = RequireChange(await session.Revise());
            var work = RevisionScope.Derive(plan, document, change);
            Require(work.Clarification is null && work.NewMaterials.Length == 0 && work.Rewrites.Length == 1 && work.Rewrites[0].Id == story.Id &&
                work.Questions == "all", id + "-scope");
            var settings = change.Plan.Settings;
            Require(settings.Audience == plan.Settings.Audience && settings.QuestionCount == plan.Settings.QuestionCount &&
                change.Plan.Questions.Formats.SequenceEqual(plan.Questions.Formats) && (id == "topic-pirates" || settings.Topic == plan.Settings.Topic),
                id + "-retained-requirements");
            var result = await session.Execute(plan, document, change, work);
            Require(result.Questions.All(rebuilt => questions.All(old => old.Id != rebuilt.Id)), id + "-questions-rebuilt");
            var body = result.Materials[0].Body;
            // Wording checks run last, so a scope miss is never hidden behind a phrasing miss.
            Require(id switch
            {
                // The new topic may be told without its literal name; the replaced garden story must be gone.
                "topic-pirates" => !body.Contains("חמנייה"),
                "poem-same-plot" => body.Contains("נועה") && body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length >= 4,
                _ => TextLength.CountWords(body) <= TextLength.CountWords(story.Body) * 0.7
            }, id + "-text");
            await session.Finish(change.Plan, result);
        }

        var third = questions[2].Id;
        session.Begin("replace-easier", plan, document, new("question", third));
        var replace = RequireChange(await session.Revise());
        var replaceWork = RevisionScope.Derive(plan, document, replace);
        Require(replaceWork.Clarification is null && replaceWork.Rewrites.Length == 0 && replaceWork.Questions == "selected" &&
            replaceWork.QuestionEdits.Select(edit => edit.Id).SequenceEqual([third]) && SameRequirements(plan, replace.Plan), "replace-scope");
        var replaced = await session.Execute(plan, document, replace, replaceWork);
        Require(replaced.Questions.Select(q => q.Id).SequenceEqual(questions.Select(q => q.Id)) && SameMaterials(document, replaced) &&
            SameQuestions(questions[..2], replaced.Questions[..2]) && replaced.Questions[2].Prompt != questions[2].Prompt, "replace-content");
        await session.Finish(replace.Plan, replaced);

        session.Begin("remove-third", plan, document);
        var remove = RequireChange(await session.Revise());
        var removeWork = RevisionScope.Derive(plan, document, remove);
        Require(removeWork.Clarification is null && removeWork.Questions == "preserve" && remove.Plan.Settings.QuestionCount == 2 &&
            remove.QuestionOrder is { } order && order.SequenceEqual(questions[..2].Select(q => q.Id)), "remove-scope");
        var removed = await session.Execute(plan, document, remove, removeWork);
        Require(SameMaterials(document, removed) && SameQuestions(questions[..2], removed.Questions), "remove-content");
        await session.Finish(remove.Plan, removed);

        session.Begin("add-focused", plan, document);
        var add = RequireChange(await session.Revise());
        var addWork = RevisionScope.Derive(plan, document, add);
        Require(addWork.Clarification is null && addWork.Questions == "append" && addWork.Rewrites.Length == 0 && add.Plan.Settings.QuestionCount == 4 &&
            !string.IsNullOrWhiteSpace(addWork.Instruction), "add-scope");
        var added = await session.Execute(plan, document, add, addWork);
        Require(added.Questions.Length == 4 && SameMaterials(document, added) && SameQuestions(questions, added.Questions[..3]), "add-originals");
        Require(added.Questions[3].Prompt.Contains("צבע") || added.Questions[3].Prompt.Contains("צהוב"), "add-focus");
        await session.Finish(add.Plan, added);

        session.Begin("vocabulary-focus", plan, document);
        var vocabulary = RequireChange(await session.Revise());
        var vocabularyWork = RevisionScope.Derive(plan, document, vocabulary);
        Require(vocabularyWork.Clarification is null && vocabularyWork.Questions == "all" && vocabularyWork.Rewrites.Length == 0 &&
            vocabularyWork.NewMaterials.Length == 0, "vocabulary-scope");
        var rebuilt = await session.Execute(plan, document, vocabulary, vocabularyWork);
        Require(SameMaterials(document, rebuilt) && rebuilt.Questions.All(q => questions.All(old => old.Id != q.Id)), "vocabulary-content");
        await session.Finish(vocabulary.Plan, rebuilt);

        // Choosing which question to remove is the parent's decision: the planner or the derived scope must ask.
        session.Begin("decrease-unspecified", plan, document);
        var decision = await session.Revise();
        Require(decision.Clarification is not null ||
            decision.Change is { } guessed && RevisionScope.Derive(plan, document, guessed).Clarification is not null, "decrease-clarification");
        await session.Finish(plan, document);
    }

    /// <summary>One run: records each case and call and stops at the first failed expectation.</summary>
    private sealed class ProbeSession(AiGenerationService service, ActivityProbeReport report, Func<Task> save,
        ActivityProbeScenario[] protocol, CancellationToken ct)
    {
        private ActivityProbeCase current = null!;

        internal ActivityProbeCase Begin(string id, LearningPlan? beforePlan = null, TaskDocument? before = null, RevisionTarget? target = null)
        {
            current = new ActivityProbeCase(id, protocol.Single(item => item.Id == id).Message, beforePlan, before, target);
            report.Cases.Add(current);
            return current;
        }

        internal async Task Finish(LearningPlan acceptedPlan, TaskDocument? acceptedDocument)
        {
            if (acceptedDocument is not null && current.Id != "empty-clarification")
                Require(TaskDocumentValidator.ValidateRelease(Resolve(acceptedPlan), acceptedDocument).Count == 0, "release-validation");
            current.Plan = acceptedPlan;
            current.Document = acceptedDocument;
            current.Passed = true;
            await save();
        }

        internal async Task<RevisionDecision> Revise() => (await Call("revise", evidence => service.ReviseAsync(
            new(current.BeforePlan!, current.BeforeDocument!, current.Message, current.Target), ct, evidence))).Value;

        internal async Task<AiResult<T>> Call<T>(string stage, Func<AiCallEvidence, Task<AiResult<T>>> operation)
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

        /// <summary>New texts get ideas, writing and polish; then the complete question batch is generated.</summary>
        internal async Task<TaskDocument> Generate(LearningPlan requirements, TaskDocument before, RevisionWork scope)
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

        /// <summary>
        /// Mirrors <c>GenerationWorker</c>: preparation, existing-text rewrites in plan order, then one question stage, with the
        /// parent's explicit title and instructions holding over them.
        /// </summary>
        internal async Task<TaskDocument> Execute(LearningPlan before, TaskDocument document, RevisionChange change, RevisionWork work) =>
            RevisionScope.ApplyDocument(await Stages(before, document, change, work), work.Document);

        private async Task<TaskDocument> Stages(LearningPlan before, TaskDocument document, RevisionChange change, RevisionWork work)
        {
            if (work.NewMaterials.Length > 0) throw new InvalidOperationException("The edit protocol does not add texts.");
            var input = Resolve(change.Plan);
            var working = RevisionScope.PrepareDocument(before, document, change.Plan, work);
            for (var index = 0; index < work.Rewrites.Length; index++)
            {
                var rewrite = work.Rewrites[index];
                var rewriteInput = new MaterialReplacementInput(input, new TaskDocument("", null, working.Materials, []), rewrite.Id, rewrite.Instruction,
                    work.Rewrites.Skip(index + 1).Select(r => r.Id).ToArray());
                var rewritten = await Call("rewrite-material", evidence => service.ReplaceMaterialAsync(rewriteInput, ct, evidence));
                working = working with { Materials = TaskAssembly.ReplaceMaterial(rewriteInput, rewritten.Value, rewritten.Metadata).Materials };
            }
            switch (work.Questions)
            {
                case "all":
                    var request = TaskAssembly.PrepareQuestions(input, new TaskDocument("", null, working.Materials, [])) with
                    { Current = working, Instruction = work.Instruction };
                    var questions = await Call("questions", evidence => service.GenerateQuestionsAsync(request, [], ct, evidence));
                    return TaskAssembly.AcceptQuestions(input, working, questions.Value, questions.Metadata);
                case "append":
                    var addition = new QuestionAdditionInput(input, working, input.Settings.QuestionCount - working.Questions.Length, work.Instruction);
                    var appended = await Call("append-questions", evidence => service.AppendQuestionsAsync(addition, [], ct, evidence));
                    return TaskAssembly.AppendQuestions(addition, appended.Value, appended.Metadata);
                case "selected":
                    foreach (var edit in work.QuestionEdits)
                    {
                        var replacement = new QuestionReplacementInput(input, working, edit.Id, edit.Instruction);
                        var replaced = await Call("revise-question", evidence => service.ReplaceQuestionAsync(replacement, ct, evidence));
                        working = TaskAssembly.ReplaceQuestion(replacement, replaced.Value, replaced.Metadata);
                    }
                    return working;
                default:
                    return working;
            }
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
    private static bool SameMaterials(TaskDocument left, TaskDocument right) => Equal(Materials(left), Materials(right));
    private static bool SameQuestions(DocumentQuestion[] left, DocumentQuestion[] right) => Equal(QuestionContent(left), QuestionContent(right));
    private static bool SameRequirements(LearningPlan left, LearningPlan right) => Equal(left with { Name = right.Name }, right);
    private static object Project(TaskDocument document) => new
    {
        document.Title,
        document.Instructions,
        materials = Materials(document),
        questions = QuestionContent(document.Questions)
    };
    private static object Materials(TaskDocument document) => document.Materials.Select(m => new { m.Id, m.Revision, m.Title, m.Body }).ToArray();
    private static object QuestionContent(DocumentQuestion[] questions) => questions.Select(q => new { q.Id, q.Prompt, q.Interaction, q.Answer, q.Points }).ToArray();
    private static ResolvedTaskRequest Resolve(LearningPlan plan) => TaskRequestResolver.ResolveOrThrow(plan);
    private static LearningPlan Numeric() => new("תרגול חיבור", "חיבור מספרים עד 10", "", new("חיבור", "כיתה ג׳", "easy", 2), [], new(["numeric-input"], null, ""));
    private static TaskDocument Questions(LearningPlan plan, TaskDocument document) => TaskAssembly.AcceptQuestions(Resolve(plan), document,
        plan.Materials.Length == 0
            ? new("תרגול חיבור", "ענו על השאלות", [new("כמה הם אחת ועוד אחת?", new("numeric-input"), new("2"), 1), new("כמה הם אחת ועוד שתיים?", new("numeric-input"), new("3"), 1)])
            : new("הגינה", "קראו וענו", [new("מי שתל פרח בגינה?", new("text-input"), new("דני"), 1), new("כמה פרחים יש בגינה של נועה?", new("text-input"), new("שלושה"), 1)]));
    private static (LearningPlan, TaskDocument) Mixed(string guidance = "")
    {
        var plan = Numeric() with
        {
            Name = "הגינה",
            Goal = "הבנת הנקרא",
            Guidance = guidance,
            Settings = new("גינה", "כיתה ג׳", "hard", 2),
            Questions = new(["text-input"], null, ""),
            Materials = [new("11111111111111111111111111111111", "סיפור", "generated", "סיפור על גינה", null, null),
                new("22222222222222222222222222222222", "מקור", "supplied", "", "בגינה של נועה יש שלושה פרחים. נועה משקה את הפרחים בבוקר.", null)]
        };
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), new([new(plan.Materials[0].Id!, "הגינה", "דני שתל פרח בגינה. הוא השקה אותו בכל בוקר.")])).Document!;
        return (plan, Questions(plan, document));
    }

    /// <summary>A current grade-3 story with a named character, concrete numbers and three short-answer questions.</summary>
    private static (LearningPlan, TaskDocument) Story()
    {
        var plan = Numeric() with
        {
            Name = "החמנייה של נועה",
            Goal = "הבנת הנקרא",
            Settings = new("גינה", "כיתה ג׳", "easy", 3),
            Questions = new(["text-input"], null, ""),
            Materials = [new("33333333333333333333333333333333", "סיפור", "generated", "סיפור קצר על ילדה ששותלת פרח בגינה", null, new("target", 50))]
        };
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), new([new(plan.Materials[0].Id!, "החמנייה של נועה",
            "נועה קיבלה מסבתא שקית קטנה של זרעי חמנייה. בבוקר היא חפרה גומה בפינת הגינה, שמה בה שלושה זרעים וכיסתה אותם באדמה.\n\n" +
            "כל יום אחרי בית הספר השקתה נועה את האדמה בעדינות. אחרי שבוע הופיעו שני נבטים ירוקים, ונועה רצה לספר לסבתא.\n\n" +
            "בסוף הקיץ צמחה חמנייה גבוהה, ופרחיה הצהובים פנו אל השמש.")])).Document!;
        return (plan, TaskAssembly.AcceptQuestions(request, document, new("החמנייה של נועה", "קראו את הסיפור וענו על השאלות.",
        [
            new("מה קיבלה נועה מסבתא?", new("text-input"), new("זרעי חמנייה"), 1),
            new("כמה זרעים שמה נועה בגומה?", new("text-input"), new("שלושה"), 1),
            new("מה עשתה נועה כל יום אחרי בית הספר?", new("text-input"), new("השקתה את האדמה"), 1)
        ])));
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

internal sealed record ActivityProbeCase(string Id, string Message, LearningPlan? BeforePlan, TaskDocument? BeforeDocument, RevisionTarget? Target = null)
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
