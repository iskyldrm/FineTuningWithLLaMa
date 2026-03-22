# APEX Operator Console

## 1. Purpose

This repository implements a local-first operator console for running a small multi-agent software team against GitHub repositories, GitHub Project boards, and a local knowledge base.

The system is designed to let an operator:

1. select a repository,
2. optionally load GitHub Project / sprint / board data,
3. dispatch either a direct objective or a selected board item into a swarm run,
4. let agents analyze, edit, validate, and summarize work,
5. review produced patches,
6. optionally publish a branch and open a GitHub pull request.

This README is intentionally written as a technical handoff document for another AI or engineer. It describes the current implementation, not an aspirational design.

## 2. Terminology

The codebase uses both `mission` and `run`.

- `Mission` is the core persisted domain model.
- `Run` is the preferred operator-facing term used by the current UI and `/api/runs` endpoints.
- `POST /api/missions` still exists as a compatibility wrapper and internally maps to `CreateRunAsync`.

Practical rule:

- If you are changing backend orchestration, think in terms of the `Mission` aggregate.
- If you are changing frontend operator flows, think in terms of `Run`.

## 3. Current Product Surface

The current frontend is an operator console with these routes:

- `/overview`: active run summary, recent runs, system status
- `/tasks`: direct task entry and optional GitHub board import
- `/swarms`: live run detail, steps, progress, patch review
- `/agents`: agent registry and capabilities
- `/permissions`: runtime role policy editor
- `/tools`: runtime tool registry editor
- `/chats`: freeform model chat outside run execution

Legacy routes still redirect:

- `/dashboard` -> `/overview`
- `/workflows` -> `/swarms`
- `/execution` -> `/swarms`
- `/home` -> `/overview`
- `/monitoring` -> `/overview`

## 4. High-Level Architecture

### 4.1 Components

| Layer | Technology | Responsibility |
| --- | --- | --- |
| Frontend | React + TypeScript + Vite | Operator UI, run creation, board selection, patch review, runtime policy editing |
| API | ASP.NET Core .NET 10 | Orchestration, GitHub integration, SignalR streaming, runtime policy enforcement |
| Mission persistence | PostgreSQL | Stores persisted `Mission` aggregates and activities |
| Progress + chat persistence | MongoDB | Stores progress logs and chat threads/messages |
| Knowledge store | Qdrant | Stores and retrieves knowledge chunks from markdown notes |
| Model gateway | Ollama | Local chat and embedding provider |
| GitHub integration | REST + GraphQL | Repository list, Project v2 board loading, milestone fallback, issue creation, PR creation |
| Workspace execution | local git workspaces | Clone/sync repository workspaces, edit files, run validation, publish branches |

### 4.2 Runtime shape

At runtime, the API does three major jobs:

1. it accepts operator requests and persists run state,
2. it processes queued runs in a background orchestrator,
3. it streams activities and progress to the UI via SignalR.

### 4.3 Main service registrations

The service graph is assembled in `src/Apex.AgentTeam.Api/Program.cs`.

Important registrations:

- `MissionOrchestrator`: run queue and execution engine
- `PostgresMissionRepository`: mission persistence
- `MongoTelemetryStore`: progress logs and chats
- `QdrantMemoryStore`: semantic retrieval
- `GitWorkspaceToolset`: workspace snapshot, file IO, diff, validation, branch publishing
- `GitHubCatalogService`: repo list, board snapshots, PR creation
- `GitHubIssueSink`: external issue creation fallback
- `AdaptiveAgentExecutor`: role-level execution mode switch between structured prompting and tool loop

## 5. Domain Model

Core contracts live in `src/Apex.AgentTeam.Api/Models/Contracts.cs`.

### 5.1 Mission

`Mission` is the main aggregate. It contains:

