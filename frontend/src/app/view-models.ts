import type {
  AgentRole,
  AgentRunStatus,
  AgentSnapshot,
  Mission,
  OllamaModelInfo,
  OverviewSnapshot,
  RepositoryRef,
  SwarmTemplate,
} from '../types'

export type RoleVisualMeta = {
  title: string
  subtitle: string
  short: string
  icon: string
  capabilities: string[]
}

export const roleMeta: Record<AgentRole, RoleVisualMeta> = {
  Manager: { title: 'Manager', subtitle: 'Swarm coordination', short: 'MGR', icon: 'hub', capabilities: ['Queue control', 'Delegation', 'Operator updates'] },
  Analyst: { title: 'Analyst', subtitle: 'Task scoping', short: 'ANL', icon: 'analytics', capabilities: ['Requirements', 'Acceptance criteria', 'Risk framing'] },
  WebDev: { title: 'WebDev', subtitle: 'System planner', short: 'WEB', icon: 'account_tree', capabilities: ['Architecture', 'Contracts', 'Implementation order'] },
  Frontend: { title: 'Frontend', subtitle: 'UI delivery', short: 'FE', icon: 'web', capabilities: ['React UI', 'Interaction polish', 'Layout systems'] },
  Backend: { title: 'Backend', subtitle: 'API delivery', short: 'BE', icon: 'dns', capabilities: ['APIs', 'Persistence', 'Runtime fixes'] },
  Tester: { title: 'Tester', subtitle: 'Validation', short: 'QA', icon: 'fact_check', capabilities: ['Diff review', 'Regression checks', 'Failure reporting'] },
  PM: { title: 'PM', subtitle: 'Operator summary', short: 'PM', icon: 'description', capabilities: ['Status briefs', 'Milestone summaries', 'Operational notes'] },
  Support: { title: 'Support', subtitle: 'Handoff', short: 'SUP', icon: 'support_agent', capabilities: ['User-facing summary', 'Operator guidance', 'Context packaging'] },
}

export const swarmTemplates: Array<{ value: SwarmTemplate; label: string; description: string }> = [
  { value: 'Sequential', label: 'Sequential', description: 'Roles hand work off in a strict linear chain.' },
  { value: 'Hierarchical', label: 'Hierarchical', description: 'Manager drives specialists with visible delegation.' },
  { value: 'ParallelReview', label: 'Parallel Review', description: 'Frontend and backend converge into review and summary.' },
]

export function repoKey(repository: RepositoryRef) {
  return `${repository.owner}/${repository.name}`
}

export function preferredModel(models: OllamaModelInfo[], fallback: string) {
  return models.find((item) => item.name === fallback)?.name ?? models.find((item) => !/embed/i.test(item.name))?.name ?? fallback
}

export function formatClock(value?: string | null) {
  if (!value) {
    return 'No time'
  }

  return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

export function formatDateTime(value?: string | null) {
  if (!value) {
    return 'Unknown'
  }

  return new Date(value).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

export function readableAgentStatus(agent: AgentSnapshot) {
  return agent.detail?.trim() || agent.status
}

export function toneByStatus(status: string) {
  const normalized = status.toLowerCase()
  if (normalized.includes('completed') || normalized.includes('applied')) return 'good'
  if (normalized.includes('failed') || normalized.includes('error') || normalized.includes('rejected') || normalized.includes('cancelled')) return 'bad'
  if (normalized.includes('queued') || normalized.includes('pending') || normalized.includes('awaiting') || normalized.includes('running')) return 'warn'
  return 'neutral'
}

export function toneByAgentStatus(status: AgentRunStatus) {
  if (status === 'Completed') return 'good'
  if (status === 'Error') return 'bad'
  if (status === 'Idle' || status === 'Waiting') return 'neutral'
  return 'warn'
}

export function buildIdleOverview(): OverviewSnapshot {
  const now = new Date().toISOString()
  const agents: AgentSnapshot[] = (['Manager', 'Analyst', 'WebDev', 'Frontend', 'Backend', 'Tester', 'PM', 'Support'] as AgentRole[]).map((role) => ({
    role,
    status: 'Idle',
    label: role,
    detail: 'Waiting for a task',
    updatedAt: now,
    queueDepth: 0,
  }))

  return {
    activeRun: null,
    recentRuns: [],
    system: {
      logicalQueueDepth: 0,
      chatModel: 'qwen2.5-coder:14b',
      physicalWorkerCount: 1,
    },
    agents,
  }
}

export function buildIdleMission(overview: OverviewSnapshot): Mission {
  const now = new Date().toISOString()
  return {
    id: 'idle-run',
    title: 'No active run',
    prompt: '',
    objective: '',
    swarmTemplate: 'Hierarchical',
    status: 'Draft',
    createdAt: now,
    updatedAt: now,
    currentPhase: 'Idle',
    isArchived: false,
    archivedAt: null,
    cancelledAt: null,
    cancelledReason: null,
    selectedRepository: null,
    selectedSprint: null,
    selectedWorkItem: null,
    externalTask: null,
    pullRequest: null,
    autoCreatePullRequest: true,
    workspaceRootPath: null,
    steps: [],
    patchProposals: [],
    agents: overview.agents,
    artifacts: {},
  }
}

export function activeAgents(agents: AgentSnapshot[]) {
  return agents.filter((agent) => !['Idle', 'Waiting'].includes(agent.status))
}

export function latestRun(runs: Mission[]) {
  return runs[0] ?? null
}
