# Family Learning App
## KISS Dynamic Product & Technical Specification

**Version:** 3.0  
**Target:** Private family educational application  
**Architecture:** Angular/Ionic PWA + ASP.NET Core 10 + EF Core 10 + SQLite + optional AI generation  
**Design principle:** Simple core, highly dynamic content

---

# 1. Product Idea

The application is a small educational platform for a family.

It starts empty:

```text
Templates: 0
Tasks:     0
Results:   0
```

The application does not ship with a large fixed library of educational exercises.

Instead, parents create reusable **Task Templates**.

Templates can be created:

- manually;
- with AI assistance.

A template is not necessarily a task itself.

It is a reusable blueprint describing:

```text
What kind of task is this?

What can the parent configure each time?

How should a concrete task be generated?

How should the child interact with it?

How should it be scored?
```

Example:

```text
Template:
Reading Practice

Every time I create a task, ask me for:

Difficulty
Theme
Story length
Number of questions
```

Today:

```text
Theme: Dinosaurs
Difficulty: Easy
Length: Short
Questions: 5
```

Tomorrow:

```text
Theme: Space
Difficulty: Medium
Length: Medium
Questions: 8
```

Both come from the same reusable template.

---

# 2. The Core Model

The whole educational domain should revolve around only five concepts:

```text
TaskTemplate
      ↓
TaskTemplateVersion
      ↓
Instance Parameters
      ↓
TaskInstance
      ↓
TaskSession
```

## TaskTemplate

The permanent identity.

Example:

```text
Reading Practice
```

## TaskTemplateVersion

An immutable version of the blueprint.

It defines:

```text
what is fixed
what is configurable
how generation works
how scoring works
```

## Instance Parameters

Values chosen by the parent when creating one task.

Example:

```text
difficulty = medium
theme = space
length = short
questionCount = 5
```

## TaskInstance

The exact frozen task generated for the child.

Example:

```text
"The Lost Astronaut"

<story>

Question 1...
Question 2...
...
```

## TaskSession

What the child actually did.

Example:

```text
4/5 correct
2 attempts on question 3
7m 42s
```

This is the fundamental architecture.

---

# 3. Important Distinction

A template is:

> How tasks of this type should be created.

A task instance is:

> The exact task the child receives this time.

A session is:

> What happened when the child performed that task.

Never merge these concepts.

---

# 4. High-Level Architecture

```text
                    Angular + Ionic
                         PWA
                          │
                          │ HTTPS
                          ▼
                 ASP.NET Core 10
                 Modular Monolith
                          │
       ┌──────────────────┼──────────────────┐
       │                  │                  │
       ▼                  ▼                  ▼
   EF Core          Task Engine        AI Generation
       │                  │                  │
       ▼                  │             IChatClient
    SQLite                │                  │
                          │        OpenRouter / DeepSeek /
                          │         OpenAI / Gemini / ...
                          │
                          ▼
                    Task Instance
```

There is:

```text
one frontend
one backend
one database
```

No microservices.

No message broker.

No Redis.

No Kubernetes.

No separate AI service.

---

# 5. Technology Stack

## Frontend

```text
Angular 22
Ionic
TypeScript
PWA
Angular Signals
Angular HttpClient
```

Use:

```text
standalone components
strict TypeScript
modern Angular control flow
signals/computed
lazy feature routes
```

Do not start with NgRx.

## Backend

```text
.NET 10
ASP.NET Core
Minimal APIs
EF Core 10
SQLite
Microsoft.Extensions.AI
```

## Production

Initially:

```text
one ASP.NET Core process
        │
        ├── serves Angular
        ├── serves /api/*
        │
        └── accesses SQLite
```

---

# 6. Same-Origin Deployment

Production should look like:

```text
https://learning.example.com/
    → Angular

https://learning.example.com/api/*
    → ASP.NET Core
```

ASP.NET Core serves the Angular production files.

This provides:

```text
simple cookies
no unnecessary CORS
one deployment
one domain
simple HTTPS
```

---

# 7. Template Definition

The heart of the system is `TaskTemplateDefinition`.

Conceptually:

```json
{
  "schemaVersion": 1,

  "name": "Reading Practice",

  "instanceParameters": [],

  "generation": {},

  "scoring": {}
}
```

A template definition answers three major questions:

```text
1. What can the parent configure?

2. How do we generate a task from those values?

3. How do we score the resulting task?
```

---

# 8. Parameterized Templates

Every template may expose zero or more parameters.

Example reading template:

```json
{
  "instanceParameters": [
    {
      "key": "difficulty",
      "label": "Difficulty",
      "type": "select",
      "required": true,
      "default": "medium",
      "options": [
        "easy",
        "medium",
        "hard"
      ]
    },
    {
      "key": "theme",
      "label": "Story theme",
      "type": "text",
      "required": true
    },
    {
      "key": "length",
      "label": "Story length",
      "type": "select",
      "default": "short",
      "options": [
        "short",
        "medium",
        "long"
      ]
    },
    {
      "key": "questionCount",
      "label": "Questions",
      "type": "integer",
      "default": 5,
      "min": 3,
      "max": 10
    }
  ]
}
```

Angular dynamically turns this into:

```text
Reading Practice

Difficulty
[ Medium ▼ ]

Theme
[ Space exploration        ]

Story Length
[ Short ▼ ]

Questions
[ 5 ]

[ Generate Task ]
```

There is no custom `ReadingAssignmentComponent`.

The form comes from the template.

---

# 9. Supported Parameter Types

Keep parameter types intentionally small.

MVP:

```text
text
integer
select
boolean
```

Example definitions:

### Text

```json
{
  "key": "theme",
  "label": "Theme",
  "type": "text",
  "required": true,
  "maxLength": 100
}
```

### Integer

```json
{
  "key": "questionCount",
  "label": "Number of questions",
  "type": "integer",
  "min": 1,
  "max": 20,
  "default": 5
}
```

### Select

```json
{
  "key": "difficulty",
  "label": "Difficulty",
  "type": "select",
  "options": [
    "easy",
    "medium",
    "hard"
  ],
  "default": "medium"
}
```

### Boolean

```json
{
  "key": "allowRetry",
  "label": "Allow retry",
  "type": "boolean",
  "default": true
}
```

Do not create a generic form framework beyond what the product actually requires.

---

# 10. Fixed Settings vs Dynamic Parameters

A template contains both:

```text
Fixed settings
+
Instance parameters
```

Example:

```text
Reading Practice

FIXED
Language = English
Answer type = Multiple choice

CONFIGURABLE EACH TIME
Theme
Difficulty
Story length
Question count
```

The parent does not need to configure every technical detail every time.

---

# 11. Three Generation Strategies

Every template uses one of three generation modes:

```text
Static
Deterministic
AI
```

This single abstraction handles almost everything we need.

---

# 12. Static Generation

Static means the content already exists in the template.

Example:

```text
Capital Cities Quiz
```

Template stores:

```text
Question 1
Question 2
Question 3
...
```

Instantiation simply copies the content.

```text
Template
   ↓
copy
   ↓
TaskInstance
```

No AI.

---

# 13. Deterministic Generation

Deterministic means normal C# generates the content.

Example:

```text
Multiplication Practice
```

Template might have:

```text
Fixed:

operation = multiplication

Parameters:

difficulty
questionCount
```

Parent chooses:

```text
Difficulty = Medium
Questions = 15
```

Backend runs:

```text
MathTaskGenerator
```

and creates:

```text
7 × 8
4 × 6
3 × 9
...
```

No AI is needed.

---

# 14. AI Generation

AI generation is used when the task requires genuinely new creative content.

Example:

```text
Reading Practice
```

Parent chooses:

```text
Theme = Dinosaurs
Difficulty = Medium
Length = Short
Questions = 5
```

Backend does:

```text
Template instructions
       +
Instance parameters
       ↓
IChatClient
       ↓
structured TaskContent
       ↓
validation
       ↓
TaskInstance
```

Example result:

```text
The Lost Dinosaur Egg

Tom was walking through...

Question 1...
Question 2...
...
```

The generated instance is then frozen.

Opening it later never calls AI again.

---

# 15. Why We Need All Three

Examples:

| Template | Generation |
|---|---|
| Fixed geography quiz | Static |
| Multiplication | Deterministic |
| Addition | Deterministic |
| Vocabulary from stored word pool | Deterministic |
| New reading story | AI |
| New science quiz on a chosen theme | AI |
| New English story | AI |

Do not force everything through AI.

Do not force everything to avoid AI either.

Use the simplest generation strategy appropriate to the task.

---

# 16. Generic AI Generator

Avoid creating:

```text
ReadingAiGenerator
ScienceAiGenerator
HistoryAiGenerator
SpaceAiGenerator
AnimalAiGenerator
```

Instead have one generic:

```text
AiTaskGenerator
```

The template contains generation instructions.

Example:

```json
{
  "mode": "ai",

  "instructions":
    "Create an age-appropriate English reading passage.
     Use the provided difficulty, theme and length.
     Then create the requested number of comprehension
     questions."
}
```

At instance creation:

```text
Template instructions:

"Create an age-appropriate English reading passage..."

+

Parameters:

theme = dinosaurs
difficulty = medium
length = short
questionCount = 5

↓

AI
```