- identity: `Id`, `Title`
- operator intent: `Prompt`, `Objective`
- topology: `SwarmTemplate`
- execution state: `Status`, `CurrentPhase`
- lifecycle metadata: `CreatedAt`, `UpdatedAt`, `IsArchived`, `ArchivedAt`, `CancelledAt`, `CancelledReason`
- GitHub context: `SelectedRepository`, `SelectedSprint`, `SelectedWorkItem`
- outputs: `ExternalTask`, `PullRequest`, `Artifacts`
- execution payloads: `Steps`, `PatchProposals`, `Agents`
- workspace context: `WorkspaceRootPath`

### 5.2 Related models

- `RepositoryRef`: selected GitHub repository metadata
- `SprintRef`: selected sprint or milestone context
- `GitHubBoardItemRef`: selected issue / draft issue / PR card from a board
- `PatchProposal`: pending/applied/rejected diff proposal
- `AgentSnapshot`: current state of a role inside the run
- `ActivityEvent`: timeline event
- `ProgressLog`: machine-readable progress stream row

### 5.3 Mission status model

`MissionStatus`:

- `Draft`
- `Queued`
- `Running`
- `AwaitingPatchApproval`
- `Completed`
- `Failed`
- `Cancelled`

`PatchProposalStatus`:

- `PendingReview`
- `Approved`
- `Rejected`
- `Applied`
- `Failed`

## 6. Agent System

### 6.1 Roles

The system has eight fixed roles:

| Role | Purpose |
| --- | --- |
| `Manager` | queue control, orchestration, operator-facing run state |
| `Analyst` | scope extraction, acceptance criteria, risk framing |
| `WebDev` | architecture, contract, implementation sequencing |
| `Frontend` | UI-side implementation work |
| `Backend` | API / persistence / orchestration implementation work |
| `Tester` | validation and review |
| `PM` | milestone and status summary |
| `Support` | user-facing handoff / explanation |

Important behavioral note:

- The run always enters through `Manager`.
- `PM` is not the first queue owner in the current implementation.
- If you need a true PM-first queue semantics, that is a future orchestration change, not current behavior.

### 6.2 Execution modes

Roles do not all execute the same way. The runtime catalog defines a per-role `ExecutionMode`.

Supported modes:

- `StructuredPrompt`
- `ToolLoop`

Default runtime catalog behavior:

| Role | Default mode | Notes |
| --- | --- | --- |
| Manager | StructuredPrompt | orchestrates, does not edit files |
| Analyst | StructuredPrompt | derives acceptance criteria and optional external task |
| WebDev | StructuredPrompt | plans implementation |
| Frontend | ToolLoop | can inspect and modify workspace within allowed roots |
| Backend | ToolLoop | can inspect and modify workspace within allowed roots |
| Tester | StructuredPrompt | can inspect, search, run terminal |
| PM | StructuredPrompt | summarizes sprint/run state |
| Support | StructuredPrompt | turns engineering state into operator/user summary |

### 6.3 Tool permissions

Runtime tool and policy configuration is stored in the JSON runtime catalog and exposed via:

- `GET /api/agent-runtime`
- `POST /api/agent-runtime/tools`
- `PUT /api/agent-runtime/policies/{role}`

Default registered tools:

- `list_files`
- `read_file`
- `write_file`
- `search_code`
- `run_terminal`
- `git_status`
- `git_diff`
- `git_commit`
- `git_push`

Writable roots are enforced per role. By default:

- `Frontend` writable roots: `frontend`, `src`
- `Backend` writable roots: `src`, `tests`
- read-only roles cannot use `write_file`

## 7. Swarm Templates

The run topology is selected by `SwarmTemplate`.

Supported templates:

| Template | Intent |
| --- | --- |
| `Sequential` | strict linear handoff |
| `Hierarchical` | manager-led specialist delegation |
| `ParallelReview` | frontend/backend converge into review |

Current role order / delegation:

### 7.1 Sequential

`Manager -> Analyst -> WebDev -> Frontend -> Backend -> Tester -> PM -> Support`

