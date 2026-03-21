import { motion } from 'framer-motion'
import { startTransition, useDeferredValue, useEffect, useMemo, useState, type Dispatch, type SetStateAction } from 'react'
import {
  createMission,
  createRealtimeConnection,
  createThread,
  decidePatch,
  fetchDashboard,
  fetchMessages,
  fetchMilestones,
  fetchModels,
  fetchProgress,
  fetchRepositories,
  fetchThreads,
  sendMessage,
} from './api'
import type {
  ActivityEvent,
  AgentRole,
  AgentSnapshot,
  ChatMessage,
  ChatThread,
  DashboardSnapshot,
  Mission,
  OllamaModelInfo,
  PatchProposal,
  ProgressLog,
  RepositoryRef,
  SprintRef,
} from './types'

type ViewKey = 'agentTeam' | 'workflows' | 'mcpServers' | 'chat'

type MeshRole = 'Manager' | 'WebDev' | 'Frontend' | 'Backend' | 'Tester'

const navItems: Array<{ key: ViewKey; label: string; short: string }> = [
  { key: 'agentTeam', label: 'Agent Team', short: 'AT' },
  { key: 'workflows', label: 'Workflows', short: 'WF' },
  { key: 'mcpServers', label: 'MCP Servers', short: 'MCP' },
  { key: 'chat', label: 'Chat', short: 'CH' },
]

const meshNodes: Record<MeshRole, { x: number; y: number; theme: string; caption: string }> = {
  Manager: { x: 50, y: 34, theme: 'cyan', caption: 'Coordinator' },
  WebDev: { x: 24, y: 48, theme: 'violet', caption: 'Architect' },
  Frontend: { x: 34, y: 74, theme: 'green', caption: 'UI/UX' },
  Backend: { x: 66, y: 74, theme: 'amber', caption: 'API' },
  Tester: { x: 76, y: 48, theme: 'rose', caption: 'QA' },
}

const meshEdges: Array<{ from: MeshRole; to: MeshRole }> = [
  { from: 'Manager', to: 'WebDev' },
  { from: 'Manager', to: 'Frontend' },
  { from: 'Manager', to: 'Backend' },
  { from: 'Manager', to: 'Tester' },
  { from: 'WebDev', to: 'Frontend' },
  { from: 'WebDev', to: 'Backend' },
  { from: 'Frontend', to: 'Backend' },
  { from: 'Backend', to: 'Tester' },
]

const sidebarOrder: AgentRole[] = ['Manager', 'Analyst', 'WebDev', 'Frontend', 'Backend', 'Tester', 'PM', 'Support']