This keeps the system extremely dynamic.

---

# 17. AI Does Not Generate Arbitrary UI

AI output must always conform to the task-content schema supported by the application.

The AI cannot invent:

```text
new Angular components
JavaScript
HTML
executables
new database structures
```

It can only create data.

For example:

```json
{
  "title": "The Lost Dinosaur Egg",

  "contentBlocks": [
    {
      "type": "text",
      "text": "Tom walked through..."
    }
  ],

  "questions": [
    {
      "id": "q1",
      "prompt": "What did Tom find?",
      "interaction": {
        "type": "single-choice",
        "options": [
          "A dinosaur egg",
          "A bicycle",
          "A book"
        ]
      },

      "answer": {
        "value": "A dinosaur egg"
      },

      "points": 10
    }
  ]
}
```

The application understands this schema.

---

# 18. Generic Task Content

Keep generated task content simple.

Conceptually:

```csharp
public sealed record TaskContent(
    string Title,
    string? Instructions,
    IReadOnlyList<ContentBlock> ContentBlocks,
    IReadOnlyList<TaskQuestion> Questions);
```

`ContentBlocks` allow things such as:

```text
story
reading passage
instructions
introductory information
```

Initially support only plain text.

Images/audio can come later.

---

# 19. Question Interaction Types

MVP child interactions:

```text
numeric-input
text-input
single-choice
```

That is enough to support:

```text
math
spelling
translation
reading comprehension
science quizzes
general knowledge
vocabulary
```

Future additions can include:

```text
multiple-choice
true-false
matching
ordering
audio
speech
image-selection
```

Add them only when needed.

---

# 20. Generic Child Renderer

Angular should have one task player.

```text
TaskContent
     │
     ▼
TaskPlayer
     │
     ├── NumericInputRenderer
     ├── TextInputRenderer
     └── SingleChoiceRenderer
```

The frontend does not care whether the task is:

```text
English
Math
History
Dinosaurs
Space
Reading
```

It cares only about:

```text
content blocks
questions
interaction type
```

That is the key to keeping the UI dynamic.

---

# 21. Template Authoring with AI

Parents can create templates using a prompt.

Example:

> Create a reusable English reading task for an 8-year-old. Every time I use it, let me choose the difficulty, story theme, story length and number of questions.

AI should return a `TaskTemplateDraft`.

Example:

```text
Reading Practice

Generation:
AI

Fixed:
Language = English

Configurable:
Difficulty
Theme
Length
Question count
```

Parent sees the draft.

Parent can:

```text
Edit
Regenerate
Save
Cancel
```

AI never saves the template itself.

---

# 22. AI Template Authoring vs AI Instance Generation

These are separate operations.

## Template Authoring

Occurs rarely.

```text
Parent description
       ↓
AI
       ↓
TaskTemplateDraft
       ↓
Parent saves
```

## Instance Generation

Occurs when a template uses `generation.mode = ai`.

```text
Saved template
      +
current parameters
       ↓
AI
       ↓
TaskInstance
```

A deterministic template does not perform the second AI call.

---

# 23. AI Provider Abstraction

Backend application code depends on:

```csharp
IChatClient
```

not directly on:

```text
OpenAI
DeepSeek
OpenRouter
Gemini
```

Configuration selects the provider/model.

Example:

```json
{
  "Ai": {
    "Provider": "OpenRouter",
    "Model": "openrouter/free"
  }
}
```

Later:

```json
{
  "Ai": {
    "Provider": "DeepSeek",
    "Model": "..."
  }
}
```

Changing model/provider should not modify domain logic.

---

# 24. AI Responsibilities

AI may:

```text
interpret parent intent
create template drafts
write reading passages
create quiz questions
create answer choices
adapt wording to difficulty
```

AI must not control:

```text
authentication
authorization
database access
task status
session status
scoring calculations
permissions
template versioning
deployment
```

---

# 25. Structured AI Output

Never ask the AI:

> Please write some JSON.

Use structured output against an application-owned schema.

Flow:

```text
AI
 ↓
structured result
 ↓
deserialize
 ↓
validate structure
 ↓
validate business rules
 ↓
use
```

AI output is always untrusted input.

---

# 26. Validation

Every generated template and task goes through application validation.

Example template checks:

```text
parameter keys unique
supported parameter types only
defaults valid
integer ranges valid
supported generation mode
supported deterministic generator
reasonable limits
```

Example task checks:

```text
question count matches request
every question has an interaction
every question has a valid answer
single-choice answer exists in options
points >= 0
content length within limits
```

---

# 27. Parent Preview

There should be two different preview steps.