Delegation chain:

- Analyst delegated by Manager
- WebDev delegated by Analyst
- Frontend delegated by WebDev
- Backend delegated by Frontend
- Tester delegated by Backend
- PM delegated by Tester
- Support delegated by PM

### 7.2 Hierarchical

`Manager` directly delegates to Analyst, WebDev, Tester, PM, and Support. `WebDev` delegates to Frontend and Backend.

### 7.3 ParallelReview

Similar to Hierarchical, but review emphasis is changed by dependency graph:

- Analyst depends on Manager
- WebDev depends on Manager
- Frontend depends on WebDev
- Backend depends on WebDev
- Tester depends on Manager
- PM depends on Tester
- Support depends on PM

## 8. Task Intake and Selection Flow

This is the most important part if another AI needs to understand how operator task selection currently works.

### 8.1 Two task entry paths

The current UI supports two task entry modes:

1. direct task entry
2. optional GitHub board import

The primary operator path in the current UI is still direct entry on `/tasks`. Board import is supported but optional.

### 8.2 Direct task entry

On `/tasks`, the operator can directly provide:

- repository
- swarm template
- sprint
- title
- objective

This becomes a `CreateRunRequest` sent to `POST /api/runs`.

### 8.3 Board-based task entry

When a repository is selected:

1. the frontend calls `GET /api/github/repositories/{owner}/{repo}/board`
2. the backend returns a `GitHubBoardSnapshot`
3. the frontend stores:
   - `board`
   - `sprints`
   - `selectedSprintId`
   - `selectedWorkItemId`
4. if a sprint is selected, the frontend filters `board.items` by `sprintId`
5. clicking `Dispatch` on a board card builds an objective from:
   - card title
   - repository full name
   - sprint title
   - status lane
   - description
   - extracted checklist items
6. that generated objective is submitted as a `CreateRunRequest`

The card itself is also attached as `SelectedWorkItem`, so the run keeps explicit task context separate from the plain text objective.

### 8.4 Important implementation detail

Dispatching a board item does **not** create a separate PM-owned queue item.

What actually happens:

- the selected board item is embedded into the run context,
- the run is queued,
- `Manager` starts orchestration,
- downstream roles receive repository/sprint/work item context in their prompts.

## 9. GitHub Board Resolution

Board loading is implemented in `GitHubCatalogService`.

### 9.1 Repository list

`GET /api/github/repositories` uses GitHub REST:

- `/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member`

If no token exists:

- it returns a single fallback repository only if `GitHub__RepositoryOwner` and `GitHub__RepositoryName` are configured,
- otherwise it returns an empty list.

### 9.2 Project v2 resolution

Preferred board source is GitHub Projects v2 via GraphQL.

Resolution steps:

1. load viewer projects and organization projects
2. for each candidate project, page through up to 100 items at a time
3. read:
   - `Status`
   - `Iteration`
   - issue / draft issue / PR content
4. filter items by `repository.nameWithOwner`
5. convert items into `GitHubBoardItemRef`
6. build synthetic sprint catalog from `Iteration` values
7. group columns by `Status`

Project board output fields:

- `Repository`
- `Source = "project-v2"`
- `Projects`
- `Sprints`
- `Items`
- `Columns`

### 9.3 Milestone fallback

If Project v2 resolution fails or yields no usable items, the backend falls back to milestones:

1. list milestones from REST
2. load issues per milestone
3. convert issues into `GitHubBoardItemRef`
4. build board columns from issue state

Fallback output:

- `Source = "milestones"`

### 9.4 Checklist extraction

Checklist items are extracted from board issue bodies by scanning markdown lines starting with:

- `- [ ] `
- `* [ ] `

These are placed into `GitHubBoardItemRef.Subtasks` and later injected into task objectives.

## 10. Run Lifecycle

### 10.1 API entry

Preferred run endpoint:

- `POST /api/runs`

Compatibility endpoint:

