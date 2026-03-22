import * as signalR from '@microsoft/signalr'
import type {
  ActivityEvent,
  AgentRuntimeCatalog,
  ChatExchangeResult,
  ChatMessage,
  ChatThread,
  CreateMissionRequest,
  DashboardSnapshot,
  GitHubBoardSnapshot,
  OllamaModelInfo,
  PatchProposal,
  ProgressLog,
  RepositoryRef,
} from './types'

const configuredBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '')
const API_BASE = configuredBase ?? ''

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${url}`, init)
  if (!response.ok) {
    throw new Error(`Request failed with ${response.status}`)
  }

  return response.json() as Promise<T>
}

export function createRealtimeConnection(handlers: { onActivity: (event: ActivityEvent) => void; onProgress: (event: ProgressLog) => void }) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE}/hubs/activity`)
    .withAutomaticReconnect()
    .build()

  connection.on('activity', handlers.onActivity)
  connection.on('progress', handlers.onProgress)
  return connection
}

export function fetchDashboard() {
  return requestJson<DashboardSnapshot>('/api/dashboard')
}

export function fetchRepositories() {
  return requestJson<RepositoryRef[]>('/api/github/repositories')
}

export function fetchRepositoryBoard(owner: string, repo: string) {
  return requestJson<GitHubBoardSnapshot>(`/api/github/repositories/${owner}/${repo}/board`)
}

export function createMission(request: CreateMissionRequest) {
  return requestJson('/api/missions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })
}

export function decidePatch(proposalId: string, action: 'approve' | 'reject') {
  return requestJson<PatchProposal>(`/api/patches/${proposalId}/${action}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reviewNote: `${action} from dashboard` }),
  })
}

export function fetchProgress(missionId: string) {
  return requestJson<ProgressLog[]>(`/api/missions/${missionId}/progress`)
}

export function fetchModels() {
  return requestJson<OllamaModelInfo[]>('/api/ollama/models')
}

export function fetchAgentRuntime() {
  return requestJson<AgentRuntimeCatalog>('/api/agent-runtime')
}

export function saveAgentTool(tool: {
  name: string
  displayName: string
  description: string
  type: string
  enabled: boolean
  destructive: boolean
  commandTemplate?: string | null
}) {
  return requestJson('/api/agent-runtime/tools', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(tool),
  })
}

export function saveAgentPolicy(role: string, policy: {
  executionMode: string
  allowedTools: string[]
  writableRoots: string[]
  maxSteps: number
}) {
  return requestJson(`/api/agent-runtime/policies/${role}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(policy),
  })
}

export function fetchThreads() {
  return requestJson<ChatThread[]>('/api/chat/threads')
}

export function createThread(model: string, title?: string) {
  return requestJson<ChatThread>('/api/chat/threads', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, model }),
  })
}

export function fetchMessages(threadId: string) {
  return requestJson<ChatMessage[]>(`/api/chat/threads/${threadId}/messages`)
}

export function sendMessage(threadId: string, content: string, model?: string) {
  return requestJson<ChatExchangeResult>(`/api/chat/threads/${threadId}/messages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content, model }),
  })
}
