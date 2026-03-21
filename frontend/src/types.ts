export type AgentRole = 'Manager' | 'Analyst' | 'WebDev' | 'Frontend' | 'Backend' | 'Tester' | 'PM' | 'Support'
export type AgentRunStatus = 'Idle' | 'Thinking' | 'Delegating' | 'Coding' | 'Reviewing' | 'Waiting' | 'Completed' | 'Error'
export type MissionStatus = 'Draft' | 'Queued' | 'Running' | 'AwaitingPatchApproval' | 'Completed' | 'Failed'
export type MissionStepStatus = 'Pending' | 'InProgress' | 'Completed' | 'Blocked'
export type PatchProposalStatus = 'PendingReview' | 'Approved' | 'Rejected' | 'Applied' | 'Failed'

export interface RepositoryRef {
  owner: string
  name: string
  fullName: string
  defaultBranch: string
}

export interface SprintRef {
  id: number
  title: string
  number: number
  state: string
  dueOn?: string | null
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

export interface AgentSnapshot {
  role: AgentRole
  status: AgentRunStatus
  label: string
  detail?: string | null
  updatedAt: string
  queueDepth: number
}

export interface Mission {
  id: string
  title: string
  prompt: string
  status: MissionStatus
  createdAt: string
  updatedAt: string
  currentPhase?: string | null
  selectedRepository?: RepositoryRef | null
  selectedSprint?: SprintRef | null
  externalTask?: ExternalTaskRef | null
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

export interface CreateMissionRequest {
  title: string
  prompt: string
  selectedRepository?: RepositoryRef | null
  selectedSprint?: SprintRef | null
}