## Template preview

Before saving an AI-created template.

```text
AI created template
      ↓
Parent reviews blueprint
      ↓
Save
```

## Instance preview

Especially useful for AI-generated instances.

```text
Generate task
      ↓
exact story/questions generated
      ↓
Parent preview
      ↓
Assign to child
```

This ensures AI-generated child content remains parent-controlled.

For deterministic tasks we may later support:

```text
Generate & Assign
```

as a shortcut.

---

# 28. Instance Creation Flow

Parent opens a saved template.

Example:

```text
Reading Practice

[ Create Task ]
```

Angular reads the template's parameter definitions.

It dynamically renders:

```text
Difficulty
[ Medium ▼ ]

Theme
[ Space ]

Length
[ Short ▼ ]

Questions
[ 5 ]

[ Generate ]
```

Request:

```http
POST /api/templates/{templateId}/instances
```

Body:

```json
{
  "parameters": {
    "difficulty": "medium",
    "theme": "space",
    "length": "short",
    "questionCount": 5
  }
}
```

Backend:

```text
load latest template version
        ↓
validate parameters
        ↓
select generator
        ↓
generate content
        ↓
validate content
        ↓
save TaskInstance as Draft
        ↓
return preview
```

Parent then chooses:

```text
[ Assign ]
```

---

# 29. Task Instance

A task instance must store:

```text
TemplateVersion used
Instance parameters
Exact generated content
Generation method
Created timestamp
Status
```

Conceptually:

```csharp
public sealed class TaskInstance
{
    public Guid Id { get; set; }

    public Guid ChildId { get; set; }

    public Guid TemplateVersionId { get; set; }

    public string ParametersJson { get; set; } = null!;

    public string ContentJson { get; set; } = null!;

    public TaskInstanceStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? AssignedAtUtc { get; set; }
}
```

Once assigned, the exact content must not change.

---

# 30. Task Instance Status

Keep statuses minimal:

```text
Draft
Assigned
InProgress
Completed
Cancelled
```

Example lifecycle:

```text
Generate
   ↓
Draft
   ↓
Assign
   ↓
Assigned
   ↓
Child starts
   ↓
InProgress
   ↓
Complete
   ↓
Completed
```

---

# 31. Template Versioning

Published templates are immutable.

Example:

```text
Reading Practice v1

Difficulty:
Easy / Medium / Hard

Theme:
Text
```

Later the parent edits it to support:

```text
Very Easy
Easy
Medium
Hard
```

Create:

```text
Reading Practice v2
```

Do not rewrite v1.

Existing instances remain attached to v1.

---

# 32. Template Data Model

Database:

```csharp
TaskTemplate
```

stores identity:

```text
Id
FamilyId
Name
CurrentVersion
Status
CreatedAtUtc
UpdatedAtUtc
```

`TaskTemplateVersion` stores the immutable blueprint:

```text
Id
TemplateId
Version
DefinitionJson
CreatedAtUtc
AuthoringSource
```

`DefinitionJson` contains:

```text
parameter schema
generation definition
scoring
presentation settings
schema version
```

---

# 33. Why JSON for Template Definitions

Task templates are naturally polymorphic.

A math configuration may contain:

```text
operation
operand ranges
```

A reading template may contain:

```text
language
generation instructions
```

Trying to represent every possibility with relational columns would become messy.

Therefore:

```text
relational database
    → identity and relationships

JSON
    → dynamic template/task configuration
```

We do not need complex JSON querying in SQLite.

We deserialize it into strongly typed C# objects.

---

# 34. Generation Definition

Conceptually:

```csharp
public abstract record GenerationDefinition;
```

Implementations:

```csharp
StaticGenerationDefinition

DeterministicGenerationDefinition

AiGenerationDefinition
```

Example deterministic:

```json
{
  "mode": "deterministic",
  "generator": "math-v1",

  "fixedSettings": {
    "operation": "multiplication"
  }
}
```

Example AI:

```json
{
  "mode": "ai",

  "instructions":
    "Create an English reading comprehension task
     appropriate to the provided settings."
}
```

---

# 35. Deterministic Generator Registry

Do not dynamically execute code.

Use known generators.

Example:

```text
math-v1
vocabulary-pool-v1
```

Conceptually:

```csharp
ITaskGenerator
```

with implementations:

```csharp
MathTaskGenerator
VocabularyPoolTaskGenerator
```

Registry:

```text
"math-v1"
     ↓
MathTaskGenerator
```

The template references only registered generators.

---

# 36. AI Generator

AI generation can remain mostly generic.

Conceptually:

```csharp
AiTaskGenerator
```

Input:

