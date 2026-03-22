export type AgentRole = 'Manager' | 'Analyst' | 'WebDev' | 'Frontend' | 'Backend' | 'Tester' | 'PM' | 'Support'
export type AgentRunStatus = 'Idle' | 'Thinking' | 'Delegating' | 'Coding' | 'Reviewing' | 'Waiting' | 'Completed' | 'Error'
export type MissionStatus = 'Draft' | 'Queued' | 'Running' | 'AwaitingPatchApproval' | 'Completed' | 'Failed' | 'Cancelled'
export type MissionStepStatus = 'Pending' | 'InProgress' | 'Completed' | 'Blocked'
export type PatchProposalStatus = 'PendingReview' | 'Approved' | 'Rejected' | 'Applied' | 'Failed'
export type AgentExecutionMode = 'StructuredPrompt' | 'ToolLoop'
export type AgentToolType = 'ListFiles' | 'ReadFile' | 'WriteFile' | 'SearchCode' | 'RunTerminal' | 'GitStatus' | 'GitDiff' | 'GitCommit' | 'GitPush' | 'CustomCommand'
export type SwarmTemplate = 'Sequential' | 'Hierarchical' | 'ParallelReview'
export type AppRoute = 'overview' | 'tasks' | 'swarms' | 'agents' | 'permissions' | 'tools' | 'chats'

export const agentRoles: AgentRole[] = ['Manager', 'Analyst', 'WebDev', 'Frontend', 'Backend', 'Tester', 'PM', 'Support']

export interface RepositoryRef {
  owner: string
  name: string
  fullName: string
  defaultBranch: string
}

export interface SprintRef {
  id: string
  title: string
  number: number
  state: string
  dueOn?: string | null
  iterationId?: string | null
  projectId?: string | null
  projectNumber?: number | null
  projectTitle?: string | null
  startDate?: string | null
  durationDays?: number | null
}

export interface GitHubProjectRef {
  id: string
  number: number
  title: string
  url: string
  ownerLogin: string
  ownerType: string
  shortDescription?: string | null
  closed: boolean
}

export interface GitHubBoardItemRef {
  id: string
  projectId: string
  projectNumber: number
  projectTitle: string
  projectUrl: string
  sprintId: string
  iterationId?: string | null
  sprintTitle: string
  status: string
  statusOptionId?: string | null
  contentType: string
  number?: number | null
  title: string
  description: string
  state: string
  url?: string | null
  repositoryOwner: string
  repositoryName: string
  repositoryFullName: string
  labels: string[]
  assignees: string[]
  subtasks: string[]
  updatedAt: string
  isDraft: boolean
}

export interface GitHubBoardColumn {
  id: string
  title: string
  items: GitHubBoardItemRef[]
}

export interface GitHubBoardSnapshot {
  repository: RepositoryRef
  source: string
  statusMessage: string
  projects: GitHubProjectRef[]
  sprints: SprintRef[]
  columns: GitHubBoardColumn[]
  items: GitHubBoardItemRef[]
}

export interface MissionStep {
  id: string
  title: string
  owner: AgentRole
  status: MissionStepStatus
  order: number
  summary: string
  dependencies: string[]
}

export interface PatchProposal {
  id: string
  missionId: string
  authorRole: AgentRole
  title: string
  summary: string
  status: PatchProposalStatus
  targetPaths: string[]
  diff: string
  alreadyApplied: boolean
  reviewNote?: string | null
  createdAt: string
  updatedAt?: string | null
}

export interface ActivityEvent {
  id: string
  missionId: string
  createdAt: string
  eventType: string
  agentRole?: AgentRole | null
  summary: string
  details: string
}

export interface ProgressLog {
  id: string
  missionId: string
  role?: AgentRole | null
  stage: string
  message: string
  metadata: Record<string, string>
  createdAt: string
}

export interface ExternalTaskRef {
  provider: string
  externalId: string
  title: string
  url?: string | null
  status: string
}

export interface PullRequestRef {
  provider: string
  externalId: string
  title: string
  url?: string | null
  status: string
  headBranch: string
  baseBranch: string
}

