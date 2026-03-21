import { startTransition, useDeferredValue, useEffect, useMemo, useState } from 'react'
import {
  createMission,
  createRealtimeConnection,
  createThread,
  decidePatch,
  ensureDefaultMilestones,
  fetchDashboard,
  fetchMessages,
  fetchMilestones,
  fetchModels,
  fetchProgress,
  fetchRepositories,
  fetchThreads,
  sendMessage,
} from '../api'
import type {
  ChatMessage,
  ChatThread,
  DashboardSnapshot,
  Mission,
  OllamaModelInfo,
  PatchProposal,
  ProgressLog,
  RepositoryRef,
  SprintRef,
} from '../types'
import { buildFallbackDashboard, preferredModel, repoKey } from './view-models'

export type ApexConsoleState = ReturnType<typeof useApexConsole>

export function useApexConsole() {
  const fallback = useMemo(() => buildFallbackDashboard(), [])
  const [dashboard, setDashboard] = useState<DashboardSnapshot>(fallback)
  const [title, setTitle] = useState('Neural Workspace Build')
  const [prompt, setPrompt] = useState('APEX knowledge base icin referans ekranlardaki estetikle yeni control room UI olustur.')
  const [busy, setBusy] = useState(false)
  const [chatBusy, setChatBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [connected, setConnected] = useState(false)
  const [repositories, setRepositories] = useState<RepositoryRef[]>([])
  const [selectedRepoKey, setSelectedRepoKey] = useState('')
  const [repoStatus, setRepoStatus] = useState('GitHub repository listesi yukleniyor...')
  const [sprints, setSprints] = useState<SprintRef[]>([])
  const [selectedSprintId, setSelectedSprintId] = useState('')
  const [progressLogs, setProgressLogs] = useState<ProgressLog[]>(fallback.recentProgressLogs)
  const [models, setModels] = useState<OllamaModelInfo[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [threads, setThreads] = useState<ChatThread[]>([])
  const [selectedThreadId, setSelectedThreadId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')

  const deferredActivities = useDeferredValue(dashboard.recentActivities)
  const deferredProgress = useDeferredValue(progressLogs)
  const mission = dashboard.activeMission ?? fallback.activeMission!
  const selectedRepository = useMemo(() => repositories.find((repo) => repoKey(repo) === selectedRepoKey) ?? null, [repositories, selectedRepoKey])
  const selectedSprint = useMemo(() => sprints.find((sprint) => String(sprint.id) === selectedSprintId) ?? null, [selectedSprintId, sprints])
  const chatModels = useMemo(() => {
    const filtered = models.filter((model) => !/embed/i.test(model.name))
    return filtered.length > 0 ? filtered : models
  }, [models])
  const selectedThread = useMemo(() => threads.find((thread) => thread.id === selectedThreadId) ?? null, [threads, selectedThreadId])
  const changedFiles = useMemo(() => Array.from(new Set((mission.patchProposals ?? []).flatMap((proposal) => proposal.targetPaths))).slice(0, 8), [mission.patchProposals])

  useEffect(() => {
    let active = true

    async function boot() {
      try {
        const [snapshot, repoList, modelList, threadList] = await Promise.all([
          fetchDashboard(),
          fetchRepositories(),
          fetchModels(),
          fetchThreads(),
        ])

        if (!active) {
          return
        }

        startTransition(() => {
          setDashboard(snapshot)
          setProgressLogs(snapshot.recentProgressLogs.length > 0 ? snapshot.recentProgressLogs : fallback.recentProgressLogs)
          setRepositories(repoList)
          setRepoStatus(repoList.length > 0 ? `${repoList.length} repository hazir` : 'Repository gelmedi. Token erisimi kontrol edilmeli.')
          setModels(modelList)
          setThreads(threadList)
          setSelectedThreadId((current) => current ?? threadList[0]?.id ?? null)
          setSelectedModel((current) => current || preferredModel(modelList, snapshot.chatModel))
        })
      } catch (requestError) {
        if (active) {
          setError(requestError instanceof Error ? requestError.message : 'Ilk yukleme basarisiz oldu.')
          setRepoStatus('Repository lookup basarisiz oldu.')
        }
      }
    }

    void boot()

    const connection = createRealtimeConnection({
      onActivity: (event) => {
        startTransition(() => {
          setDashboard((current) => ({
            ...current,
            recentActivities: [...current.recentActivities.slice(-31), event],
          }))
        })
      },
      onProgress: (event) => {
        startTransition(() => {
          setProgressLogs((current) => [...current.slice(-79), event])
        })
      },
    })

    void connection.start().then(() => setConnected(true)).catch(() => setConnected(false))

    const intervalId = window.setInterval(() => {
      void refreshDashboard(active, fallback, setDashboard, setProgressLogs, setError)
    }, 6000)

    return () => {
      active = false
      window.clearInterval(intervalId)
      void connection.stop()
    }
  }, [fallback])

  useEffect(() => {
    const activeMission = dashboard.activeMission
    if (!activeMission) {
      return
    }

    if (activeMission.selectedRepository) {
      setSelectedRepoKey(repoKey(activeMission.selectedRepository))
    }

    if (activeMission.selectedSprint) {
      setSelectedSprintId(String(activeMission.selectedSprint.id))
    }

    if (activeMission.id) {
      void fetchProgress(activeMission.id)
        .then((items) => setProgressLogs(items.length > 0 ? items : fallback.recentProgressLogs))
        .catch(() => undefined)
    }
  }, [dashboard.activeMission?.id, fallback.recentProgressLogs])

  useEffect(() => {
    if (!selectedRepository) {
      setSprints([])
      setSelectedSprintId('')
      setRepoStatus(repositories.length > 0 ? `${repositories.length} repository hazir` : 'GitHub repository listesi yukleniyor...')
      return
    }

    const repository = selectedRepository
    let active = true

    async function loadSprints() {
      try {
        let items = await fetchMilestones(repository.owner, repository.name)
        if (items.length === 0) {
          setRepoStatus(`${repository.fullName} icin sprint bulunamadi. Varsayilan sprintler olusturuluyor...`)
          items = await ensureDefaultMilestones(repository.owner, repository.name)
        }

        if (!active) {
          return
        }

        setSprints(items)
        setSelectedSprintId((current) => (current && items.some((item) => String(item.id) === current) ? current : String(items[0]?.id ?? '')))
        setRepoStatus(items.length > 0 ? `${repository.fullName} • ${items.length} sprint hazir` : `${repository.fullName} milestone donmedi`)
      } catch (requestError) {
        if (!active) {
          return
        }

        setSprints([])
        setSelectedSprintId('')
        setRepoStatus(requestError instanceof Error ? requestError.message : 'Sprint lookup basarisiz oldu')
      }
    }

    void loadSprints()

    return () => {
      active = false
    }
  }, [selectedRepository, repositories.length])

  useEffect(() => {
    if (!selectedThreadId) {
      setMessages([])
      return
    }

    let active = true
    void fetchMessages(selectedThreadId)
      .then((items) => {
        if (active) {
          setMessages(items)
        }
      })
      .catch(() => {
        if (active) {
          setMessages([])
        }
      })

    return () => {
      active = false
    }
  }, [selectedThreadId])

  async function handleCreateMission() {
    setBusy(true)
    setError(null)
    try {
      const nextMission = (await createMission({
        title,
        prompt,
        selectedRepository,
        selectedSprint,
      })) as Mission

      startTransition(() => {
        setDashboard((current) => ({
          ...current,
          activeMission: nextMission,
          agents: nextMission.agents,
          pendingPatchProposals: nextMission.patchProposals.filter((proposal) => proposal.status === 'PendingReview'),
        }))
      })
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Mission olusturma basarisiz oldu.')
    } finally {
      setBusy(false)
    }
  }

  async function handlePatchDecision(proposal: PatchProposal, action: 'approve' | 'reject') {
    try {
      await decidePatch(proposal.id, action)
      await refreshDashboard(true, fallback, setDashboard, setProgressLogs, setError)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Patch karari gonderilemedi.')
    }
  }

  async function handleNewThread() {
    try {
      const thread = await createThread(selectedModel || dashboard.chatModel)
      setThreads((current) => [thread, ...current.filter((item) => item.id !== thread.id)])
      setSelectedThreadId(thread.id)
      setMessages([])
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Yeni thread olusturulamadi.')
    }
  }

  async function handleSendMessage() {
    const content = chatInput.trim()
    if (!content || chatBusy) {
      return
    }

    setChatBusy(true)
    setError(null)
    try {
      let threadId = selectedThreadId
      if (!threadId) {
        const thread = await createThread(selectedModel || dashboard.chatModel)
        threadId = thread.id
        setThreads((current) => [thread, ...current.filter((item) => item.id !== thread.id)])
        setSelectedThreadId(thread.id)
      }

      const result = await sendMessage(threadId, content, selectedModel || dashboard.chatModel)
      setChatInput('')
      setThreads((current) => [result.thread, ...current.filter((item) => item.id !== result.thread.id)])
      setMessages((current) => [...current, result.userMessage, result.assistantMessage])
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Mesaj gonderilemedi.')
    } finally {
      setChatBusy(false)
    }
  }

  return {
    busy,
    changedFiles,
    chatBusy,
    chatInput,
    chatModels,
    connected,
    dashboard,
    deferredActivities,
    deferredProgress,
    error,
    fallback,
    messages,
    mission,
    models,
    progressLogs,
    prompt,
    repoStatus,
    repositories,
    selectedModel,
    selectedRepoKey,
    selectedRepository,
    selectedSprint,
    selectedSprintId,
    selectedThread,
    selectedThreadId,
    sprints,
    threads,
    title,
    setChatInput,
    setPrompt,
    setSelectedModel,
    setSelectedRepoKey,
    setSelectedSprintId,
    setSelectedThreadId,
    setTitle,
    handleCreateMission,
    handleNewThread,
    handlePatchDecision,
    handleSendMessage,
  }
}

async function refreshDashboard(
  active: boolean,
  fallback: DashboardSnapshot,
  setDashboard: React.Dispatch<React.SetStateAction<DashboardSnapshot>>,
  setProgressLogs: React.Dispatch<React.SetStateAction<ProgressLog[]>>,
  setError: React.Dispatch<React.SetStateAction<string | null>>,
) {
  try {
    const snapshot = await fetchDashboard()
    if (!active) {
      return
    }

    startTransition(() => {
      setDashboard((current) => ({ ...current, ...snapshot }))
      setProgressLogs(snapshot.recentProgressLogs.length > 0 ? snapshot.recentProgressLogs : fallback.recentProgressLogs)
    })
  } catch (requestError) {
    if (active) {
      setError(requestError instanceof Error ? requestError.message : 'Dashboard yukleme basarisiz oldu.')
    }
  }
}
