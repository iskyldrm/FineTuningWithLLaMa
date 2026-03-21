import * as signalR from '@microsoft/signalr'
import type {
  ActivityEvent,
  ChatExchangeResult,
  ChatMessage,
  ChatThread,
  CreateMissionRequest,
  DashboardSnapshot,
  OllamaModelInfo,
  PatchProposal,
  ProgressLog,
  RepositoryRef,
  SprintRef,
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

export function fetchMilestones(owner: string, repo: string) {
  return requestJson<SprintRef[]>(`/api/github/repositories/${owner}/${repo}/milestones`)
}

export function ensureDefaultMilestones(owner: string, repo: string) {
  return requestJson<SprintRef[]>(`/api/github/repositories/${owner}/${repo}/milestones/defaults`, {
    method: 'POST',
  })
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