```text
template generation instructions
+
validated parameter values
+
supported task-content schema
```

Output:

```text
TaskContent
```

This allows many educational categories without backend code changes.

For example, the same generator can support:

```text
reading
history
science
English
general knowledge
creative quizzes
```

because the difference is data/instructions, not executable code.

---

# 37. Example: Dynamic Math Template

Template:

```text
Multiplication Practice
```

Fixed:

```text
operation = multiplication
```

Parameters:

```text
difficulty
questionCount
```

Parent chooses:

```text
difficulty = hard
questionCount = 20
```

Backend:

```text
MathTaskGenerator
```

produces a fresh instance.

No AI.

---

# 38. Example: Dynamic Reading Template

Template:

```text
English Reading
```

Fixed:

```text
language = English
interaction = single-choice
```

Parameters:

```text
theme
difficulty
storyLength
questionCount
```

Parent chooses:

```text
theme = ancient Egypt
difficulty = medium
storyLength = short
questionCount = 5
```

Backend:

```text
AiTaskGenerator
```

produces a new story and questions.

---

# 39. Example: Reuse

Template:

```text
English Reading
```

Assignment 1:

```text
Theme: Dinosaurs
Difficulty: Easy
Questions: 5
```

Assignment 2:

```text
Theme: Space
Difficulty: Medium
Questions: 7
```

Assignment 3:

```text
Theme: Pokémon
Difficulty: Hard
Questions: 10
```

Same template.

Different parameters.

Different task instances.

---

# 40. Useful Future Operation: Recreate

History can show:

```text
Space
Medium
5 questions

[ Create Another ]
```

This copies the old **parameters**, not the old generated content.

Result:

```text
same configuration
+
new generation
=
new task
```

---

# 41. Useful Future Operation: Duplicate Exact Task

Different operation:

```text
[ Duplicate Exact Task ]
```

This copies:

```text
same content
same questions
```

without generating anything.

Keep these operations conceptually separate.

---

# 42. Child Task Session

When the child starts:

```text
TaskInstance
     ↓
TaskSession
```

A session stores:

```text
started time
completion time
answers
attempt count
score
maximum score
```

Example:

```csharp
public sealed class TaskSession
{
    public Guid Id { get; set; }

    public Guid TaskInstanceId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int? Score { get; set; }

    public int? MaximumScore { get; set; }
}
```

---

# 43. Answers

Store answer state separately.

Example:

```text
TaskAnswer

SessionId
QuestionId
AnswerJson
AttemptCount
IsCorrect
AnsweredAtUtc
```

`AnswerJson` keeps the model flexible enough for different interaction types.

---

# 44. Server-Side Scoring

Backend scoring is authoritative.

Example:

```text
Question:
7 × 8

Correct answer:
56

Child answer:
56

Points:
10
```

The backend calculates:

```text
10 points
```

Never ask the LLM to calculate session scores.

---

# 45. Parent Reports

MVP reports should be simple.

Dashboard:

```text
Today

Completed: 3
Correct:   31 / 35
Time:      22 minutes
```

Session detail:

```text
Reading Practice

Theme: Space
Difficulty: Medium

4 / 5 correct

Question 1    ✓
Question 2    ✓
Question 3    ✗
Question 4    ✓
Question 5    ✓
```

No analytics platform is required.

---

# 46. Authentication

Use ASP.NET Core Identity for parents.

Use cookie authentication.

Production:

```text
HttpOnly
Secure
SameSite
```

Do not store authentication tokens in:

```text
localStorage
sessionStorage
IndexedDB
```

---

# 47. Child Device Login

An 8-year-old should not repeatedly type credentials.

Use device activation.

Parent:

```text
Activate Child Device
```

Server creates:

```text
temporary activation code
```

Child phone enters code once.

Server establishes a persistent child authentication cookie.

Parent can later revoke the device.

---

# 48. Authorization

Roles:

```text
Parent
Child
```

Parent can:

```text
manage templates
generate instances
assign tasks
view reports
manage child devices
```

Child can:

```text
view own assigned tasks
start own sessions
submit own answers
complete own tasks
```

Always validate ownership server-side.

Do not trust IDs supplied by the browser.

---

# 49. API

Keep HTTP API small.

## Authentication

```text
POST /api/auth/login
POST /api/auth/logout
GET  /api/auth/me
```

## Templates

```text
GET  /api/templates
GET  /api/templates/{id}

POST /api/templates/generate-draft
POST /api/templates

POST /api/templates/{id}/versions
```

## Instances

```text
POST /api/templates/{id}/instances

GET  /api/instances/{id}

POST /api/instances/{id}/assign
POST /api/instances/{id}/cancel
```

## Child

