import type {
  ActivityEvent,
  AgentPodGroup,
  AgentRole,
  AgentRunStatus,
  AgentSnapshot,
  DashboardMetric,
  DashboardSnapshot,
  ExecutionFeedRow,
  Mission,
  MissionStep,
  OllamaModelInfo,
  ProgressLog,
  RepositoryRef,
  SprintRef,
  WorkflowEdge,
  WorkflowNode,
} from '../types'

export type RoleVisualMeta = {
  title: string
  subtitle: string
  short: string
  icon: string
  tone: 'cyan' | 'lime' | 'violet' | 'rose'
  podId: string
  registryId: string
  capabilities: string[]
}

export const roleMeta: Record<AgentRole, RoleVisualMeta> = {
  Manager: {
    title: 'Manager',
    subtitle: 'Koordinasyon cekirdegi',
    short: 'MGR',
    icon: 'hub',
    tone: 'cyan',
    podId: 'core',
    registryId: 'AG-MGR-001',
    capabilities: ['Mission Routing', 'Queue Control', 'Patch Governance'],
  },
  Analyst: {
    title: 'Analyst',
    subtitle: 'Gereksinim zekasi',
    short: 'ANL',
    icon: 'psychology',
    tone: 'violet',
    podId: 'intelligence',
    registryId: 'AG-ANL-204',
    capabilities: ['Acceptance Criteria', 'Backlog Synthesis', 'Knowledge Lookup'],
  },
  WebDev: {
    title: 'WebDev',
    subtitle: 'Mimari katman',
    short: 'WEB',
    icon: 'architecture',
    tone: 'cyan',
    podId: 'core',
    registryId: 'AG-WEB-042',
    capabilities: ['API Contract', 'System Design', 'Repo Planning'],
  },
  Frontend: {
    title: 'Frontend',
    subtitle: 'UI ve deneyim',
    short: 'FE',
    icon: 'palette',
    tone: 'violet',
    podId: 'delivery',
    registryId: 'AG-FE-334',
    capabilities: ['React', 'State Design', 'Motion Systems'],
  },
  Backend: {
    title: 'Backend',
    subtitle: 'Servis orkestrasi',
    short: 'BE',
    icon: 'dns',
    tone: 'lime',
    podId: 'core',
    registryId: 'AG-BE-110',
    capabilities: ['API', 'Persistence', 'Integration Tests'],
  },
  Tester: {
    title: 'Tester',
    subtitle: 'Guven ve kalite',
    short: 'QA',
    icon: 'bug_report',
    tone: 'rose',
    podId: 'delivery',
    registryId: 'AG-QA-909',
    capabilities: ['Regression', 'Smoke', 'Failure Reports'],
  },
  PM: {
    title: 'PM',
    subtitle: 'Sprint kontrolu',
    short: 'PM',
    icon: 'view_timeline',
    tone: 'cyan',
    podId: 'delivery',
    registryId: 'AG-PM-552',
    capabilities: ['Status Briefs', 'Milestone Sync', 'Timeline Notes'],
  },
  Support: {
    title: 'Support',
    subtitle: 'Kullanici sesi',
    short: 'SUP',
    icon: 'support_agent',
    tone: 'lime',
    podId: 'intelligence',
    registryId: 'AG-SUP-811',
    capabilities: ['Customer Replies', 'Context Recall', 'Thread Handoff'],
  },
}

const podBlueprints: Omit<AgentPodGroup, 'count'>[] = [
  {
    id: 'core',
    label: 'Core Infrastructure',
    description: 'Orchestrator, backend ve architecture layer',
    roles: ['Manager', 'WebDev', 'Backend'],
    tone: 'cyan',
  },
  {
    id: 'delivery',
    label: 'Product Delivery',
    description: 'UI, kalite ve sprint akis kontrolu',
    roles: ['Frontend', 'Tester', 'PM'],
    tone: 'lime',
  },
  {
    id: 'intelligence',
    label: 'Intelligence Nodes',
    description: 'Analiz ve kullanici baglami',
    roles: ['Analyst', 'Support'],
    tone: 'violet',
  },
]

const reliabilityBase: Record<AgentRunStatus, number> = {
  Idle: 88,
  Thinking: 95,
  Delegating: 98,
  Coding: 94,
  Reviewing: 96,
  Waiting: 90,
  Completed: 99.4,
  Error: 48,
}

