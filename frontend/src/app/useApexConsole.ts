import { startTransition, useDeferredValue, useEffect, useMemo, useState } from 'react'
import {
  createMission,
  createRealtimeConnection,
  createThread,
  decidePatch,
  fetchDashboard,
  fetchMessages,
  fetchModels,
  fetchRepositoryBoard,
  fetchProgress,
  fetchRepositories,
  fetchThreads,
  sendMessage,
} from '../api'
import type {
  ChatMessage,
  ChatThread,
  DashboardSnapshot,
  GitHubBoardItemRef,
  GitHubBoardSnapshot,
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
  const [board, setBoard] = useState<GitHubBoardSnapshot | null>(null)
  const [sprints, setSprints] = useState<SprintRef[]>([])
  const [selectedSprintId, setSelectedSprintId] = useState('')
  const [selectedWorkItemId, setSelectedWorkItemId] = useState('')
  const [progressLogs, setProgressLogs] = useState<ProgressLog[]>(fallback.recentProgressLogs)
  const [models, setModels] = useState<OllamaModelInfo[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [threads, setThreads] = useState<ChatThread[]>([])
  const [selectedThreadId, setSelectedThreadId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')

  const deferredActivities = useDeferredValue(dashboard.recentActivities)
  const deferredProgress = useDeferredValue(progressLogs)
  const mission = dashboard.activeMission ?? buildIdleMission(fallback)
  const selectedRepository = useMemo(() => repositories.find((repo) => repoKey(repo) === selectedRepoKey) ?? null, [repositories, selectedRepoKey])
  const selectedSprint = useMemo(() => sprints.find((sprint) => sprint.id === selectedSprintId) ?? null, [selectedSprintId, sprints])
  const selectedWorkItem = useMemo(() => board?.items.find((item) => item.id === selectedWorkItemId) ?? null, [board?.items, selectedWorkItemId])
  const chatModels = useMemo(() => {
    const filtered = models.filter((model) => !/embed/i.test(model.name))
    return filtered.length > 0 ? filtered : models
  }, [models])
  const selectedThread = useMemo(() => threads.find((thread) => thread.id === selectedThreadId) ?? null, [threads, selectedThreadId])
  const changedFiles = useMemo(() => Array.from(new Set((mission.patchProposals ?? []).flatMap((proposal) => proposal.targetPaths))).slice(0, 8), [mission.patchProposals])

  useEffect(() => {
    let active = true

    async function boot() {
      const [dashboardResult, repositoriesResult, modelsResult, threadsResult] = await Promise.allSettled([
        fetchDashboard(),
        fetchRepositories(),
        fetchModels(),
        fetchThreads(),
      ])

      if (!active) {
        return
      }

      startTransition(() => {
        if (dashboardResult.status === 'fulfilled') {
          setDashboard(dashboardResult.value)
          setProgressLogs(dashboardResult.value.recentProgressLogs.length > 0 ? dashboardResult.value.recentProgressLogs : fallback.recentProgressLogs)
        } else {
          setError(dashboardResult.reason instanceof Error ? dashboardResult.reason.message : 'Dashboard yukleme basarisiz oldu.')
        }

        if (repositoriesResult.status === 'fulfilled') {
          setRepositories(repositoriesResult.value)
          setRepoStatus(
            repositoriesResult.value.length > 0
              ? `${repositoriesResult.value.length} repository hazir`
              : 'Repository gelmedi. .env icine GITHUB_TOKEN, GITHUB_OWNER ve GITHUB_REPO ekle.'
          )
        } else {
          setRepoStatus(repositoriesResult.reason instanceof Error ? repositoriesResult.reason.message : 'Repository lookup basarisiz oldu.')
        }

        if (modelsResult.status === 'fulfilled') {
          setModels(modelsResult.value)
          const modelFallback = dashboardResult.status === 'fulfilled' ? dashboardResult.value.chatModel : fallback.chatModel
          setSelectedModel((current) => current || preferredModel(modelsResult.value, modelFallback))
        }

        if (threadsResult.status === 'fulfilled') {
          setThreads(threadsResult.value)
          setSelectedThreadId((current) => current ?? threadsResult.value[0]?.id ?? null)
        }
      })
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
      setSelectedSprintId(activeMission.selectedSprint.id)
    }

    if (activeMission.selectedWorkItem) {
      setSelectedWorkItemId(activeMission.selectedWorkItem.id)
    }

    if (activeMission.id) {
      void fetchProgress(activeMission.id)
        .then((items) => setProgressLogs(items.length > 0 ? items : fallback.recentProgressLogs))
        .catch(() => undefined)
    }
  }, [dashboard.activeMission?.id, fallback.recentProgressLogs])

  useEffect(() => {
    if (!selectedRepository) {
      setBoard(null)
      setSprints([])
      setSelectedSprintId('')
      setSelectedWorkItemId('')
      setRepoStatus(repositories.length > 0 ? `${repositories.length} repository hazir` : 'GitHub repository listesi yukleniyor...')
      return
    }

    const repository = selectedRepository
    let active = true

    async function loadBoard() {
      try {
        const snapshot = await fetchRepositoryBoard(repository.owner, repository.name)

        if (!active) {
          return
        }

        setBoard(snapshot)
        setSprints(snapshot.sprints)
        setSelectedSprintId((current) => (current && snapshot.sprints.some((item) => item.id === current) ? current : snapshot.sprints[0]?.id ?? ''))
        setSelectedWorkItemId((current) => (current && snapshot.items.some((item) => item.id === current) ? current : ''))
        setRepoStatus(snapshot.statusMessage || `${repository.fullName} board hazir`)
      } catch (requestError) {
        if (!active) {
          return
        }

        setBoard(null)
        setSprints([])
        setSelectedSprintId('')
        setSelectedWorkItemId('')
        setRepoStatus(requestError instanceof Error ? requestError.message : 'Sprint lookup basarisiz oldu')
      }
    }

    void loadBoard()

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

  useEffect(() => {
    if (!board || !selectedSprintId) {
      return
    }

    setSelectedWorkItemId((current) => {
      if (current && board.items.some((item) => item.id === current && item.sprintId === selectedSprintId)) {
        return current
      }

      return ''
    })
  }, [board, selectedSprintId])

  async function handleCreateMission() {
    setBusy(true)
    setError(null)
    try {
      const nextMission = (await createMission({
        title,
        prompt,
        selectedRepository,
        selectedSprint,
        selectedWorkItem,
        autoCreatePullRequest: true,
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

  async function handleDispatchWorkItem(workItem: GitHubBoardItemRef) {
    const sprintForItem = sprints.find((item) => item.id === workItem.sprintId) ?? selectedSprint ?? null
    const nextTitle = workItem.title
    const nextPrompt = buildWorkItemPrompt(workItem, selectedRepository, sprintForItem)

    setSelectedWorkItemId(workItem.id)
    setSelectedSprintId(workItem.sprintId)
    setTitle(nextTitle)
    setPrompt(nextPrompt)
    setBusy(true)
    setError(null)

    try {
      const nextMission = (await createMission({
        title: nextTitle,
        prompt: nextPrompt,
        selectedRepository,
        selectedSprint: sprintForItem,
        selectedWorkItem: workItem,
        autoCreatePullRequest: true,
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
      setError(requestError instanceof Error ? requestError.message : 'Task dispatch basarisiz oldu.')
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
    board,
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
    selectedWorkItem,
    selectedWorkItemId,
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
    setSelectedWorkItemId,
    setSelectedThreadId,
    setTitle,
    handleCreateMission,
    handleDispatchWorkItem,
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

function buildWorkItemPrompt(workItem: GitHubBoardItemRef, repository: RepositoryRef | null, sprint: SprintRef | null) {
  const subtasks = workItem.subtasks.length > 0
    ? workItem.subtasks.map((item) => `- ${item}`).join('\n')
    : '- Gorevi issue aciklamasina gore tamamla.'

  return [
    `GitHub board gorevini secili repository icinde tamamla: ${workItem.title}.`,
    repository ? `Repository: ${repository.fullName}.` : 'Repository secimi eksik.',
    sprint ? `Sprint: ${sprint.title}.` : 'Sprint secimi yok.',
    `Status lane: ${workItem.status}.`,
    workItem.description ? `Aciklama:\n${workItem.description}` : 'Aciklama yok.',
    `Checklist:\n${subtasks}`,
    'Gerekli degisiklikleri repo klasorlerinde yap, patchleri hazirla, dogrulamayi calistir ve uygunsa PR olustur.',
  ].join('\n\n')
}

function buildIdleMission(fallback: DashboardSnapshot): Mission {
  const now = new Date().toISOString()
  return {
    id: 'idle-mission',
    title: 'Task secimi bekleniyor',
    prompt: '',
    status: 'Draft',
    createdAt: now,
    updatedAt: now,
    currentPhase: 'Idle',
    selectedRepository: null,
    selectedSprint: null,
    selectedWorkItem: null,
    externalTask: null,
    pullRequest: null,
    autoCreatePullRequest: true,
    workspaceRootPath: null,
    steps: [],
    patchProposals: [],
    agents: fallback.agents,
    artifacts: {},
  }
}

