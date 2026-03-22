import { startTransition, useDeferredValue, useEffect, useMemo, useState } from 'react'
import type { Dispatch, SetStateAction } from 'react'
import {
  archiveRun,
  cancelRun,
  createRealtimeConnection,
  createRun,
  createThread,
  decidePatch,
  fetchAgentRuntime,
  fetchMessages,
  fetchModels,
  fetchOverview,
  fetchRepositories,
  fetchRepositoryBoard,
  fetchRun,
  fetchRunActivities,
  fetchRunProgress,
  fetchRuns,
  fetchThreads,
  saveAgentPolicy,
  saveAgentTool,
  sendMessage,
} from '../api'
import type {
  ActivityEvent,
  AgentRole,
  AgentRuntimeCatalog,
  AgentToolType,
  ChatMessage,
  ChatThread,
  CreateRunRequest,
  GitHubBoardItemRef,
  GitHubBoardSnapshot,
  Mission,
  OllamaModelInfo,
  OverviewSnapshot,
  PatchProposal,
  ProgressLog,
  RepositoryRef,
  SprintRef,
  SwarmTemplate,
} from '../types'
import { buildIdleMission, buildIdleOverview, preferredModel, repoKey } from './view-models'

export type ApexConsoleState = ReturnType<typeof useApexConsole>