export interface AgentSnapshot {
  role: AgentRole
  status: AgentRunStatus
  label: string
  detail?: string | null
  updatedAt: string
  queueDepth: number
}

export interface AgentToolDefinition {
  name: string
  displayName: string
  description: string
  type: AgentToolType
  enabled: boolean
  destructive: boolean
  commandTemplate?: string | null
}

export interface AgentRolePolicy {
  role: AgentRole
  executionMode: AgentExecutionMode
  allowedTools: string[]
  allowedDelegates: AgentRole[]
  writableRoots: string[]
  maxSteps: number
}

export interface AgentRuntimeCatalog {
  updatedAt: string
  tools: AgentToolDefinition[]
  policies: AgentRolePolicy[]
}

export interface Mission {
  id: string
  title: string
  prompt: string
  objective: string
  swarmTemplate: SwarmTemplate
  status: MissionStatus
  createdAt: string
  updatedAt: string
  currentPhase?: string | null
  isArchived: boolean
  archivedAt?: string | null
  cancelledAt?: string | null
  cancelledReason?: string | null
  selectedRepository?: RepositoryRef | null
  selectedSprint?: SprintRef | null
  selectedWorkItem?: GitHubBoardItemRef | null
  externalTask?: ExternalTaskRef | null
  pullRequest?: PullRequestRef | null
  autoCreatePullRequest: boolean
  workspaceRootPath?: string | null
  steps: MissionStep[]
  patchProposals: PatchProposal[]
  agents: AgentSnapshot[]
  artifacts: Record<string, string>
}

export interface DashboardSnapshot {
  activeMission?: Mission | null
  agents: AgentSnapshot[]
  recentActivities: ActivityEvent[]
  recentProgressLogs: ProgressLog[]
  pendingPatchProposals: PatchProposal[]
  logicalQueueDepth: number
  chatModel: string
  physicalWorkerCount: number
}

export interface OverviewSystemSnapshot {
  logicalQueueDepth: number
  chatModel: string
  physicalWorkerCount: number
}

export interface OverviewSnapshot {
  activeRun?: Mission | null
  recentRuns: Mission[]
  system: OverviewSystemSnapshot
  agents: AgentSnapshot[]
}

export interface OllamaModelInfo {
  name: string
  family: string
  parameterSize: string
  modifiedAt?: string | null
  size: number
}

export interface ChatThread {
  id: string
  title: string
  model: string
  createdAt: string
  updatedAt: string
}

export interface ChatUsage {
  promptEvalCount?: number | null
  evalCount?: number | null
}

export interface ChatMessage {
  id: string
  threadId: string
  role: 'user' | 'assistant' | 'system'
  content: string
  model: string
  createdAt: string
  usage?: ChatUsage | null
}

export interface ChatExchangeResult {
  thread: ChatThread
  userMessage: ChatMessage
  assistantMessage: ChatMessage
}

export interface CreateRunRequest {
  title: string
  objective: string
  selectedRepository?: RepositoryRef | null
  selectedSprint?: SprintRef | null
  selectedWorkItem?: GitHubBoardItemRef | null
  swarmTemplate: SwarmTemplate
  autoCreatePullRequest?: boolean
}

export interface DashboardMetric {
  label: string
  value: string
  helper: string
  tone: 'cyan' | 'lime' | 'violet' | 'rose'
}

export interface AgentPodGroup {
  id: string
  label: string
  description: string
  roles: AgentRole[]
  count: number
  tone: 'cyan' | 'lime' | 'violet' | 'rose'
}

export interface WorkflowNode {
  id: string
  x: number
  y: number
  width: number
  owner: AgentRole
  title: string
  summary: string
  status: MissionStepStatus
  tone: 'cyan' | 'lime' | 'violet' | 'rose'
}

export interface WorkflowEdge {
  id: string
  from: string
  to: string
  active: boolean
  label: string
}

export interface ExecutionFeedRow {
  id: string
  line: number
  createdAt: string
  tag: string
  tone: 'cyan' | 'lime' | 'violet' | 'rose' | 'neutral'
  content: string
}