- `POST /api/missions`

`CreateMissionAsync` currently maps to `CreateRunAsync` with:

- `Objective = request.Prompt`
- `SwarmTemplate = Hierarchical`

### 10.2 Queueing

When a run is created:

1. a `Mission` aggregate is initialized
2. `Status = Queued`
3. `Manager` status becomes `Delegating`
4. logical queue depth is incremented
5. run is persisted
6. a queue item is written into `MissionQueue`

### 10.3 Background processing

`MissionOrchestrator` is a hosted background service. It continuously:

1. reads queued mission IDs,
2. reloads the mission,
3. validates runtime catalog capability constraints,
4. captures a workspace snapshot,
5. performs semantic knowledge search,
6. executes the chosen swarm plan role by role,
7. persists activities, progress logs, artifacts, and patch proposals.

### 10.4 Knowledge search

The search query is constructed from:

- objective
- selected repository full name
- selected sprint title
- selected work item title
- selected work item description

The result is a list of `KnowledgeChunk` hits from Qdrant.

### 10.5 External issue generation

If the operator creates a direct task with no selected board item:

- `Analyst` may emit an `ExternalTaskDraft`
- the backend sends it to `GitHubIssueSink`
- this creates a GitHub issue only if repository + token configuration exists

If a board item already exists, that external task path is skipped.

## 11. Structured vs ToolLoop Execution

### 11.1 StructuredPrompt roles

Structured roles generate text summaries and artifacts.

Behavior:

- prompts are assembled from repository, sprint, selected task, workspace preview, and knowledge hits
- the model returns plain text
- artifacts are stored in `Mission.Artifacts`
- `Analyst` extracts acceptance criteria
- `Frontend` and `Backend`, when running in structured fallback mode, generate synthetic markdown-file patch proposals rather than real code edits

### 11.2 ToolLoop roles

`Frontend` and `Backend` default to `ToolLoop`.

Tool loop behavior:

1. model receives a JSON-only system prompt with allowed tools
2. model chooses either:
   - a tool action
   - a finish action
3. server validates the requested tool against role policy
4. tool is executed through `IWorkspaceToolset`
5. the result is sent back to the model as an observation
6. this continues until:
   - the model returns `finish`, or
   - the role hits `MaxSteps`

If tool loop fails badly, `AdaptiveAgentExecutor` falls back to structured prompting for that role.

### 11.3 Real workspace mutation behavior

This is an important implementation detail.

When `Frontend` or `Backend` runs in `ToolLoop` mode:

- the agent can directly modify files in the workspace via `write_file`
- the backend then reads the actual git diff from the working tree
- the resulting diff is stored as a `PatchProposal`
- that proposal is marked `AlreadyApplied = true`

This means:

- the workspace is already changed before operator review,
- approving such a patch does not re-apply the diff,
- approval validates and finalizes the already-applied workspace state,
- rejection attempts to revert that already-applied diff.

This is different from structured fallback mode, where the diff may only represent a synthetic markdown proposal file.

## 12. Workspace Model

Workspace operations are implemented by `GitWorkspaceToolset`.

### 12.1 Workspace selection

If `SelectedRepository` is null:

- the primary workspace root is used (`Workspace__RootPath`)

If `SelectedRepository` is present:

- the repository workspace root is:
  - `Workspace__RepositoriesRootPath/<owner>/<repo>`
- the backend ensures the repository exists locally
- if missing, it clones the repository
- after that, it tries to:
  - `git fetch --all --prune`
  - `git checkout <default branch>`
  - `git pull --ff-only origin <default branch>`

### 12.2 Snapshot

`CaptureSnapshotAsync` collects:

- up to 160 file paths
- up to 24 file previews
- previewable extensions include:
  - `.cs`
  - `.md`
  - `.json`
  - `.ts`
  - `.tsx`
  - `.css`
  - `.csproj`
  - `.sln`

Ignored segments:

- `.git`
- `node_modules`
- `bin`
- `obj`
- `.nuget`
- `.dotnet-home`

### 12.3 Validation

Validation command depends on workspace type:

- primary workspace: `Workspace__ValidationCommand`
- selected repository workspace: `Workspace__RepositoryValidationCommand`

### 12.4 Branch publishing

When PR creation is allowed and there is at least one applied patch:

1. backend creates/switches to branch `apex/<slug>-<shortId>`
2. stages all changes
3. commits with message `Apex AI: <title>`
4. pushes to `origin`
5. requests PR creation through GitHub

## 13. Patch Review Pipeline

Patch review is mandatory in the current implementation. There is no fully automatic merge-to-PR flow without patch resolution.

### 13.1 How proposals are created

Patch proposals come from:

- `Frontend`
- `Backend`

The system does not currently accept arbitrary patch authors.

### 13.2 Approval flow

When the operator approves a patch:

1. proposal state becomes `Approved`
2. unified diff policy is checked
3. if `AlreadyApplied = false`, git apply is executed
4. if `AlreadyApplied = true`, apply is skipped
5. validation command is executed
6. if validation fails:
   - revert is attempted
   - proposal becomes `Failed`
7. if validation passes:
   - proposal becomes `Applied`
   - mission may complete
8. if mission completes and PR conditions are met:
   - PR creation is attempted

### 13.3 Rejection flow

When the operator rejects a patch:

- if the diff had already been applied in workspace, revert is attempted
- proposal becomes `Rejected` unless revert fails
- mission closes when no pending patch proposals remain

### 13.4 Patch safety rules

Patch policy rejects diffs that touch protected or invalid areas such as:

- `.git`
- `.env`
- `node_modules`
- `bin`
- `obj`
- `project-workspaces`
- `workspace-data`
- gitlinks / submodules
- parent-directory traversal
- diff metadata with no actual hunks

## 14. Pull Request Creation

PR creation is implemented in `GitHubCatalogService.CreatePullRequestAsync`.

PR creation only runs if:

- `AutoCreatePullRequest = true`
- `SelectedRepository` exists
- no `PullRequest` is already stored on the mission
- at least one patch has status `Applied`
- git branch publication succeeded
- GitHub token is configured

PR body includes:

- mission title
- branch/base names
- sprint title if present
- source board item title
- `Closes #<issue>` for selected issue cards
- first few agent artifacts

If PR creation fails, the mission can still complete; failure is stored as PR status metadata rather than crashing the run.

## 15. Operator-Facing Pages

### 15.1 Overview

Shows:

- active run
- recent runs
- current system queue and chat model
- current agent snapshots

### 15.2 Tasks

This is the primary task intake page.

It supports:

- selecting repository
- selecting swarm template
- selecting sprint
- creating a direct run with title/objective
- optionally importing board items from GitHub and dispatching one directly

### 15.3 Swarms

Shows execution detail for the current run:

- steps
- progress
- activities
- patch proposals
- archive / cancel actions

### 15.4 Agents

Shows:

- static role registry
- current run agent state
- role capabilities

### 15.5 Permissions

Edits runtime role policy:

- execution mode
- allowed tools
- allowed delegates
- writable roots
- max steps

### 15.6 Tools

Edits the runtime tool registry:

- built-in or custom command tools
- display name
- description
- enabled flag
- destructive flag
- command template

### 15.7 Chats

Independent assistant chat threads. This is not part of swarm run execution.

## 16. Persistence Model

### 16.1 PostgreSQL

Used for:

- mission aggregates
- run/activity persistence

### 16.2 MongoDB

Used for:

- progress logs
- chat threads
- chat messages

### 16.3 Qdrant

Used for:

- embedded knowledge chunks
- semantic retrieval during run execution

## 17. API Surface

### 17.1 Health and overview

- `GET /api/health`
- `GET /api/overview`
- `GET /api/dashboard`