```text
GET /api/child/tasks
GET /api/child/tasks/{id}
```

## Sessions

```text
POST /api/child/tasks/{id}/sessions

PUT /api/child/sessions/{sessionId}/answers/{questionId}

POST /api/child/sessions/{sessionId}/complete
```

## Reports

```text
GET /api/reports/summary
GET /api/reports/sessions/{id}
```

Do not create dozens of controllers/services prematurely.

---

# 50. Database

Use EF Core with SQLite.

Main tables:

```text
Families
Parents / Identity tables
Children
ChildDevices

TaskTemplates
TaskTemplateVersions
TaskInstances

TaskSessions
TaskAnswers
```

That's enough.

---

# 51. SQLite Strategy

SQLite is a deliberate choice for this application.

Production:

```text
/data/family-learning.db
```

Keep:

```text
single ASP.NET instance
+
persistent storage
```

If the application eventually needs multiple API instances, migrate to PostgreSQL.

Until then SQLite is simpler.

---

# 52. SQLite Date/Number Conventions

Persist timestamps as UTC `DateTime`.

Example:

```csharp
DateTime CreatedAtUtc
```

Use integer scores whenever possible.

Avoid designing around provider-specific SQLite behavior.

This keeps future PostgreSQL migration easier.

---

# 53. EF Core Usage

Use `DbContext` directly.

Do not add generic wrappers such as:

```text
IGenericRepository<T>
GenericRepository<T>
IUnitOfWork
```

EF Core already provides these capabilities.

Introduce abstractions for real business capabilities:

```text
ITaskGenerator
ITaskTemplateDraftGenerator
ITaskScoringService
```

not generic database ceremony.

---

# 54. Backend Structure

Keep one production `.csproj` initially.

Example:

```text
FamilyLearning.Api/

  Features/

    Auth/
    Children/

    Templates/
      GenerateDraft/
      Create/
      CreateVersion/
      Get/

    Instances/
      Create/
      Assign/
      Get/

    Sessions/
      Start/
      SaveAnswer/
      Complete/

    Reports/

  TaskEngine/
    Models/
    Generators/
    Validation/
    Scoring/

  Ai/
    Prompts/
    Configuration/

  Infrastructure/
    Persistence/
    Auth/

  Program.cs
```

Do not split into six class-library projects just to simulate Clean Architecture.

---

# 55. Frontend Structure

```text
src/app/

  core/
    auth/
    api/

  features/

    parent/
      dashboard/
      templates/
      create-template/
      create-instance/
      reports/

    child/
      home/
      task-player/
      completion/

  task-engine/
    renderers/

  dynamic-form/
    parameter-field/

  shared/
    ui/
```

Important reusable frontend pieces:

```text
DynamicParameterForm

TaskPlayer
```

Those are the two main generic UI engines.

---

# 56. Dynamic Parameter Form

The parent assignment UI should be generic.

Input:

```text
InstanceParameterDefinition[]
```

Output:

```text
ParameterValues
```

Example:

```typescript
type ParameterValues =
  Record<string, string | number | boolean>;
```

The backend still performs authoritative validation.

Frontend validation exists for UX only.

---

# 57. Template Creation UX

Parent starts with:

```text
Templates

No templates yet.

[ Create Template ]
```

Then:

```text
Describe what you want:

┌─────────────────────────────────────────┐
│ Create a reusable English reading      │
│ task. Each time let me choose theme,   │
│ difficulty, length and questions.      │
└─────────────────────────────────────────┘

[ Generate Template ]
```

Preview:

```text
Reading Practice

Generated using AI

Each task will ask for:

✓ Theme
✓ Difficulty
✓ Length
✓ Number of questions

Generation:
AI

[ Edit ]
[ Save ]
```

---

# 58. Creating a Task

Saved template:

```text
Reading Practice

[ Create Task ]
```

Generated form:

```text
Theme
[ Dinosaurs ]

Difficulty
[ Medium ▼ ]

Length
[ Short ▼ ]

Questions
[ 5 ]

[ Generate ]
```

After generation:

```text
The Lost Dinosaur Egg

<story preview>

5 questions

[ Regenerate ]
[ Assign ]
[ Cancel ]
```

---

# 59. Child UX

Child home:

```text
Today's Tasks

┌───────────────────────┐
│ Reading Practice      │
│                       │
│ Space Adventure       │
│                       │
│      [ Start ]        │
└───────────────────────┘
```

Task:

```text
Question 2 / 5

Why did the astronaut return
to the spaceship?

○ He was hungry
○ A storm was coming
○ He found a dog

[ Answer ]
```

Completion:

```text
Great job! 🎉

4 / 5 correct

[ Done ]
```

