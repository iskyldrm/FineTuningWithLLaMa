import { useMemo, useState } from 'react'
import type { AgentRole, AgentToolType } from '../types'
import type { ApexConsoleState } from '../app/useApexConsole'
import {
  buildAgentPodGroups,
  computeReliability,
  formatRelativeDate,
  readableAgentStatus,
  roleMeta,
  statusTone,
  toneByRunStatus,
} from '../app/view-models'

const toolTypeOptions: AgentToolType[] = ['ListFiles', 'ReadFile', 'WriteFile', 'SearchCode', 'RunTerminal', 'GitStatus', 'GitDiff', 'GitCommit', 'GitPush', 'CustomCommand']

export function AgentsPage({ state }: { state: ApexConsoleState }) {
  const pods = buildAgentPodGroups(state.dashboard.agents.length > 0 ? state.dashboard.agents : state.fallback.agents)
  const [selectedPodId, setSelectedPodId] = useState<string>('all')
  const roster = state.dashboard.agents.length > 0 ? state.dashboard.agents : state.fallback.agents

  const visibleAgents = useMemo(() => {
    if (selectedPodId === 'all') {
      return roster
    }

    const selectedPod = pods.find((pod) => pod.id === selectedPodId)
    return selectedPod ? roster.filter((agent) => selectedPod.roles.includes(agent.role)) : roster
  }, [pods, roster, selectedPodId])

  const capabilityChips = Array.from(new Set(visibleAgents.flatMap((agent) => roleMeta[agent.role].capabilities))).slice(0, 6)
  const boardItems = useMemo(() => {
    const items = state.board?.items ?? []
    return state.selectedSprintId ? items.filter((item) => item.sprintId === state.selectedSprintId) : items
  }, [state.board?.items, state.selectedSprintId])
  const boardColumns = useMemo(() => {
    const grouped = new Map<string, typeof boardItems>()
    for (const item of boardItems) {
      const key = item.status || 'Backlog'
      const current = grouped.get(key) ?? []
      current.push(item)
      grouped.set(key, current)
    }

    return Array.from(grouped.entries()).map(([title, items]) => ({
      id: title.toLowerCase().replace(/\s+/g, '-'),
      title,
      items: items.sort((left, right) => new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime()),
    }))
  }, [boardItems])
  const selectedSprintMeta = state.sprints.find((sprint) => sprint.id === state.selectedSprintId) ?? null
  const selectedPolicy = state.runtimeCatalog.policies.find((policy) => policy.role === state.selectedPolicyRole) ?? null

  return (
    <div className="ns-page ns-agents-page">
      <section className="ns-hero">
        <div>
          <p className="ns-eyebrow">Agents / Registry</p>
          <h1 className="ns-hero__title">Agent Orchestration</h1>
          <p className="ns-hero__subtitle">
            Pod secimi, repo baglami, sprint dispatch ve registry gorunumu ayni control room icinde birlesiyor.
          </p>
        </div>
        <div className="ns-hero__metric ns-hero__metric--lime">
          <span>Active Compute</span>
          <strong>{(84.2 + state.dashboard.logicalQueueDepth).toFixed(1)} TFLOPS</strong>
        </div>
      </section>

      <div className="ns-agents-layout">
        <aside className="ns-agents-sidebar">
          <div className="ns-card ns-pod-list">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Agent Pods</p>
                <h2>Registry gruplari</h2>
              </div>
            </div>
            <button type="button" className={`ns-pod-item ${selectedPodId === 'all' ? 'is-active' : ''}`} onClick={() => setSelectedPodId('all')}>
              <span>
                <strong>Tum agentlar</strong>
                <small>Tam roster gorunumu</small>
              </span>
              <em>{roster.length}</em>
            </button>
            {pods.map((pod) => (
              <button key={pod.id} type="button" className={`ns-pod-item tone-${pod.tone} ${selectedPodId === pod.id ? 'is-active' : ''}`} onClick={() => setSelectedPodId(pod.id)}>
                <span>
                  <strong>{pod.label}</strong>
                  <small>{pod.description}</small>
                </span>
                <em>{pod.count}</em>
              </button>
            ))}
          </div>

          <div className="ns-card ns-mini-chart-card">
            <p className="ns-eyebrow">Network Reliability</p>
            <div className="ns-mini-chart" aria-hidden="true">
              <i />
              <i />
              <i />
              <i />
              <i />
              <i />
            </div>
            <div className="ns-mini-chart__meta">
              <span>00:08</span>
              <strong>{state.connected ? '0.992 REL' : '0.971 REL'}</strong>
            </div>
          </div>
        </aside>

        <section className="ns-agents-main">
          <article className="ns-card ns-constructor-card">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Neural Entity Constructor</p>
                <h2>Repo, sprint ve board dispatch</h2>
              </div>
              <span className={`ns-pill ${state.error ? 'is-bad' : 'is-good'}`}>{state.error ?? state.repoStatus}</span>
            </div>

            <div className="ns-constructor-grid">
              <label className="ns-field">
                <span>Repository</span>
                <select value={state.selectedRepoKey} onChange={(event) => state.setSelectedRepoKey(event.target.value)}>
                  <option value="">Repository sec</option>
                  {state.repositories.map((repo) => (
                    <option key={`${repo.owner}/${repo.name}`} value={`${repo.owner}/${repo.name}`}>
                      {repo.fullName}
                    </option>
                  ))}
                </select>
              </label>

              <label className="ns-field">
                <span>Sprint</span>
                <select value={state.selectedSprintId} onChange={(event) => state.setSelectedSprintId(event.target.value)} disabled={state.sprints.length === 0}>
                  <option value="">Sprint sec</option>
                  {state.sprints.map((sprint) => (
                    <option key={sprint.id} value={sprint.id}>
                      {sprint.projectTitle ? `${sprint.projectTitle} / ` : ''}{sprint.title}
                    </option>
                  ))}
                </select>
              </label>

              <label className="ns-field ns-field--wide">
                <span>Mission</span>
                <input value={state.title} onChange={(event) => state.setTitle(event.target.value)} placeholder="Yeni sprint gorevi adi" />
              </label>

              <button type="button" className="ns-button ns-button--primary ns-button--dispatch" onClick={state.handleCreateMission} disabled={state.busy}>
                {state.busy ? 'Gonderiliyor...' : 'Serbest Gorev Gonder'}
              </button>
            </div>

            <div className="ns-constructor-footer">
              <label className="ns-field ns-field--prompt">
                <span>Prompt</span>
                <textarea value={state.prompt} onChange={(event) => state.setPrompt(event.target.value)} rows={3} />
              </label>
              <div className="ns-task-brief">
                <div className="ns-chip-row">
                  {capabilityChips.map((capability) => (
                    <span key={capability} className="ns-chip">
                      {capability}
                    </span>
                  ))}
                </div>
                <div className="ns-card ns-task-selection-card">
                  <p className="ns-eyebrow">Selected Board Task</p>
                  {state.selectedWorkItem ? (
                    <>
                      <strong>{state.selectedWorkItem.title}</strong>
                      <p>{state.selectedWorkItem.projectTitle} | {state.selectedWorkItem.status}</p>
                      <small>{state.selectedWorkItem.sprintTitle}</small>
                    </>
                  ) : (
                    <p>Bir board karti secildiginde gorev promptu otomatik dolacak ve AI takim dispatch butonu kart uzerinde acilacak.</p>
                  )}
                </div>
              </div>
            </div>
          </article>

          <article className="ns-card ns-board-card">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">GitHub Project Board</p>
                <h2>{selectedSprintMeta ? selectedSprintMeta.title : 'Sprint board bekleniyor'}</h2>
              </div>
              <span className="ns-pill">{boardItems.length} kart</span>
            </div>

            <div className="ns-board-meta">
              <span>{state.board?.source ?? 'board'}</span>
              <strong>{state.board?.projects.length ?? 0} project</strong>
              <strong>{state.sprints.length} sprint</strong>
              {state.mission.pullRequest?.url ? (
                <a className="ns-link-button" href={state.mission.pullRequest.url} target="_blank" rel="noreferrer">
                  Open PR
                </a>
              ) : null}
            </div>

            {boardColumns.length > 0 ? (
              <div className="ns-board-columns">
                {boardColumns.map((column) => (
                  <section key={column.id} className="ns-board-column">
                    <header className="ns-board-column__head">
                      <strong>{column.title}</strong>
                      <span>{column.items.length}</span>
                    </header>
                    <div className="ns-board-column__stack">
                      {column.items.map((item) => (
                        <article key={item.id} className={`ns-board-task ${state.selectedWorkItemId === item.id ? 'is-active' : ''}`}>
                          <div className="ns-board-task__head">
                            <strong>{item.title}</strong>
                            <span>{item.number ? `#${item.number}` : item.contentType}</span>
                          </div>
                          <p>{item.description || 'Aciklama yok.'}</p>
                          <div className="ns-chip-row">
                            <span className="ns-chip">{item.projectTitle}</span>
                            <span className="ns-chip">{item.sprintTitle}</span>
                            {item.labels.slice(0, 2).map((label) => (
                              <span key={label} className="ns-chip">
                                {label}
                              </span>
                            ))}
                          </div>
                          {item.subtasks.length > 0 ? (
                            <div className="ns-board-task__list">
                              {item.subtasks.slice(0, 3).map((subtask) => (
                                <small key={subtask}>{subtask}</small>
                              ))}
                            </div>
                          ) : null}
                          <div className="ns-board-task__footer">
                            <span>{item.assignees.length > 0 ? item.assignees.join(', ') : 'Unassigned'}</span>
                            <button type="button" className="ns-button ns-button--primary" onClick={() => state.handleDispatchWorkItem(item)} disabled={state.busy}>
                              {state.busy ? 'Gonderiliyor...' : 'Manager Agentina Gonder'}
                            </button>
                          </div>
                        </article>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            ) : (
              <div className="ns-board-empty">
                <strong>Bu sprint icin board karti yok.</strong>
                <p>Repository sec, sprint sec ve GitHub Project iteration icindeki task veya issue kartlari burada board olarak gelsin.</p>
              </div>
            )}
          </article>

          <div className="ns-agent-card-grid">
            {visibleAgents.map((agent) => {
              const meta = roleMeta[agent.role]
              return (
                <article key={agent.role} className={`ns-card ns-registry-card tone-${meta.tone}`}>
                  <div className="ns-registry-card__head">
                    <div className="ns-registry-card__identity">
                      <div className="ns-registry-card__icon">
                        <span className="material-symbols-outlined">{meta.icon}</span>
                      </div>
                      <div>
                        <h3>{meta.title}</h3>
                        <p>{meta.registryId}</p>
                      </div>
                    </div>
                    <div className="ns-registry-card__stat">
                      <span>Reliability</span>
                      <strong>{computeReliability(agent)}%</strong>
                    </div>
                  </div>

                  <div className="ns-capability-tags">
                    {meta.capabilities.map((capability) => (
                      <span key={capability}>{capability}</span>
                    ))}
                  </div>

                  <div className="ns-registry-card__focus">
                    <span>Current Focus</span>
                    <p>{agent.detail ?? readableAgentStatus(agent)}</p>
                  </div>

                  <div className="ns-registry-card__footer">
                    <span className={`ns-status-text ${toneByRunStatus(agent.status)}`}>{agent.status}</span>
                    <small>Updated {formatRelativeDate(agent.updatedAt)}</small>
                  </div>
                </article>
              )
            })}
          </div>

          <div className="ns-runtime-grid">
            <article className="ns-card ns-runtime-card">
              <div className="ns-section-head">
                <div>
                  <p className="ns-eyebrow">Agent Loop Runtime</p>
                  <h2>Role bazli tool yetkileri</h2>
                </div>
                <span className="ns-pill">{selectedPolicy?.executionMode ?? 'Unconfigured'}</span>
              </div>

              <div className="ns-runtime-form-grid">
                <label className="ns-field">
                  <span>Role</span>
                  <select value={state.selectedPolicyRole} onChange={(event) => state.setSelectedPolicyRole(event.target.value as AgentRole)}>
                    {roster.map((agent) => (
                      <option key={agent.role} value={agent.role}>{agent.role}</option>
                    ))}
                  </select>
                </label>

                <label className="ns-field">
                  <span>Execution Mode</span>
                  <select
                    value={state.policyDraft.executionMode}
                    onChange={(event) => state.setPolicyDraft((current) => ({ ...current, executionMode: event.target.value as 'StructuredPrompt' | 'ToolLoop' }))}
                  >
                    <option value="StructuredPrompt">StructuredPrompt</option>
                    <option value="ToolLoop">ToolLoop</option>
                  </select>
                </label>

                <label className="ns-field">
                  <span>Max Steps</span>
                  <input
                    type="number"
                    min={1}
                    max={24}
                    value={state.policyDraft.maxSteps}
                    onChange={(event) => state.setPolicyDraft((current) => ({ ...current, maxSteps: Number(event.target.value) || 1 }))}
                  />
                </label>
              </div>

              <label className="ns-field ns-field--prompt">
                <span>Writable Roots</span>
                <textarea
                  rows={4}
                  value={state.policyDraft.writableRoots}
                  onChange={(event) => state.setPolicyDraft((current) => ({ ...current, writableRoots: event.target.value }))}
                  placeholder={'frontend\nsrc'}
                />
              </label>

              <div className="ns-runtime-tools">
                {state.runtimeCatalog.tools.map((tool) => (
                  <label key={tool.name} className={`ns-runtime-tool ${state.policyDraft.allowedTools.includes(tool.name) ? 'is-active' : ''}`}>
                    <input
                      type="checkbox"
                      checked={state.policyDraft.allowedTools.includes(tool.name)}
                      onChange={() => state.togglePolicyTool(tool.name)}
                    />
                    <div>
                      <strong>{tool.displayName}</strong>
                      <p>{tool.name} | {tool.type}</p>
                      <small>{tool.description}</small>
                    </div>
                    {tool.destructive ? <span className="ns-pill warn">destructive</span> : null}
                  </label>
                ))}
              </div>

              <div className="ns-runtime-actions">
                <button type="button" className="ns-button ns-button--primary" onClick={state.handleSavePolicy} disabled={state.runtimeBusy}>
                  {state.runtimeBusy ? 'Kaydediliyor...' : 'Role Policy Kaydet'}
                </button>
              </div>
            </article>

            <article className="ns-card ns-runtime-card">
              <div className="ns-section-head">
                <div>
                  <p className="ns-eyebrow">Tool Registry</p>
                  <h2>Custom tool calling tanimla</h2>
                </div>
                <span className="ns-pill">{state.runtimeCatalog.tools.length} tool</span>
              </div>

              <div className="ns-runtime-tool-list">
                {state.runtimeCatalog.tools.map((tool) => (
                  <div key={tool.name} className="ns-runtime-tool-list__item">
                    <div>
                      <strong>{tool.displayName}</strong>
                      <p>{tool.name} | {tool.type}</p>
                    </div>
                    <div className="ns-chip-row">
                      <span className="ns-chip">{tool.enabled ? 'enabled' : 'disabled'}</span>
                      {tool.destructive ? <span className="ns-chip">destructive</span> : null}
                    </div>
                  </div>
                ))}
              </div>

              <div className="ns-runtime-form-grid">
                <label className="ns-field">
                  <span>Name</span>
                  <input value={state.toolForm.name} onChange={(event) => state.setToolForm((current) => ({ ...current, name: event.target.value }))} placeholder="open_pr" />
                </label>

                <label className="ns-field">
                  <span>Display Name</span>
                  <input value={state.toolForm.displayName} onChange={(event) => state.setToolForm((current) => ({ ...current, displayName: event.target.value }))} placeholder="Open Pull Request" />
                </label>

                <label className="ns-field">
                  <span>Type</span>
                  <select value={state.toolForm.type} onChange={(event) => state.setToolForm((current) => ({ ...current, type: event.target.value as AgentToolType }))}>
                    {toolTypeOptions.map((type) => (
                      <option key={type} value={type}>{type}</option>
                    ))}
                  </select>
                </label>
              </div>

              <label className="ns-field ns-field--prompt">
                <span>Description</span>
                <textarea value={state.toolForm.description} onChange={(event) => state.setToolForm((current) => ({ ...current, description: event.target.value }))} rows={3} />
              </label>

              <label className="ns-field ns-field--prompt">
                <span>Command Template</span>
                <textarea
                  value={state.toolForm.commandTemplate}
                  onChange={(event) => state.setToolForm((current) => ({ ...current, commandTemplate: event.target.value }))}
                  rows={3}
                  placeholder={"gh pr create --title {{title}} --body {{body}}"}
                />
              </label>

              <div className="ns-runtime-toggle-row">
                <label className="ns-runtime-checkbox">
                  <input type="checkbox" checked={state.toolForm.enabled} onChange={(event) => state.setToolForm((current) => ({ ...current, enabled: event.target.checked }))} />
                  <span>Enabled</span>
                </label>
                <label className="ns-runtime-checkbox">
                  <input type="checkbox" checked={state.toolForm.destructive} onChange={(event) => state.setToolForm((current) => ({ ...current, destructive: event.target.checked }))} />
                  <span>Destructive</span>
                </label>
              </div>

              <div className="ns-runtime-actions">
                <button type="button" className="ns-button ns-button--primary" onClick={state.handleSaveTool} disabled={state.runtimeBusy}>
                  {state.runtimeBusy ? 'Kaydediliyor...' : 'Tool Kaydet'}
                </button>
              </div>
            </article>
          </div>

          <article className="ns-card ns-patch-tray">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Patch Review</p>
                <h2>Pending review ve registry diffleri</h2>
              </div>
              <span className="ns-pill">{state.mission.patchProposals.length} teklif</span>
            </div>

            <div className="ns-patch-tray__grid">
              {state.mission.patchProposals.map((proposal) => (
                <div key={proposal.id} className="ns-patch-card">
                  <div className="ns-patch-card__head">
                    <div>
                      <strong>{proposal.title}</strong>
                      <p>{proposal.authorRole}</p>
                    </div>
                    <span className={`ns-pill ${statusTone(proposal.status)}`}>{proposal.status}</span>
                  </div>
                  <p>{proposal.summary}</p>
                  <code>{proposal.targetPaths.join(', ')}</code>
                  <div className="ns-patch-card__actions">
                    <button type="button" className="ns-button ns-button--primary" onClick={() => state.handlePatchDecision(proposal, 'approve')} disabled={proposal.status !== 'PendingReview'}>
                      Approve
                    </button>
                    <button type="button" className="ns-button ns-button--ghost" onClick={() => state.handlePatchDecision(proposal, 'reject')} disabled={proposal.status !== 'PendingReview'}>
                      Reject
                    </button>
                  </div>
                </div>
              ))}
              <div className="ns-patch-files-card">
                <p className="ns-eyebrow">Changed Files</p>
                {state.changedFiles.map((file) => (
                  <code key={file}>{file}</code>
                ))}
              </div>
            </div>
          </article>
        </section>
      </div>
    </div>
  )
}