### 17.2 GitHub catalog

- `GET /api/github/repositories`
- `GET /api/github/repositories/{owner}/{repo}/milestones`
- `POST /api/github/repositories/{owner}/{repo}/milestones/defaults`
- `GET /api/github/repositories/{owner}/{repo}/board`

### 17.3 Runs

- `GET /api/runs`
- `GET /api/runs/{runId}`
- `GET /api/runs/{runId}/activities`
- `GET /api/runs/{runId}/progress`
- `POST /api/runs`
- `POST /api/runs/{runId}/archive`
- `POST /api/runs/{runId}/cancel`

### 17.4 Missions compatibility

- `GET /api/missions/{missionId}`
- `GET /api/missions/{missionId}/activities`
- `GET /api/missions/{missionId}/progress`
- `POST /api/missions`

### 17.5 Patch review

- `POST /api/patches/{proposalId}/approve`
- `POST /api/patches/{proposalId}/reject`

### 17.6 Runtime configuration

- `GET /api/agent-runtime`
- `POST /api/agent-runtime/tools`
- `PUT /api/agent-runtime/policies/{role}`

### 17.7 Models and chat

- `GET /api/ollama/models`
- `GET /api/chat/threads`
- `POST /api/chat/threads`
- `GET /api/chat/threads/{threadId}/messages`
- `POST /api/chat/threads/{threadId}/messages`

### 17.8 Realtime

- `SignalR /hubs/activity`

Both activity events and progress rows are streamed over the same realtime hub.

## 18. Configuration

### 18.1 Docker compose defaults

Default local ports:

- frontend: `http://localhost:8080`
- api: `http://localhost:5000`
- postgres: `localhost:5432`
- mongo: `localhost:27017`
- qdrant: `http://localhost:6333`

### 18.2 Important environment/config keys

| Key | Purpose |
| --- | --- |
| `Model__BaseUrl` | Ollama base URL |
| `Model__ChatModel` | primary chat model |
| `Model__EmbeddingModel` | embedding model |
| `GitHub__AccessToken` | required for repo listing, Project v2 board load, issue creation, PR creation |
| `GitHub__RepositoryOwner` | optional fallback owner |
| `GitHub__RepositoryName` | optional fallback repo |
| `Workspace__RootPath` | primary workspace root |
| `Workspace__RepositoriesRootPath` | cloned repository workspace root |
| `Workspace__ValidationCommand` | validation for primary workspace |
| `Workspace__RepositoryValidationCommand` | validation for cloned repo workspaces |
| `Storage__PostgresConnectionString` | mission persistence |
| `Mongo__ConnectionString` | progress/chat persistence |
| `Storage__QdrantBaseUrl` | vector store |

### 18.3 Repository workspace storage

In Docker compose, repository workspaces are persisted in a dedicated named volume:

- `repository-workspaces:/workspace-data/repositories`

This means cloned target repositories survive container restarts unless that volume is explicitly removed.

## 19. Local Startup

### 19.1 Prerequisites

- Docker Desktop
- Ollama running on the host
- required Ollama models pulled locally
- optional GitHub token for full GitHub integration

### 19.2 Suggested Ollama models

- `qwen2.5-coder:14b`
- `nomic-embed-text`

### 19.3 Start the stack

```bash
docker compose up -d --build
```

Open:

- frontend: `http://localhost:8080`
- api: `http://localhost:5000/api/overview`

## 20. What Another AI Should Assume

If another AI is asked to extend this project, these assumptions are currently safe:

1. the frontend route for task dispatch is `/tasks`
2. repository selection happens before board loading
3. board loading is optional, not mandatory
4. `SelectedWorkItem` is the main structured task context object
5. `Manager` owns queue ingress; `PM` is later-stage summarization, not ingress
6. `Frontend` and `Backend` are the only roles expected to produce patch proposals
7. runtime execution mode is policy-driven and editable at runtime
8. the system already supports real workspace edits through ToolLoop for `Frontend` and `Backend`
9. patch review is still an explicit operator gate
10. PR creation is post-validation and post-patch-resolution, not pre-review