Keep the child experience extremely simple.

---

# 60. PWA

The initial Android application is a PWA.

Child installs it from Chrome to the home screen.

Advantages:

```text
no Play Store required
same Angular code
fast deployment
easy updates
```

Capacitor can be added later if native features become necessary.

---

# 61. Offline Support

Do not make full offline synchronization an MVP requirement.

That adds substantial complexity.

Initial version:

```text
Internet required while doing tasks
```

PWA caching may still provide fast application loading.

Later, if needed:

```text
cache assigned task
store answers in IndexedDB
sync later
```

Build this only after the normal online workflow is solid.

---

# 62. AI Prompt Management

Application prompts live in source control.

Example:

```text
Ai/
  Prompts/
    TemplateAuthoringPrompt.cs
    InstanceGenerationPrompt.cs
```

Separate:

```text
template authoring prompt
```

from:

```text
instance content generation prompt
```

Prompt changes go through normal Git history.

---

# 63. AI Metadata

For diagnostics, record minimal generation metadata.

Template AI generation:

```text
provider
model
promptVersion
timestamp
```

AI-generated instance:

```text
provider
model
promptVersion
timestamp
```

Do not store hidden reasoning.

Do not request chain-of-thought.

---

# 64. AI Evaluations

Create a small set of representative test prompts.

Examples:

```text
Create a reusable multiplication template.

Create a reusable reading template with configurable theme.

Create an English vocabulary template.

Create a science quiz where I choose the topic and difficulty.

Create a spelling task.
```

Evaluate:

```text
valid schema
correct generation type
reasonable parameters
instruction following
age appropriateness
```

Run when changing:

```text
model
provider
prompt
schema
```

Do not run paid AI evals on every normal CI build.

---

# 65. AI Privacy

Send only what the AI needs.

Good:

```text
Create a reading story suitable
for an 8-year-old.

Theme: Space
Difficulty: Medium
```

Unnecessary:

```text
child full name
address
school
phone
location
```

Child identity stays inside the application.

---

# 66. AI Failure

AI is an optional dependency.

If it is unavailable:

```text
existing templates still work
deterministic tasks still work
existing instances still work
child can still complete assigned tasks
reports still work
```

Only operations requiring new AI content fail temporarily.

This is important.

---

# 67. Error Handling

Use ASP.NET Core `ProblemDetails`.

AI generation errors should produce parent-friendly UI:

```text
We couldn't generate this task.

Your settings were kept.

[ Try Again ]
```

Do not expose provider errors directly.

---

# 68. Logging

Log:

```text
TraceId
user
operation
duration
result
```

For AI:

```text
provider
model
latency
success/failure
token usage when available
```

Do not log:

```text
passwords
cookies
API keys
full child personal data
```

---

# 69. Deployment

Production can be:

```text
one Docker container
        │
        ├── ASP.NET Core
        └── Angular build

persistent volume
        │
        ├── family-learning.db
        └── Data Protection keys
```

No database server required initially.

No orchestration platform required.

---

# 70. Backups

SQLite data must be backed up.

Use an SQLite-consistent backup mechanism.

Basic target:

```text
daily backup
+
several previous copies
```

For this private application, keep operations simple.

---

# 71. Testing

## Unit tests

Test:

```text
parameter validation
math generation
scoring
template versioning
task generation selection
```

## Integration tests

Test against real SQLite using the ASP.NET application:

```text
create template
create instance
assign
start session
submit answer
complete
report
```

## AI tests

Keep AI evaluations separate.

## End-to-end

Test the parent → child journey in the browser.

---

# 72. Implementation Order

## Phase 1 — Foundation

Build:

```text
Angular
ASP.NET Core
SQLite
authentication
child profile
child-device login
```

Result:

```text
Parent dashboard works.
Child empty home works.
```

---

## Phase 2 — Core Task Engine

Build:

```text
TaskTemplate
TaskTemplateVersion
InstanceParameterDefinition
TaskInstance
TaskSession

DynamicParameterForm
TaskPlayer

numeric
text
single-choice
```

No AI yet.

---

## Phase 3 — Deterministic Generation

Build:

```text
MathTaskGenerator
```

Create a multiplication template manually.

Example:

```text
Operation fixed = multiplication

Parent chooses:
difficulty
questions
```

Complete the full flow:

```text
Template
 ↓
Parameters
 ↓
Instance
 ↓
Child
 ↓
Result
```

This proves the architecture.

---

## Phase 4 — AI Template Authoring

Add:

```text
IChatClient
AI provider configuration
structured output
template authoring prompt
validation
template preview
```

Now parents can create templates by prompting.

---