export default function App() {
  const [view, setView] = useState<ViewKey>('agentTeam')
  const [sidebarOpen, setSidebarOpen] = useState(true)
  const [dashboard, setDashboard] = useState<DashboardSnapshot>(buildFallbackDashboard())
  const [title, setTitle] = useState('Sprint Build')
  const [prompt, setPrompt] = useState('Convert the APEX knowledge base into a local-first .NET multi-agent software team with Mongo progress logs, GitHub sprint selection, and Ollama chat.')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [connected, setConnected] = useState(false)
  const [repositories, setRepositories] = useState<RepositoryRef[]>([])
  const [selectedRepoKey, setSelectedRepoKey] = useState('')
  const [sprints, setSprints] = useState<SprintRef[]>([])
  const [selectedSprintId, setSelectedSprintId] = useState('')
  const [progressLogs, setProgressLogs] = useState<ProgressLog[]>(buildFallbackDashboard().recentProgressLogs)
  const [models, setModels] = useState<OllamaModelInfo[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [threads, setThreads] = useState<ChatThread[]>([])
  const [selectedThreadId, setSelectedThreadId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [chatBusy, setChatBusy] = useState(false)

  const deferredActivities = useDeferredValue(dashboard.recentActivities)
  const deferredProgress = useDeferredValue(progressLogs)
  const mission = dashboard.activeMission ?? buildFallbackDashboard().activeMission!
  const agentMap = new Map(dashboard.agents.map((agent) => [agent.role, agent]))
  const changedFiles = Array.from(new Set((mission.patchProposals ?? []).flatMap((proposal) => proposal.targetPaths))).slice(0, 8)
  const selectedRepository = useMemo(() => repositories.find((repo) => repoKey(repo) === selectedRepoKey) ?? null, [repositories, selectedRepoKey])
  const selectedSprint = useMemo(() => sprints.find((sprint) => String(sprint.id) === selectedSprintId) ?? null, [selectedSprintId, sprints])
  const chatModels = useMemo(() => {
    const filtered = models.filter((model) => !/embed/i.test(model.name))
    return filtered.length > 0 ? filtered : models
  }, [models])
  const activeMeshFlow = useMemo(() => {
    return [...deferredProgress].reverse().find((item) => {
      const fromRole = item.metadata.fromRole as MeshRole | undefined
      const toRole = item.metadata.toRole as MeshRole | undefined
      return Boolean(fromRole && toRole && meshNodes[fromRole] && meshNodes[toRole])
    })
  }, [deferredProgress])

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

        if (!active) return

        startTransition(() => {
          setDashboard(snapshot)
          setProgressLogs(snapshot.recentProgressLogs.length > 0 ? snapshot.recentProgressLogs : buildFallbackDashboard().recentProgressLogs)
          setRepositories(repoList)
          setModels(modelList)
          setThreads(threadList)
          setSelectedThreadId((current) => current ?? threadList[0]?.id ?? null)
          setSelectedModel((current) => current || preferredModel(modelList, snapshot.chatModel))
        })
      } catch (requestError) {
        if (active) {
          setError(requestError instanceof Error ? requestError.message : 'Initial load failed.')
        }
      }
    }

    void boot()

    const connection = createRealtimeConnection({
      onActivity: (event) => {
        startTransition(() => {
          setDashboard((current) => ({
            ...current,
            recentActivities: [...current.recentActivities.slice(-29), event],
          }))
        })
      },
      onProgress: (event) => {
        startTransition(() => {
          setProgressLogs((current) => [...current.slice(-49), event])
        })
      },
    })

    void connection.start().then(() => setConnected(true)).catch(() => setConnected(false))

    const intervalId = window.setInterval(() => {
      void refreshDashboard(active, setDashboard, setProgressLogs, setError)
    }, 6000)

    return () => {
      active = false
      window.clearInterval(intervalId)
      void connection.stop()
    }
  }, [])

  useEffect(() => {
    const activeMission = dashboard.activeMission
    if (!activeMission) return

    if (activeMission.selectedRepository) {
      setSelectedRepoKey(repoKey(activeMission.selectedRepository))
    }

    if (activeMission.selectedSprint) {
      setSelectedSprintId(String(activeMission.selectedSprint.id))
    }

    if (activeMission.id) {
      void fetchProgress(activeMission.id)
        .then((items) => setProgressLogs(items.length > 0 ? items : buildFallbackDashboard().recentProgressLogs))
        .catch(() => undefined)
    }
  }, [dashboard.activeMission?.id])

  useEffect(() => {
    if (!selectedRepository) {
      setSprints([])
      setSelectedSprintId('')
      return
    }

    let active = true
    void fetchMilestones(selectedRepository.owner, selectedRepository.name)
      .then((items) => {
        if (!active) return
        setSprints(items)
        setSelectedSprintId((current) => (current && items.some((item) => String(item.id) === current) ? current : String(items[0]?.id ?? '')))
      })
      .catch(() => {
        if (active) {
          setSprints([])
          setSelectedSprintId('')
        }
      })

    return () => {
      active = false
    }
  }, [selectedRepository?.owner, selectedRepository?.name])

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
      setError(requestError instanceof Error ? requestError.message : 'Mission creation failed.')
    } finally {
      setBusy(false)
    }
  }

  async function handlePatchDecision(proposal: PatchProposal, action: 'approve' | 'reject') {
    try {
      await decidePatch(proposal.id, action)
      await refreshDashboard(true, setDashboard, setProgressLogs, setError)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Patch decision failed.')
    }
  }

  async function handleNewThread() {
    try {
      const thread = await createThread(selectedModel || dashboard.chatModel)
      setThreads((current) => [thread, ...current.filter((item) => item.id !== thread.id)])
      setSelectedThreadId(thread.id)
      setMessages([])
      setView('chat')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Thread creation failed.')
    }
  }

  async function handleSendMessage() {
    const content = chatInput.trim()
    if (!content || chatBusy) return

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
      setError(requestError instanceof Error ? requestError.message : 'Chat message failed.')
    } finally {
      setChatBusy(false)
    }
  }

  return (
    <div className={`app-shell ${sidebarOpen ? 'sidebar-open' : 'sidebar-closed'}`}>
      <div className="matrix-rain" aria-hidden="true">
        {Array.from({ length: 16 }).map((_, index) => (
          <span key={index} style={{ left: `${index * 6.2}%`, animationDelay: `${index * 0.35}s` }}>
            agent team orchestration mongodb qdrant ollama github milestones patch review live progress
          </span>
        ))}
      </div>

      <aside className={`sidebar ${sidebarOpen ? '' : 'collapsed'}`}>
        <button type="button" className="hamburger" onClick={() => setSidebarOpen((current) => !current)}>
          <span />
          <span />
          <span />
        </button>

        <div className="sidebar-brand">
          <strong>{sidebarOpen ? 'APEX Team' : 'AT'}</strong>
          <small>{sidebarOpen ? 'Local control room' : 'v2'}</small>
        </div>

        <nav className="sidebar-nav">
          {navItems.map((item) => (
            <button key={item.key} type="button" className={`nav-item ${view === item.key ? 'active' : ''}`} onClick={() => setView(item.key)}>
              <span className="nav-item__badge">{item.short}</span>
              {sidebarOpen && <span>{item.label}</span>}
            </button>
          ))}
        </nav>

        {sidebarOpen && (
          <div className="sidebar-footer">
            <span>Queue {dashboard.logicalQueueDepth}</span>
            <span>{connected ? 'SignalR live' : 'Polling'}</span>
          </div>
        )}
      </aside>

      <main className="workspace">
        <header className="workspace-topbar">
          <div>
            <p className="eyebrow">AGENT TEAM &gt; local orchestration &gt; sprint console</p>
            <h1>{viewTitle(view)}</h1>
            <p className="subline">{mission.currentPhase ?? mission.status} • model {selectedModel || dashboard.chatModel}</p>
          </div>
          <div className="metric-row">
            <MetricCard label="Queue" value={String(dashboard.logicalQueueDepth)} />
            <MetricCard label="Workers" value={String(dashboard.physicalWorkerCount)} />
            <MetricCard label="Stream" value={connected ? 'LIVE' : 'POLL'} accent={connected ? 'good' : 'warn'} />
          </div>
        </header>

        {view === 'agentTeam' ? (
          <div className="agent-team-layout">
            <section className="command-deck">
              <section className="panel mission-panel">
                <div className="panel-heading">
                  <span className="panel-kicker">Mission Setup</span>
                  <span>{mission.status}</span>
                </div>

                <div className="selector-grid">
                  <label>
                    <span>Repository</span>
                    <select value={selectedRepoKey} onChange={(event) => setSelectedRepoKey(event.target.value)}>
                      <option value="">Select repo</option>
                      {repositories.map((repo) => (
                        <option key={repoKey(repo)} value={repoKey(repo)}>
                          {repo.fullName}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <span>Sprint</span>
                    <select value={selectedSprintId} onChange={(event) => setSelectedSprintId(event.target.value)} disabled={sprints.length === 0}>
                      <option value="">Select sprint</option>
                      {sprints.map((sprint) => (
                        <option key={sprint.id} value={String(sprint.id)}>
                          #{sprint.number} {sprint.title}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>

                <div className="composer-grid slim">
                  <label>
                    <span>Mission title</span>
                    <input value={title} onChange={(event) => setTitle(event.target.value)} />
                  </label>
                  <label className="prompt-field">
                    <span>Prompt</span>
                    <textarea value={prompt} onChange={(event) => setPrompt(event.target.value)} rows={4} />
                  </label>
                </div>

                <div className="composer-actions compact-actions">
                  <button type="button" onClick={handleCreateMission} disabled={busy}>
                    {busy ? 'Dispatching...' : 'Dispatch mission'}
                  </button>
                  <p>{error ?? (selectedRepository ? `${selectedRepository.fullName}${selectedSprint ? ` • ${selectedSprint.title}` : ''}` : 'Choose repo and sprint, then dispatch.')}</p>
                </div>
              </section>

              <section className="panel mesh-panel">
                <div className="panel-heading">
                  <span className="panel-kicker">Coordination Mesh</span>
                  <span>{activeMeshFlow?.metadata.toRole ?? 'idle'}</span>
                </div>

                <div className="network-surface">
                  <svg className="mesh-lines" viewBox="0 0 100 100" preserveAspectRatio="none">
                    {meshEdges.map((edge) => {
                      const from = meshNodes[edge.from]
                      const to = meshNodes[edge.to]
                      const isActive = activeMeshFlow?.metadata.fromRole === edge.from && activeMeshFlow?.metadata.toRole === edge.to
                      return (
                        <g key={`${edge.from}-${edge.to}`}>
                          <line x1={from.x} y1={from.y} x2={to.x} y2={to.y} className={isActive ? 'active' : ''} />
                          {isActive ? (
                            <motion.circle
                              r="1.15"
                              fill="rgba(89,229,255,0.95)"
                              initial={{ cx: from.x, cy: from.y, opacity: 0 }}
                              animate={{ cx: [from.x, to.x], cy: [from.y, to.y], opacity: [0, 1, 1, 0] }}
                              transition={{ duration: 1.35, repeat: Number.POSITIVE_INFINITY, ease: 'linear' }}
                            />
                          ) : null}
                        </g>
                      )
                    })}
                  </svg>

                  {(Object.keys(meshNodes) as MeshRole[]).map((role, index) => {
                    const node = meshNodes[role]
                    const agent = agentMap.get(role) ?? buildFallbackDashboard().agents.find((item) => item.role === role)!
                    return (
                      <motion.div
                        key={role}
                        className={`agent-node ${node.theme} ${agent.status.toLowerCase()} ${activeMeshFlow?.metadata.toRole === role ? 'focused' : ''}`}
                        style={{ left: `${node.x}%`, top: `${node.y}%` }}
                        initial={{ opacity: 0, scale: 0.82 }}
                        animate={{ opacity: 1, scale: 1 }}
                        transition={{ delay: index * 0.08, duration: 0.42 }}
                      >
                        <div className="agent-node__halo" />
                        <div className="agent-node__core">
                          <strong>{role}</strong>
                          <span>{node.caption}</span>
                        </div>
                        <small>{agent.detail ?? readableAgentStatus(agent)}</small>
                      </motion.div>
                    )
                  })}
                </div>
              </section>

              <section className="bottom-panels">
                <section className="panel activity-panel">
                  <div className="panel-heading">
                    <span className="panel-kicker">Live Activity</span>
                    <span>{deferredActivities.length}</span>
                  </div>
                  <div className="activity-list fixed-scroll">
                    {deferredActivities.map((event, index) => (
                      <motion.div key={event.id} className="activity-row" initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.02 }}>
                        <time>{formatTime(event.createdAt)}</time>
                        <span className="activity-role">{event.agentRole ?? 'SYS'}</span>
                        <div>
                          <strong>{event.summary}</strong>
                          <p>{event.details}</p>
                        </div>
                      </motion.div>
                    ))}
                  </div>
                </section>

                <section className="panel terminal-panel">
                  <div className="panel-heading">
                    <span className="panel-kicker">Progress Terminal</span>
                    <span>{progressLogs.length}</span>
                  </div>
                  <div className="terminal-window fixed-scroll">
                    <pre>{deferredProgress.slice(-12).map((item) => `$ ${item.role ?? 'SYS'} :: ${item.stage}\n> ${item.message}`).join('\n\n')}</pre>
                  </div>
                </section>
              </section>
            </section>

            <aside className="side-rail">
              <section className="panel rail-panel">
                <div className="panel-heading">
                  <span className="panel-kicker">Agents</span>
                  <span>{dashboard.agents.length}</span>
                </div>
                <div className="agent-list fixed-scroll">
                  {sidebarOrder.map((role) => {
                    const agent = agentMap.get(role) ?? buildFallbackDashboard().agents.find((item) => item.role === role)!
                    return (
                      <div key={role} className="agent-row compact-row">
                        <div>
                          <strong>{role}</strong>
                          <span>{agent.detail ?? readableAgentStatus(agent)}</span>
                        </div>
                        <span className={`status-chip ${toneByRunStatus(agent.status)}`}>{agent.status}</span>
                      </div>
                    )
                  })}
                </div>
              </section>

              <section className="panel rail-panel">
                <div className="panel-heading">
                  <span className="panel-kicker">Tasks</span>
                  <span>{mission.steps.length}</span>
                </div>
                <div className="task-list fixed-scroll">
                  {mission.steps.map((step) => (
                    <div key={step.id} className="task-row compact-row">
                      <span className={`task-check ${step.status.toLowerCase()}`} />
                      <div>
                        <strong>{step.title}</strong>
                        <p>{step.owner} • {step.summary}</p>
                      </div>
                      <span className={`status-chip ${statusTone(step.status)}`}>{step.status}</span>
                    </div>
                  ))}
                </div>
              </section>

              <section className="panel rail-panel">
                <div className="panel-heading">
                  <span className="panel-kicker">Patch Review</span>
                  <span>{mission.patchProposals.length}</span>
                </div>
                <div className="patch-list fixed-scroll">
                  {mission.patchProposals.map((proposal) => (
                    <div key={proposal.id} className="patch-card">
                      <div className="patch-card__head">
                        <strong>{proposal.authorRole}</strong>
                        <span className={`status-chip ${statusTone(proposal.status)}`}>{proposal.status}</span>
                      </div>
                      <p>{proposal.summary}</p>
                      <code>{proposal.targetPaths.join(', ')}</code>
                      <div className="patch-actions">
                        <button type="button" onClick={() => handlePatchDecision(proposal, 'approve')} disabled={proposal.status !== 'PendingReview'}>
                          Approve
                        </button>
                        <button type="button" onClick={() => handlePatchDecision(proposal, 'reject')} disabled={proposal.status !== 'PendingReview'} className="ghost">
                          Reject
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </section>

              <section className="panel rail-panel compact">
                <div className="panel-heading">
                  <span className="panel-kicker">Changed Files</span>
                  <span>{changedFiles.length}</span>
                </div>
                <div className="file-list fixed-scroll">
                  {changedFiles.map((file) => (
                    <code key={file}>{file}</code>
                  ))}
                </div>
              </section>
            </aside>
          </div>
        ) : null}

        {view === 'workflows' ? <WorkflowPlaceholder /> : null}
        {view === 'mcpServers' ? <McpPlaceholder /> : null}

        {view === 'chat' ? (
          <div className="chat-layout">
            <aside className="panel thread-panel">
              <div className="panel-heading">
                <span className="panel-kicker">Threads</span>
                <button type="button" className="mini-button" onClick={handleNewThread}>New</button>
              </div>
              <div className="thread-list fixed-scroll">
                {threads.map((thread) => (
                  <button key={thread.id} type="button" className={`thread-row ${thread.id === selectedThreadId ? 'active' : ''}`} onClick={() => setSelectedThreadId(thread.id)}>
                    <strong>{thread.title}</strong>
                    <span>{thread.model}</span>
                  </button>
                ))}
              </div>
            </aside>

            <section className="panel chat-panel">
              <div className="chat-toolbar">
                <div>
                  <span className="panel-kicker">Local Chat</span>
                  <strong>{selectedThreadId ? threads.find((item) => item.id === selectedThreadId)?.title ?? 'Chat' : 'New chat'}</strong>
                </div>
                <label className="model-select">
                  <span>Model</span>
                  <select value={selectedModel} onChange={(event) => setSelectedModel(event.target.value)}>
                    {chatModels.map((model) => (
                      <option key={model.name} value={model.name}>{model.name}</option>
                    ))}
                  </select>
                </label>
              </div>

              <div className="chat-stream fixed-scroll">
                {messages.length === 0 ? (
                  <div className="empty-chat">
                    <strong>Ready for local chat</strong>
                    <p>Pick a model, open a thread, and start talking to your Ollama runtime.</p>
                  </div>
                ) : (
                  messages.map((message) => (
                    <div key={message.id} className={`chat-bubble ${message.role}`}>
                      <span>{message.role}</span>
                      <p>{message.content}</p>
                    </div>
                  ))
                )}
              </div>

              <div className="chat-composer">
                <textarea value={chatInput} onChange={(event) => setChatInput(event.target.value)} rows={3} placeholder="Write to your local model..." />
                <div className="composer-actions compact-actions">
                  <button type="button" onClick={handleSendMessage} disabled={chatBusy}>{chatBusy ? 'Sending...' : 'Send'}</button>
                  <p>{selectedModel || dashboard.chatModel}</p>
                </div>
              </div>
            </section>
          </div>
        ) : null}
      </main>
    </div>
  )
}

function WorkflowPlaceholder() {
  return (
    <div className="placeholder-layout panel">
      <div className="panel-heading">
        <span className="panel-kicker">Workflows</span>
        <span>n8n-style canvas</span>
      </div>
      <div className="placeholder-canvas workflows-canvas">
        {['Trigger', 'Planner', 'Agent Fan-out', 'Review Gate', 'Deploy'].map((item, index) => (
          <motion.div key={item} className="workflow-card" initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.08 }}>
            <strong>{item}</strong>
            <span>Placeholder block</span>
          </motion.div>
        ))}
      </div>
    </div>
  )
}

function McpPlaceholder() {
  return (
    <div className="placeholder-layout panel">
      <div className="panel-heading">
        <span className="panel-kicker">MCP Servers</span>
        <span>ready for next phase</span>
      </div>
      <div className="mcp-grid">
        {['filesystem', 'git', 'github', 'postgres', 'browser'].map((item) => (
          <div key={item} className="mcp-card">
            <strong>{item}</strong>
            <p>Connection, auth and tool registry will land here.</p>
          </div>
        ))}
      </div>
    </div>
  )
}

async function refreshDashboard(
  active: boolean,
  setDashboard: Dispatch<SetStateAction<DashboardSnapshot>>,
  setProgressLogs: Dispatch<SetStateAction<ProgressLog[]>>,
  setError: Dispatch<SetStateAction<string | null>>,
) {
  try {
    const snapshot = await fetchDashboard()
    if (!active) return
    startTransition(() => {
      setDashboard((current) => ({ ...current, ...snapshot }))
      setProgressLogs(snapshot.recentProgressLogs.length > 0 ? snapshot.recentProgressLogs : buildFallbackDashboard().recentProgressLogs)
    })
  } catch (requestError) {
    if (active) {
      setError(requestError instanceof Error ? requestError.message : 'Dashboard load failed.')
    }
  }
}

function MetricCard({ label, value, accent = 'neutral' }: { label: string; value: string; accent?: 'neutral' | 'good' | 'warn' }) {
  return (
    <div className={`metric-card ${accent}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function preferredModel(models: OllamaModelInfo[], fallback: string) {
  return models.find((item) => item.name === fallback)?.name ?? models.find((item) => !/embed/i.test(item.name))?.name ?? fallback
}

function repoKey(repository: RepositoryRef) {
  return `${repository.owner}/${repository.name}`
}

function viewTitle(view: ViewKey) {
  if (view === 'agentTeam') return 'Agent Team'
  if (view === 'workflows') return 'Workflows'
  if (view === 'mcpServers') return 'MCP Servers'
  return 'Local Chat'
}

function readableAgentStatus(agent: AgentSnapshot) {
  return agent.status === 'Idle' ? 'Standing by' : agent.status
}

function statusTone(status: string) {
  const lower = status.toLowerCase()
  if (lower.includes('completed') || lower.includes('applied') || lower.includes('live')) return 'good'
  if (lower.includes('failed') || lower.includes('rejected') || lower.includes('error')) return 'bad'
  if (lower.includes('pending') || lower.includes('queued') || lower.includes('awaiting')) return 'warn'
  return 'neutral'
}

function toneByRunStatus(status: AgentSnapshot['status']) {
  if (status === 'Completed') return 'good'
  if (status === 'Error') return 'bad'
  if (status === 'Idle' || status === 'Waiting') return 'neutral'
  return 'warn'
}

function formatTime(value: string) {
  const date = new Date(value)
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function buildFallbackDashboard(): DashboardSnapshot {
  const now = new Date().toISOString()
  const repository: RepositoryRef = { owner: 'local', name: 'apex', fullName: 'local/apex', defaultBranch: 'main' }
  const sprint: SprintRef = { id: 12, title: 'Control Room', number: 12, state: 'open', dueOn: null }
  const agents: AgentSnapshot[] = [
    { role: 'Manager', status: 'Delegating', label: 'Coordinator', detail: 'Routing the sprint', updatedAt: now, queueDepth: 2 },
    { role: 'Analyst', status: 'Thinking', label: 'Requirements', detail: 'Refining criteria', updatedAt: now, queueDepth: 2 },
    { role: 'WebDev', status: 'Thinking', label: 'Architect', detail: 'Defining contract', updatedAt: now, queueDepth: 2 },
    { role: 'Frontend', status: 'Coding', label: 'UI/UX', detail: 'Aligning mesh layout', updatedAt: now, queueDepth: 2 },
    { role: 'Backend', status: 'Coding', label: 'API', detail: 'Logging progress to Mongo', updatedAt: now, queueDepth: 2 },
    { role: 'Tester', status: 'Reviewing', label: 'QA', detail: 'Waiting on patches', updatedAt: now, queueDepth: 2 },
    { role: 'PM', status: 'Thinking', label: 'Roadmap', detail: 'Drafting sprint note', updatedAt: now, queueDepth: 2 },
    { role: 'Support', status: 'Waiting', label: 'Support', detail: 'Ready for user summary', updatedAt: now, queueDepth: 2 },
  ]

  return {
    activeMission: {
      id: 'fallback-mission',
      title: 'Sprint Build',
      prompt: 'Coordinate a local-first .NET multi-agent team with sidebar navigation, chat, and persistent logs.',
      status: 'Running',
      createdAt: now,
      updatedAt: now,
      currentPhase: 'Delegation and review',
      selectedRepository: repository,
      selectedSprint: sprint,
      externalTask: { provider: 'github', externalId: 'draft', title: 'Analyst backlog handoff', status: 'Draft', url: null },
      steps: [
        { id: '1', title: 'Analyst workstream', owner: 'Analyst', status: 'Completed', order: 1, summary: 'Define acceptance criteria.', dependencies: [] },
        { id: '2', title: 'WebDev workstream', owner: 'WebDev', status: 'Completed', order: 2, summary: 'Define repo and UI contract.', dependencies: ['1'] },
        { id: '3', title: 'Frontend workstream', owner: 'Frontend', status: 'InProgress', order: 3, summary: 'Refresh shell and mesh alignment.', dependencies: ['2'] },
        { id: '4', title: 'Backend workstream', owner: 'Backend', status: 'InProgress', order: 4, summary: 'Persist progress and chat.', dependencies: ['2'] },
        { id: '5', title: 'Tester workstream', owner: 'Tester', status: 'Pending', order: 5, summary: 'Validate keep or revert.', dependencies: ['3', '4'] },
      ],
      patchProposals: [
        { id: 'patch-a', missionId: 'fallback-mission', authorRole: 'Frontend', title: 'Frontend control room', summary: 'Sidebar, mesh and chat shell update.', status: 'PendingReview', targetPaths: ['frontend/src/App.tsx', 'frontend/src/styles.css'], diff: 'diff --git a/frontend/src/App.tsx b/frontend/src/App.tsx', createdAt: now, updatedAt: null },
      ],
      agents,
      artifacts: { Analyst: 'Criteria extracted', Backend: 'Mongo logging wired' },
    },
    agents,
    recentActivities: [
      { id: 'evt-1', missionId: 'fallback-mission', createdAt: now, eventType: 'MissionCreated', agentRole: null, summary: 'Mission dispatched.', details: 'The team is assembling around the selected repo and sprint.' },
      { id: 'evt-2', missionId: 'fallback-mission', createdAt: now, eventType: 'AgentOutput', agentRole: 'WebDev', summary: 'WebDev set the contract.', details: 'Sidebar navigation, mesh alignment and Mongo telemetry were prioritized.' },
      { id: 'evt-3', missionId: 'fallback-mission', createdAt: now, eventType: 'PatchProposed', agentRole: 'Frontend', summary: 'Frontend patch proposed.', details: 'Viewport-locked dashboard and animated handoff edges are ready.' },
    ],
    recentProgressLogs: [
      { id: 'pr-1', missionId: 'fallback-mission', role: 'Manager', stage: 'delegation', message: 'Manager delegated work to WebDev.', metadata: { fromRole: 'Manager', toRole: 'WebDev' }, createdAt: now },
      { id: 'pr-2', missionId: 'fallback-mission', role: 'WebDev', stage: 'delegation', message: 'WebDev delegated work to Frontend.', metadata: { fromRole: 'WebDev', toRole: 'Frontend' }, createdAt: now },
    ],
    pendingPatchProposals: [
      { id: 'patch-a', missionId: 'fallback-mission', authorRole: 'Frontend', title: 'Frontend control room', summary: 'Sidebar, mesh and chat shell update.', status: 'PendingReview', targetPaths: ['frontend/src/App.tsx', 'frontend/src/styles.css'], diff: 'diff --git a/frontend/src/App.tsx b/frontend/src/App.tsx', createdAt: now, updatedAt: null },
    ],
    logicalQueueDepth: 2,
    chatModel: 'qwen2.5-coder:14b',
    physicalWorkerCount: 1,
  }
}