## 21. Known Limitations

These are implementation realities, not bugs in this README.

### 21.1 Patch review remains manual

The system does not yet implement a zero-touch "dispatch task and always auto-merge to PR" pipeline. Patch proposals still require resolution.

### 21.2 PM is not queue ingress

If the product requirement is "selected board task should go directly to Product Manager first", orchestration must be changed. Current flow still begins with `Manager`.

### 21.3 Board semantics depend on GitHub conventions

Project v2 parsing assumes field names:

- `Status`
- `Iteration`

If a project uses different naming, board extraction will miss those semantics.

### 21.4 Structured fallback is weaker than ToolLoop

If ToolLoop falls back to `StructuredPrompt`, frontend/backend may only emit a markdown diff proposal file instead of a real repository mutation.

### 21.5 GitHub token quality matters

Insufficient token scopes degrade features selectively:

- repo list may fail
- board load may fail
- issue creation may skip
- PR creation may fail

## 22. Recommended Extension Points

If you want to evolve the product, these are the highest-value extension points:

1. change queue ingress semantics if PM-first execution is required
2. add richer board field mapping beyond `Status` and `Iteration`
3. add automatic patch approval modes for trusted repos
4. improve ToolLoop planning with diff-aware and test-aware heuristics
5. add stronger repository-specific validation command routing
6. add richer PR summaries with explicit artifact sections per role
7. separate direct-task runs from board-import runs in the UI

## 23. Minimal Example Payloads

### 23.1 CreateRunRequest

```json
{
  "title": "Implement board-driven dispatch",
  "objective": "Complete the selected board item inside the target repository.",
  "selectedRepository": {
    "owner": "example",
    "name": "repo",
    "fullName": "example/repo",
    "defaultBranch": "main"
  },
  "selectedSprint": {
    "id": "PVT_example:iteration_1",
    "title": "Sprint 1",
    "number": 1,
    "state": "scheduled"
  },
  "selectedWorkItem": {
    "id": "PVTI_example",
    "projectId": "PVT_example",
    "projectNumber": 4,
    "projectTitle": "APEX",
    "projectUrl": "https://github.com/users/example/projects/4",
    "sprintId": "PVT_example:iteration_1",
    "iterationId": "iteration_1",
    "sprintTitle": "Sprint 1",
    "status": "Todo",
    "contentType": "Issue",
    "number": 42,
    "title": "Build dispatch flow",
    "description": "Operator selects repo, sprint, and task.",
    "state": "OPEN",
    "repositoryOwner": "example",
    "repositoryName": "repo",
    "repositoryFullName": "example/repo",
    "labels": ["enhancement"],
    "assignees": [],
    "subtasks": ["Load repositories", "Filter by sprint"]
  },
  "swarmTemplate": "Hierarchical",
  "autoCreatePullRequest": true
}
```

### 23.2 GitHubBoardSnapshot

```json
{
  "repository": {
    "owner": "example",
    "name": "repo",
    "fullName": "example/repo",
    "defaultBranch": "main"
  },
  "source": "project-v2",
  "statusMessage": "1 project and 2 sprints found.",
  "projects": [],
  "sprints": [],
  "columns": [],
  "items": []
}
```

## 24. Bottom Line

This repository is currently a run-based operator console over a mission aggregate. The technical heart of the system is:

- GitHub-backed repository/sprint/task context,
- a queued orchestrator,
- policy-driven agents,
- ToolLoop-based workspace mutation for implementation roles,
- operator-reviewed patch finalization,
- optional PR publication.

If another AI is taking over this codebase, the most important thing to preserve is the distinction between:

- operator-selected structured task context,
- role execution policy,
- workspace mutation,
- patch review,
- post-validation PR creation.
