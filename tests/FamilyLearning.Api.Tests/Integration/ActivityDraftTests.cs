using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityDraftTests
{
    [Fact]
    public async Task Unsaved_plan_creates_an_owned_reloadable_draft_without_AI()
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await Create(parent, Supplied());
        Assert.Equal(1, draft["revision"]!.GetValue<long>());
        Assert.Equal(Source, draft["document"]!["materials"]![0]!["body"]!.GetValue<string>());
        Assert.Empty(draft["document"]!["questions"]!.AsArray());
        Assert.NotEmpty(draft["diagnostics"]!.AsObject());
        var id = draft["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/activity-drafts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/activity-drafts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/activity-drafts/{id}")).StatusCode);
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
    }

    [Fact]
    public async Task Lenient_save_keeps_incomplete_answers_and_server_owned_identity()
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var edit = Edit(draft);
        edit["document"] = Document(answer: null);
        var saved = await Save(parent, draft, edit);
        Assert.Equal(2, saved["revision"]!.GetValue<long>());
        Assert.Null(saved["document"]!["questions"]![0]!["answer"]);
        Assert.NotNull(saved["document"]!["questions"]![0]!["id"]);
        Assert.NotEmpty(saved["diagnostics"]!.AsObject());
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(draft), edit)).StatusCode);
        edit = Edit(saved);
        edit["document"]!["questions"]![0]!["origin"] = new JsonObject { ["kind"] = "generated" };
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync(Path(draft), edit)).StatusCode);
    }

    [Fact]
    public async Task Template_copy_pins_the_owned_version_and_preserves_explicit_working_plan_edits()
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var plan = Numeric(1);
        using var published = await parent.PostAsJsonAsync("/api/templates", plan);
        var template = (await published.Content.ReadFromJsonAsync<JsonNode>())!;
        var templateId = template["id"]!.GetValue<Guid>();
        var request = new { templateId, expectedVersion = 1, input = new TaskRequest(plan.Defaults) };
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync("/api/activity-drafts", request)).StatusCode);
        using var created = await parent.PostAsJsonAsync("/api/activity-drafts", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(template["versionId"]!.GetValue<Guid>(), draft["templateVersionId"]!.GetValue<Guid>());
        Assert.True(JsonNode.DeepEquals(template["definition"], draft["plan"]));
        Assert.Equal(HttpStatusCode.Created, (await parent.PostAsJsonAsync($"/api/templates/{templateId}/versions",
            new { expectedVersion = 1, definition = plan with { Name = "גרסה חדשה" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync("/api/activity-drafts", request)).StatusCode);
        using var customized = await parent.PostAsJsonAsync("/api/activity-drafts", new
        {
            templateId,
            expectedVersion = 2,
            plan = plan with { Name = "עותק אישי" },
            input = new TaskRequest(plan.Defaults)
        });
        Assert.Equal(HttpStatusCode.Created, customized.StatusCode);
        Assert.Equal("עותק אישי", (await customized.Content.ReadFromJsonAsync<JsonNode>())!["plan"]!["name"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(draft["plan"], (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!["plan"]));
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("per-task")]
    public async Task Source_replacement_is_atomic_and_stales_questions_without_editing_the_published_template(string kind)
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        var plan = Supplied(kind) with { Defaults = Numeric(1).Defaults };
        using var publication = await parent.PostAsJsonAsync("/api/templates", plan);
        var template = (await publication.Content.ReadFromJsonAsync<JsonNode>())!;
        var input = new TaskRequest(plan.Defaults, MaterialInputs: kind == "fixed" ? default :
            JsonSerializer.SerializeToElement(new Dictionary<string, object> { [MaterialId] = new { sourceText = Source } }));
        using var creation = await parent.PostAsJsonAsync("/api/activity-drafts", new { templateId = template["id"]!.GetValue<Guid>(), expectedVersion = 1, input });
        var draft = (await creation.Content.ReadFromJsonAsync<JsonNode>())!;
        var edit = Edit(draft);
        edit["document"]!["title"] = "קריאה";
        edit["document"]!["questions"] = Document()["questions"]!.DeepClone();
        draft = await Save(parent, draft, edit);
        edit = Edit(draft);
        const string replacement = "שלום, Maya!\nמקור חדש עם ציטוט: \"hello\".";
        if (kind == "fixed") edit["plan"]!["materials"]![0]!["text"] = replacement;
        else edit["input"]!["materialInputs"]![MaterialId]!["sourceText"] = replacement;
        var changed = await Save(parent, draft, edit);
        Assert.Equal(replacement, changed["document"]!["materials"]![0]!["body"]!.GetValue<string>());
        Assert.Equal(2, changed["document"]!["materials"]![0]!["revision"]!.GetValue<long>());
        Assert.Contains("questions[0].stale", changed["diagnostics"]!.AsObject().Select(p => p.Key));
        var published = await parent.GetFromJsonAsync<JsonNode>($"/api/templates/{template["id"]!.GetValue<Guid>()}");
        Assert.True(JsonNode.DeepEquals(template["definition"], published!["definition"]));
        edit = Edit(changed);
        edit["document"]!["materials"]![0]!["body"] = "unapproved source rewrite";
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync(Path(changed), edit)).StatusCode);
    }

    [Fact]
    public async Task Material_edits_stale_dependencies_and_adoption_cannot_waive_strict_length()
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        var plan = Reading() with { Defaults = Numeric(1).Defaults, Materials = [Reading().Materials[0] with { Length = new("exact", new(2, false)) }] };
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"] = Document();
        edit["document"]!["questions"]![0]!["interaction"]!["type"] = "text-input";
        edit["document"]!["materials"] = new JsonArray(new JsonObject { ["id"] = MaterialId, ["title"] = null, ["body"] = "שלום עולם" });
        draft = await Save(parent, draft, edit);
        Assert.Empty(draft["diagnostics"]!.AsObject());
        edit = Edit(draft);
        edit["document"]!["materials"]![0]!["body"] = "קצר";
        draft = await Save(parent, draft, edit);
        var questionId = draft["document"]!["questions"]![0]!["id"]!.GetValue<string>();
        var adoption = new { expectedRevision = draft["revision"]!.GetValue<long>(), materialIds = new[] { MaterialId }, questionIds = new[] { questionId } };
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/adopt-content", adoption)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { adoption.expectedRevision })).StatusCode);
        edit = Edit(draft);
        edit["document"]!["materials"]![0]!["body"] = "סיפור חדש";
        draft = await Save(parent, draft, edit);
        Assert.Contains("questions[0].stale", draft["diagnostics"]!.AsObject().Select(p => p.Key));
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() })).StatusCode);
        using var adopted = await parent.PostAsJsonAsync(Path(draft) + "/adopt-content", new
        {
            expectedRevision = draft["revision"]!.GetValue<long>(),
            materialIds = Array.Empty<string>(),
            questionIds = new[] { questionId }
        });
        Assert.Equal(HttpStatusCode.OK, adopted.StatusCode);
        var current = (await adopted.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Empty(current["diagnostics"]!.AsObject());
        Assert.True(JsonNode.DeepEquals(draft["document"]!["questions"]![0]!["origin"], current["document"]!["questions"]![0]!["origin"]));
        Assert.NotNull(current["document"]!["questions"]![0]!["acceptance"]!["adoptedAtUtc"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_and_restoring_a_material_requires_explicit_question_acceptance(bool removeRequirement)
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        var plan = Reading() with { Defaults = Numeric(1).Defaults, Materials = [Reading().Materials[0] with { Length = null }] };
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"] = Document();
        edit["document"]!["questions"]![0]!["interaction"]!["type"] = "text-input";
        edit["document"]!["materials"] = new JsonArray(new JsonObject { ["id"] = MaterialId, ["title"] = null, ["body"] = "מקור ראשון" });
        draft = await Save(parent, draft, edit);
        Assert.Empty(draft["diagnostics"]!.AsObject());
        var originalPlan = draft["plan"]!.DeepClone();
        var originalOrigin = draft["document"]!["questions"]![0]!["origin"]!.DeepClone();
        edit = Edit(draft);
        edit["document"]!["materials"] = new JsonArray();
        if (removeRequirement) edit["plan"]!["materials"] = new JsonArray();
        draft = await Save(parent, draft, edit);
        edit = Edit(draft);
        edit["plan"] = originalPlan;
        edit["document"]!["materials"] = new JsonArray(new JsonObject { ["id"] = MaterialId, ["title"] = null, ["body"] = "מקור אחר" });
        draft = await Save(parent, draft, edit);
        Assert.Contains("questions[0].stale", draft["diagnostics"]!.AsObject().Select(p => p.Key));
        Assert.True(JsonNode.DeepEquals(originalOrigin, draft["document"]!["questions"]![0]!["origin"]));
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() })).StatusCode);
        using var adopted = await parent.PostAsJsonAsync(Path(draft) + "/adopt-content", new
        {
            expectedRevision = draft["revision"]!.GetValue<long>(),
            materialIds = Array.Empty<string>(),
            questionIds = new[] { draft["document"]!["questions"]![0]!["id"]!.GetValue<string>() }
        });
        Assert.Equal(HttpStatusCode.OK, adopted.StatusCode);
        Assert.Empty((await adopted.Content.ReadFromJsonAsync<JsonNode>())!["diagnostics"]!.AsObject());
    }

    [Fact]
    public async Task Effective_input_changes_stale_unchanged_content_and_active_work_blocks_release_but_allows_edits()
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var edit = Edit(draft);
        edit["document"] = Document();
        draft = await Save(parent, draft, edit);
        edit = Edit(draft);
        edit["input"]!["settings"]!["topic"] = "נושא חדש";
        draft = await Save(parent, draft, edit);
        Assert.Contains("questions[0].stale", draft["diagnostics"]!.AsObject().Select(p => p.Key));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var row = await db.ActivityDrafts.SingleAsync();
            db.Entry(row).Property(d => d.ActiveOperationId).CurrentValue = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        draft = await Save(parent, draft, Edit(draft));
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() })).StatusCode);
    }

    [Fact]
    public async Task Changing_a_choice_does_not_choose_a_new_answer_or_discard_the_incomplete_edit()
    {
        await using var app = new ActivityApiFactory();
        using var parent = await app.ParentAsync();
        var plan = Numeric(1) with { Questions = new(["single-choice"], false, null, new(2, false), null, "", []) };
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"] = Document();
        edit["document"]!["questions"]![0]!["interaction"] = new JsonObject { ["type"] = "single-choice", ["options"] = new JsonArray("1", "2") };
        draft = await Save(parent, draft, edit);
        edit = Edit(draft);
        edit["document"]!["questions"]![0]!["interaction"]!["options"]![1] = "3";
        draft = await Save(parent, draft, edit);
        Assert.Equal("2", draft["document"]!["questions"]![0]!["answer"]!["value"]!.GetValue<string>());
        Assert.Contains("questions[0].answer", draft["diagnostics"]!.AsObject().Select(p => p.Key));
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 3 })).StatusCode);
    }

    internal static async Task<JsonNode> Create(HttpClient parent, LearningPlan plan)
    {
        using var response = await parent.PostAsJsonAsync("/api/activity-drafts", new { plan, input = new TaskRequest(plan.Defaults) });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    internal static string Path(JsonNode draft) => $"/api/activity-drafts/{draft["id"]!.GetValue<Guid>()}";

    internal static JsonObject Edit(JsonNode draft) => new()
    {
        ["expectedRevision"] = draft["revision"]!.DeepClone(),
        ["plan"] = draft["plan"]!.DeepClone(),
        ["input"] = draft["input"]!.DeepClone(),
        ["document"] = new JsonObject
        {
            ["title"] = draft["document"]!["title"]!.DeepClone(),
            ["instructions"] = draft["document"]!["instructions"]?.DeepClone(),
            ["materials"] = new JsonArray(draft["document"]!["materials"]!.AsArray().Select(m => (JsonNode)new JsonObject
            {
                ["id"] = m!["id"]!.DeepClone(),
                ["title"] = m["title"]?.DeepClone(),
                ["body"] = m["body"]!.DeepClone()
            }).ToArray()),
            ["questions"] = new JsonArray(draft["document"]!["questions"]!.AsArray().Select(q => (JsonNode)new JsonObject
            {
                ["id"] = q!["id"]!.DeepClone(),
                ["prompt"] = q["prompt"]!.DeepClone(),
                ["interaction"] = q["interaction"]!.DeepClone(),
                ["answer"] = q["answer"]?.DeepClone(),
                ["points"] = q["points"]!.DeepClone()
            }).ToArray())
        }
    };

    internal static JsonNode Document(string? answer = "2") => JsonSerializer.SerializeToNode(new
    {
        title = "תרגול",
        instructions = "ענו",
        materials = Array.Empty<object>(),
        questions = new[] { new { id = (string?)null, prompt = "כמה הם 1 ועוד 1?", interaction = new { type = "numeric-input", options = (string[]?)null },
            answer = answer is null ? null : new { value = answer }, points = 1 } }
    })!;

    internal static async Task<JsonNode> Save(HttpClient parent, JsonNode draft, JsonNode edit)
    {
        using var response = await parent.PutAsJsonAsync(Path(draft), edit);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }
}