## Phase 5 — AI Instance Generation

Add generic:

```text
AiTaskGenerator
```

Create the first reading template.

Flow:

```text
Theme
Difficulty
Length
Questions

↓

AI-generated story/questions

↓

Preview

↓

Assign
```

---

## Phase 6 — Reporting

Add:

```text
daily summary
session details
wrong answers
attempts
time
```

---

## Phase 7 — Production Hardening

Add:

```text
Docker
HTTPS
backups
rate limiting
health checks
structured logging
AI evals
CI/CD
```

Offline support remains optional afterward.

---

# 73. Main Acceptance Scenario

The system is successful when this works:

```text
1. Application starts with zero templates.

2. Parent chooses Create Template.

3. Parent writes:

   "Create a reusable English reading task
    for an 8-year-old. Every time I use it,
    let me choose theme, difficulty, story
    length and number of questions."

4. AI creates a template draft.

5. Parent previews and saves it.

6. Template appears as:

   Reading Practice

7. Parent presses Create Task.

8. Application dynamically shows:

   Theme
   Difficulty
   Story Length
   Question Count

9. Parent chooses:

   Theme = Dinosaurs
   Difficulty = Easy
   Length = Short
   Questions = 5

10. Backend calls AI.

11. AI returns structured story/questions.

12. Backend validates them.

13. TaskInstance is saved as Draft.

14. Parent previews it.

15. Parent assigns it.

16. Child sees the task.

17. Child completes it.

18. Backend calculates score.

19. Parent sees the result.

20. Several days later parent opens
    Reading Practice again.

21. Parent chooses:

    Theme = Space
    Difficulty = Medium
    Length = Medium
    Questions = 8

22. A completely new task is generated
    from the same template.
```

That is the primary product behavior.

---

# 74. Second Acceptance Scenario

Deterministic generation must also work.

```text
1. Parent has template:

   Multiplication Practice

2. Parent presses Create Task.

3. Dynamic form shows:

   Difficulty
   Number of questions

4. Parent chooses:

   Difficulty = Hard
   Questions = 20

5. ASP.NET calls MathTaskGenerator.

6. No LLM is contacted.

7. 20 questions are generated.

8. TaskInstance is frozen.

9. Child completes task.

10. Parent receives report.
```

The parent experience is nearly identical regardless of generation strategy.

That consistency is important.

---

# 75. Architecture Rule Summary

The application should preserve these rules.

**Templates are reusable blueprints.**

**Templates can expose dynamic instance-time parameters.**

**The template decides what parents can configure.**

**Angular renders parameter forms dynamically.**

**Every concrete task becomes a frozen TaskInstance.**

**Task instances may be static, deterministic, or AI-generated.**

**AI generates data, never application code.**

**The child renderer understands generic interaction types instead of school subjects.**

**Published template versions are immutable.**

**Scoring is deterministic application code.**

**Provider/model selection is infrastructure configuration.**

**AI failure must not break previously generated tasks.**

**One backend, one database, one frontend.**

**Do not add infrastructure until a real requirement appears.**

---

# 76. Final Target Architecture

```text
                         PARENT
                           │
                           ▼
                    Angular / Ionic
                           │
                  Create Template
                           │
                           ▼
                  ASP.NET Core 10
                           │
                    AI Authoring
                           │
                           ▼
                  TaskTemplateVersion
                           │
                ┌──────────┴──────────┐
                │                     │
         fixed settings       parameter schema
                                      │
                                      ▼
                            Dynamic Parent Form
                                      │
                                      ▼
                             Parameter Values
                                      │
                                      ▼
                              Generation Mode
                                      │
            ┌─────────────────────────┼─────────────────────────┐
            │                         │                         │
            ▼                         ▼                         ▼
          Static               Deterministic                   AI
            │                         │                         │
            │                  C# Generator                IChatClient
            │                         │                         │
            └─────────────────────────┴─────────────────────────┘
                                      │
                                      ▼
                              TaskInstance
                               exact/frozen
                                      │
                                  Parent Preview
                                      │
                                    Assign
                                      │
                                      ▼
                                    CHILD
                                      │
                               Angular TaskPlayer
                                      │
                                      ▼
                                  TaskSession
                                      │
                                      ▼
                                  C# Scoring
                                      │
                                      ▼
                               Parent Reporting
```

This is the architecture to implement.

The important idea is not merely:

> AI creates educational tasks.

It is:

> **Parents create reusable, parameterized educational blueprints. Each blueprint can generate unlimited concrete tasks using the simplest appropriate strategy: static content, deterministic C# logic, or AI.**

That gives the application maximum useful flexibility while keeping the engineering model small, understandable, testable, and maintainable.