export function useApexConsole() {
  const fallback = useMemo(() => buildIdleOverview(), [])
  const [overview, setOverview] = useState<OverviewSnapshot>(fallback)
  const [runs, setRuns] = useState<Mission[]>([])
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null)
  const [selectedRun, setSelectedRun] = useState<Mission | null>(null)
  const [activities, setActivities] = useState<ActivityEvent[]>([])
  const [progressLogs, setProgressLogs] = useState<ProgressLog[]>([])
  const [title, setTitle] = useState('Stabilize swarm runtime and operator UI')
  const [objective, setObjective] = useState('Recover the product into a compact operator console with direct task entry, live swarm detail, and reliable patch review.')
  const [selectedSwarmTemplate, setSelectedSwarmTemplate] = useState<SwarmTemplate>('Hierarchical')
  const [busy, setBusy] = useState(false)
  const [chatBusy, setChatBusy] = useState(false)
  const [runtimeBusy, setRuntimeBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [connected, setConnected] = useState(false)
  const [repositories, setRepositories] = useState<RepositoryRef[]>([])
  const [selectedRepoKey, setSelectedRepoKey] = useState('')
  const [repoStatus, setRepoStatus] = useState('Loading repositories...')
  const [board, setBoard] = useState<GitHubBoardSnapshot | null>(null)
  const [sprints, setSprints] = useState<SprintRef[]>([])
  const [selectedSprintId, setSelectedSprintId] = useState('')
  const [selectedWorkItemId, setSelectedWorkItemId] = useState('')
  const [models, setModels] = useState<OllamaModelInfo[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [threads, setThreads] = useState<ChatThread[]>([])
  const [selectedThreadId, setSelectedThreadId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [runtimeCatalog, setRuntimeCatalog] = useState<AgentRuntimeCatalog>({ updatedAt: new Date().toISOString(), tools: [], policies: [] })
  const [selectedPolicyRole, setSelectedPolicyRole] = useState<AgentRole>('Frontend')
  const [policyDraft, setPolicyDraft] = useState({
    executionMode: 'ToolLoop' as 'StructuredPrompt' | 'ToolLoop',
    allowedTools: [] as string[],
    allowedDelegates: [] as AgentRole[],
    writableRoots: 'frontend\nsrc',
    maxSteps: 8,
  })
  const [toolForm, setToolForm] = useState({
    name: '',
    displayName: '',
    description: '',
    type: 'CustomCommand' as AgentToolType,
    enabled: true,
    destructive: false,
    commandTemplate: '',
  })

  const deferredActivities = useDeferredValue(activities)
  const deferredProgress = useDeferredValue(progressLogs)
  const activeRun = overview.activeRun ?? buildIdleMission(overview)
  const currentRun = selectedRun ?? (selectedRunId === activeRun.id ? activeRun : null) ?? activeRun
  const selectedRepository = useMemo(() => repositories.find((repo) => repoKey(repo) === selectedRepoKey) ?? null, [repositories, selectedRepoKey])
  const selectedSprint = useMemo(() => sprints.find((sprint) => sprint.id === selectedSprintId) ?? null, [selectedSprintId, sprints])
  const selectedWorkItem = useMemo(() => board?.items.find((item) => item.id === selectedWorkItemId) ?? null, [board?.items, selectedWorkItemId])
  const selectedThread = useMemo(() => threads.find((thread) => thread.id === selectedThreadId) ?? null, [threads, selectedThreadId])
  const chatModels = useMemo(() => {
    const filtered = models.filter((model) => !/embed/i.test(model.name))
    return filtered.length > 0 ? filtered : models
  }, [models])

  useEffect(() => {
    let active = true

    async function boot() {
      const [overviewResult, runsResult, repositoriesResult, modelsResult, threadsResult, runtimeResult] = await Promise.allSettled([
        fetchOverview(),
        fetchRuns(),
        fetchRepositories(),
        fetchModels(),
        fetchThreads(),
        fetchAgentRuntime(),
      ])

      if (!active) {
        return
      }

      startTransition(() => {
        if (overviewResult.status === 'fulfilled') {
          setOverview(overviewResult.value)
        } else {
          setError(overviewResult.reason instanceof Error ? overviewResult.reason.message : 'Overview could not be loaded.')
        }

        if (runsResult.status === 'fulfilled') {
          setRuns(runsResult.value)
        }

        if (repositoriesResult.status === 'fulfilled') {
          setRepositories(repositoriesResult.value)
          setRepoStatus(repositoriesResult.value.length > 0 ? `${repositoriesResult.value.length} repositories available` : 'No repositories available.')
        }

        if (modelsResult.status === 'fulfilled') {
          setModels(modelsResult.value)
          const fallbackModel = overviewResult.status === 'fulfilled' ? overviewResult.value.system.chatModel : fallback.system.chatModel
          setSelectedModel((current) => current || preferredModel(modelsResult.value, fallbackModel))
        }

        if (threadsResult.status === 'fulfilled') {
          setThreads(threadsResult.value)
          setSelectedThreadId((current) => current ?? threadsResult.value[0]?.id ?? null)
        }

        if (runtimeResult.status === 'fulfilled') {
          setRuntimeCatalog(runtimeResult.value)
        }
      })

      const nextSelectedRunId =
        (overviewResult.status === 'fulfilled' ? overviewResult.value.activeRun?.id ?? overviewResult.value.recentRuns[0]?.id : null)
        ?? (runsResult.status === 'fulfilled' ? runsResult.value[0]?.id ?? null : null)

      if (nextSelectedRunId) {
        setSelectedRunId(nextSelectedRunId)
      }
    }

    void boot()

    const connection = createRealtimeConnection({
      onActivity: (event) => {
        if (event.missionId !== selectedRunId) {
          return
        }

        startTransition(() => {
          setActivities((current) => [event, ...current].slice(0, 80))
        })
      },
      onProgress: (event) => {
        if (event.missionId !== selectedRunId) {
          return
        }

        startTransition(() => {
          setProgressLogs((current) => [...current, event].slice(-120))
        })
      },
    })

    void connection.start().then(() => setConnected(true)).catch(() => setConnected(false))

    const intervalId = window.setInterval(() => {
      void refreshOverview(setOverview, setRuns, setError)
    }, 6000)

    return () => {
      active = false
      window.clearInterval(intervalId)
      void connection.stop()
    }
  }, [fallback.system.chatModel, selectedRunId])

  useEffect(() => {
    const policy = runtimeCatalog.policies.find((item) => item.role === selectedPolicyRole)
    if (!policy) {
      return
    }

    setPolicyDraft({
      executionMode: policy.executionMode,
      allowedTools: [...policy.allowedTools],
      allowedDelegates: [...policy.allowedDelegates],
      writableRoots: policy.writableRoots.join('\n'),
      maxSteps: policy.maxSteps,
    })
  }, [runtimeCatalog, selectedPolicyRole])

  useEffect(() => {
    if (!selectedRunId) {
      setSelectedRun(null)
      setActivities([])
      setProgressLogs([])
      return
    }

    let active = true
    void Promise.allSettled([fetchRun(selectedRunId), fetchRunActivities(selectedRunId), fetchRunProgress(selectedRunId)]).then(([runResult, activityResult, progressResult]) => {
      if (!active) {
        return
      }

      if (runResult.status === 'fulfilled') {
        setSelectedRun(runResult.value)
      }

      if (activityResult.status === 'fulfilled') {
        setActivities(activityResult.value)
      }

      if (progressResult.status === 'fulfilled') {
        setProgressLogs(progressResult.value)
      }
    })

    return () => {
      active = false
    }
  }, [selectedRunId])

  useEffect(() => {
    if (!selectedRepository) {
      setBoard(null)
      setSprints([])
      setSelectedSprintId('')
      setSelectedWorkItemId('')
      return
    }

    let active = true
    void fetchRepositoryBoard(selectedRepository.owner, selectedRepository.name)
      .then((snapshot) => {
        if (!active) {
          return
        }

        setBoard(snapshot)
        setSprints(snapshot.sprints)
        setRepoStatus(snapshot.statusMessage || `${selectedRepository.fullName} ready`)
        setSelectedSprintId((current) => current && snapshot.sprints.some((item) => item.id === current) ? current : snapshot.sprints[0]?.id ?? '')
      })
      .catch((requestError) => {
        if (active) {
          setRepoStatus(requestError instanceof Error ? requestError.message : 'Board data could not be loaded.')
          setBoard(null)
          setSprints([])
        }
      })

    return () => {
      active = false
    }
  }, [selectedRepository])

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

  async function handleCreateRun() {
    setBusy(true)
    setError(null)
    try {
      const request: CreateRunRequest = {
        title: title.trim(),
        objective: objective.trim(),
        selectedRepository,
        selectedSprint,
        selectedWorkItem,
        swarmTemplate: selectedSwarmTemplate,
        autoCreatePullRequest: true,
      }

      const run = await createRun(request)
      setSelectedRunId(run.id)
      await refreshOverview(setOverview, setRuns, setError)
      setSelectedRun(run)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Run could not be created.')
    } finally {
      setBusy(false)
    }
  }

  async function handleDispatchWorkItem(workItem: GitHubBoardItemRef) {
    const sprintForItem = sprints.find((item) => item.id === workItem.sprintId) ?? selectedSprint ?? null
    setSelectedWorkItemId(workItem.id)
    setSelectedSprintId(workItem.sprintId)
    setTitle(workItem.title)
    setObjective(buildWorkItemObjective(workItem, selectedRepository, sprintForItem))
    await handleCreateRunWithDraft({
      title: workItem.title,
      objective: buildWorkItemObjective(workItem, selectedRepository, sprintForItem),
      selectedRepository,
      selectedSprint: sprintForItem,
      selectedWorkItem: workItem,
      swarmTemplate: selectedSwarmTemplate,
      autoCreatePullRequest: true,
    })
  }

  async function handleCreateRunWithDraft(request: CreateRunRequest) {
    setBusy(true)
    setError(null)
    try {
      const run = await createRun(request)
      setSelectedRunId(run.id)
      setSelectedRun(run)
      await refreshOverview(setOverview, setRuns, setError)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Run could not be created.')
    } finally {
      setBusy(false)
    }
  }

  async function handleSelectRun(runId: string) {
    setSelectedRunId(runId)
  }

  async function handleArchiveRun(runId = currentRun.id) {
    if (!runId || runId === 'idle-run') {
      return
    }

    setBusy(true)
    setError(null)
    try {
      await archiveRun(runId)
      if (selectedRunId === runId) {
        setSelectedRunId(null)
        setSelectedRun(null)
      }

      await refreshOverview(setOverview, setRuns, setError)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Run could not be archived.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCancelRun(runId = currentRun.id) {
    if (!runId || runId === 'idle-run') {
      return
    }

    setBusy(true)
    setError(null)
    try {
      const run = await cancelRun(runId)
      setSelectedRun(run)
      await refreshOverview(setOverview, setRuns, setError)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Run could not be cancelled.')
    } finally {
      setBusy(false)
    }
  }

  async function handlePatchDecision(proposal: PatchProposal, action: 'approve' | 'reject') {
    try {
      await decidePatch(proposal.id, action)
      if (selectedRunId) {
        const [run, runActivities, runProgress] = await Promise.all([
          fetchRun(selectedRunId),
          fetchRunActivities(selectedRunId),
          fetchRunProgress(selectedRunId),
        ])
        setSelectedRun(run)
        setActivities(runActivities)
        setProgressLogs(runProgress)
      }

      await refreshOverview(setOverview, setRuns, setError)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Patch decision could not be saved.')
    }
  }

  async function handleNewThread() {
    try {
      const thread = await createThread(selectedModel || overview.system.chatModel)
      setThreads((current) => [thread, ...current.filter((item) => item.id !== thread.id)])
      setSelectedThreadId(thread.id)
      setMessages([])
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Chat thread could not be created.')
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
        const thread = await createThread(selectedModel || overview.system.chatModel)
        threadId = thread.id
        setThreads((current) => [thread, ...current.filter((item) => item.id !== thread.id)])
        setSelectedThreadId(thread.id)
      }

      const result = await sendMessage(threadId, content, selectedModel || overview.system.chatModel)
      setChatInput('')
      setThreads((current) => [result.thread, ...current.filter((item) => item.id !== result.thread.id)])
      setMessages((current) => [...current, result.userMessage, result.assistantMessage])
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Message could not be sent.')
    } finally {
      setChatBusy(false)
    }
  }

  async function refreshRuntimeCatalog() {
    const catalog = await fetchAgentRuntime()
    setRuntimeCatalog(catalog)
  }

  async function handleSaveTool() {
    if (!toolForm.name.trim()) {
      setError('Tool name is required.')
      return
    }

    if (toolForm.type === 'CustomCommand' && !toolForm.commandTemplate.trim()) {
      setError('Custom command tools require a command template.')
      return
    }

    setRuntimeBusy(true)
    setError(null)
    try {
      await saveAgentTool({
        ...toolForm,
        name: toolForm.name.trim(),
        displayName: toolForm.displayName.trim(),
        description: toolForm.description.trim(),
        commandTemplate: toolForm.commandTemplate.trim() || null,
      })
      await refreshRuntimeCatalog()
      setToolForm({
        name: '',
        displayName: '',
        description: '',
        type: 'CustomCommand',
        enabled: true,
        destructive: false,
        commandTemplate: '',
      })
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Tool could not be saved.')
    } finally {
      setRuntimeBusy(false)
    }
  }

  async function handleSavePolicy() {
    setRuntimeBusy(true)
    setError(null)
    try {
      await saveAgentPolicy(selectedPolicyRole, {
        executionMode: policyDraft.executionMode,
        allowedTools: policyDraft.allowedTools,
        allowedDelegates: policyDraft.allowedDelegates,
        writableRoots: policyDraft.writableRoots
          .split(/\r?\n|,/)
          .map((item) => item.trim())
          .filter(Boolean),
        maxSteps: Number(policyDraft.maxSteps) || 1,
      })
      await refreshRuntimeCatalog()
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Policy could not be saved.')
    } finally {
      setRuntimeBusy(false)
    }
  }

  function togglePolicyTool(toolName: string) {
    setPolicyDraft((current) => ({
      ...current,
      allowedTools: current.allowedTools.includes(toolName)
        ? current.allowedTools.filter((item) => item !== toolName)
        : [...current.allowedTools, toolName].sort(),
    }))
  }

  function togglePolicyDelegate(role: AgentRole) {
    setPolicyDraft((current) => ({
      ...current,
      allowedDelegates: current.allowedDelegates.includes(role)
        ? current.allowedDelegates.filter((item) => item !== role)
        : [...current.allowedDelegates, role].sort(),
    }))
  }

  return {
    activities,
    board,
    busy,
    chatBusy,
    chatInput,
    chatModels,
    connected,
    currentRun,
    deferredActivities,
    deferredProgress,
    error,
    messages,
    models,
    objective,
    overview,
    progressLogs,
    repoStatus,
    repositories,
    runs,
    runtimeBusy,
    runtimeCatalog,
    selectedModel,
    selectedPolicyRole,
    selectedRepoKey,
    selectedRepository,
    selectedRun,
    selectedRunId,
    selectedSprint,
    selectedSprintId,
    selectedSwarmTemplate,
    selectedThread,
    selectedThreadId,
    selectedWorkItem,
    selectedWorkItemId,
    policyDraft,
    sprints,
    threads,
    title,
    toolForm,
    setChatInput,
    setObjective,
    setPolicyDraft,
    setSelectedModel,
    setSelectedPolicyRole,
    setSelectedRepoKey,
    setSelectedRunId,
    setSelectedSprintId,
    setSelectedSwarmTemplate,
    setSelectedThreadId,
    setSelectedWorkItemId,
    setTitle,
    setToolForm,
    handleArchiveRun,
    handleCancelRun,
    handleCreateRun,
    handleDispatchWorkItem,
    handleNewThread,
    handlePatchDecision,
    handleSavePolicy,
    handleSaveTool,
    handleSelectRun,
    handleSendMessage,
    togglePolicyDelegate,
    togglePolicyTool,
  }
}

async function refreshOverview(
  setOverview: Dispatch<SetStateAction<OverviewSnapshot>>,
  setRuns: Dispatch<SetStateAction<Mission[]>>,
  setError: Dispatch<SetStateAction<string | null>>,
) {
  try {
    const [overviewResult, runsResult] = await Promise.all([fetchOverview(), fetchRuns()])
    startTransition(() => {
      setOverview(overviewResult)
      setRuns(runsResult)
    })
  } catch (requestError) {
    setError(requestError instanceof Error ? requestError.message : 'Overview could not be refreshed.')
  }
}

function buildWorkItemObjective(workItem: GitHubBoardItemRef, repository: RepositoryRef | null, sprint: SprintRef | null) {
  const subtasks = workItem.subtasks.length > 0
    ? workItem.subtasks.map((item) => `- ${item}`).join('\n')
    : '- Deliver the issue using the repository context.'

  return [
    `Complete the selected board item: ${workItem.title}.`,
    repository ? `Repository: ${repository.fullName}.` : 'Repository not selected.',
    sprint ? `Sprint: ${sprint.title}.` : 'Sprint not selected.',
    `Status lane: ${workItem.status}.`,
    workItem.description ? `Description:\n${workItem.description}` : 'No additional description.',
    `Checklist:\n${subtasks}`,
    'Keep the work direct, patch-focused, and reviewable.',
  ].join('\n\n')
}