const workflowRow: Record<AgentRole, number> = {
  Manager: 18,
  Analyst: 50,
  WebDev: 32,
  Frontend: 64,
  Backend: 34,
  Tester: 66,
  PM: 18,
  Support: 72,
}

export function repoKey(repository: RepositoryRef) {
  return `${repository.owner}/${repository.name}`
}

export function preferredModel(models: OllamaModelInfo[], fallback: string) {
  return models.find((item) => item.name === fallback)?.name ?? models.find((item) => !/embed/i.test(item.name))?.name ?? fallback
}

export function formatClock(value: string) {
  return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

export function formatRelativeDate(value?: string | null) {
  if (!value) {
    return 'Takvim tanimsiz'
  }

  return new Date(value).toLocaleDateString([], { month: 'short', day: 'numeric' })
}

export function readableAgentStatus(agent: AgentSnapshot) {
  if (agent.status === 'Idle') return 'Standby'
  if (agent.status === 'Waiting') return 'Beklemede'
  if (agent.status === 'Thinking') return 'Analiz yurutuluyor'
  if (agent.status === 'Delegating') return 'Gorev dagitiliyor'
  if (agent.status === 'Coding') return 'Uretim aktif'
  if (agent.status === 'Reviewing') return 'Kontrol aktif'
  if (agent.status === 'Completed') return 'Tur tamamlandi'
  return 'Hata tespit edildi'
}

export function statusTone(status: string) {
  const lower = status.toLowerCase()
  if (lower.includes('completed') || lower.includes('applied') || lower.includes('live')) return 'good'
  if (lower.includes('failed') || lower.includes('rejected') || lower.includes('error')) return 'bad'
  if (lower.includes('pending') || lower.includes('queued') || lower.includes('awaiting')) return 'warn'
  return 'neutral'
}

export function toneByRunStatus(status: AgentSnapshot['status']) {
  if (status === 'Completed') return 'good'
  if (status === 'Error') return 'bad'
  if (status === 'Idle' || status === 'Waiting') return 'neutral'
  return 'warn'
}

export function computeReliability(agent: AgentSnapshot) {
  const base = reliabilityBase[agent.status] ?? 90
  const penalty = Math.min(agent.queueDepth * 1.3, 8)
  const blended = Math.max(41.2, Math.min(99.9, base - penalty + 1.8))
  return blended.toFixed(1)
}

export function buildDashboardMetrics(snapshot: DashboardSnapshot, mission: Mission): DashboardMetric[] {
  const activeNodes = snapshot.agents.filter((agent) => !['Idle', 'Waiting'].includes(agent.status)).length
  const completedSteps = mission.steps.filter((step) => step.status === 'Completed').length
  const progress = mission.steps.length > 0 ? Math.round((completedSteps / mission.steps.length) * 100) : 0
  const liveReliability = snapshot.agents.length > 0
    ? (snapshot.agents.reduce((sum, agent) => sum + Number.parseFloat(computeReliability(agent)), 0) / snapshot.agents.length).toFixed(1)
    : '0.0'

  return [
    { label: 'Aktif node', value: String(activeNodes), helper: 'canli gorevde', tone: 'cyan' },
    { label: 'Sprint ilerleme', value: `%${progress}`, helper: `${completedSteps}/${mission.steps.length} tamam`, tone: 'lime' },
    { label: 'Kuyruk', value: String(snapshot.logicalQueueDepth), helper: 'mantiksal derinlik', tone: 'violet' },
    { label: 'Stability', value: `${liveReliability}%`, helper: 'turetilmis health', tone: 'rose' },
  ]
}

export function buildAgentPodGroups(agents: AgentSnapshot[]): AgentPodGroup[] {
  return podBlueprints.map((pod) => ({
    ...pod,
    count: agents.filter((agent) => pod.roles.includes(agent.role)).length,
  }))
}

export function buildWorkflowGraph(mission: Mission, progressLogs: ProgressLog[]): { nodes: WorkflowNode[]; edges: WorkflowEdge[] } {
  const sortedSteps = [...mission.steps].sort((left, right) => left.order - right.order)
  const width = sortedSteps.length > 1 ? 76 / (sortedSteps.length - 1) : 0

  const nodes = sortedSteps.map((step, index) => ({
    id: step.id,
    x: 10 + (index * width),
    y: workflowRow[step.owner],
    width: 18,
    owner: step.owner,
    title: step.title,
    summary: step.summary,
    status: step.status,
    tone: roleMeta[step.owner].tone,
  }))

  const edges: WorkflowEdge[] = []
  for (const step of sortedSteps) {
    for (const dependency of step.dependencies) {
      const source = sortedSteps.find((candidate) => candidate.id === dependency)
      if (!source) {
        continue
      }

      const active = progressLogs.some((item) => item.metadata.fromRole === source.owner && item.metadata.toRole === step.owner)
      edges.push({
        id: `${dependency}-${step.id}`,
        from: dependency,
        to: step.id,
        active,
        label: `${roleMeta[source.owner].short} -> ${roleMeta[step.owner].short}`,
      })
    }
  }

  return { nodes, edges }
}

export function buildExecutionFeed(snapshot: DashboardSnapshot, progressLogs: ProgressLog[]): ExecutionFeedRow[] {
  const activityRows = snapshot.recentActivities.map((item) => {
    const tone = item.agentRole ? roleMeta[item.agentRole].tone : 'neutral'
    const tag = item.agentRole ? roleMeta[item.agentRole].short : 'SYS'
    return {
      id: item.id,
      createdAt: item.createdAt,
      tag,
      tone,
      content: `${item.summary} ${item.details}`.trim(),
    } satisfies Omit<ExecutionFeedRow, 'line'>
  })

  const progressRows = progressLogs.map((item) => {
    const tone = item.role ? roleMeta[item.role].tone : 'neutral'
    const tag = item.role ? roleMeta[item.role].short : item.stage.toUpperCase()
    return {
      id: item.id,
      createdAt: item.createdAt,
      tag,
      tone,
      content: item.message,
    } satisfies Omit<ExecutionFeedRow, 'line'>
  })

  return [...activityRows, ...progressRows]
    .sort((left, right) => new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime())
    .slice(-18)
    .map((row, index) => ({ ...row, line: index + 1 }))
}

export function buildFallbackDashboard(): DashboardSnapshot {
  const now = new Date().toISOString()
  const agents: AgentSnapshot[] = [
    { role: 'Manager', status: 'Idle', label: 'Manager', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'Analyst', status: 'Idle', label: 'Analyst', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'WebDev', status: 'Idle', label: 'WebDev', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'Frontend', status: 'Idle', label: 'Frontend', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'Backend', status: 'Idle', label: 'Backend', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'Tester', status: 'Idle', label: 'Tester', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'PM', status: 'Idle', label: 'PM', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
    { role: 'Support', status: 'Idle', label: 'Support', detail: 'Task secimi bekleniyor', updatedAt: now, queueDepth: 0 },
  ]

  return {
    activeMission: null,
    agents,
    recentActivities: [
      { id: 'evt-idle', missionId: 'idle-mission', createdAt: now, eventType: 'QueueStatusChanged', agentRole: 'Manager', summary: 'Sistem hazir.', details: 'Repo sec, sprint sec ve istedigin taski manager agentina gonder.' },
    ],
    recentProgressLogs: [],
    pendingPatchProposals: [],
    logicalQueueDepth: 0,
    chatModel: 'qwen2.5-coder:14b',
    physicalWorkerCount: 1,
  }
}

export function topAgents(agents: AgentSnapshot[]) {
  return [...agents]
    .sort((left, right) => Number.parseFloat(computeReliability(right)) - Number.parseFloat(computeReliability(left)))
    .slice(0, 4)
}

export function activeDiscourseTitle(threadTitle?: string | null) {
  return threadTitle && threadTitle.trim().length > 0 ? threadTitle : 'Canli intervention thread'
}

export function stepProgressValue(step: MissionStep, index: number, total: number) {
  if (step.status === 'Completed') return 100
  if (step.status === 'InProgress') return Math.max(28, 40 + Math.round((index / Math.max(total, 1)) * 28))
  if (step.status === 'Blocked') return 12
  return 4
}

export function activitySummary(items: ActivityEvent[]) {
  return items.length > 0 ? items[items.length - 1].summary : 'Canli event bekleniyor'
}




