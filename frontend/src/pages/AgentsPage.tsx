import { useMemo, useState } from 'react'
import type { AgentRole } from '../types'
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
                <h2>Mission dispatch ve sprint baglami</h2>
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
                    <option key={sprint.id} value={String(sprint.id)}>
                      #{sprint.number} {sprint.title}
                    </option>
                  ))}
                </select>
              </label>

              <label className="ns-field ns-field--wide">
                <span>Mission</span>
                <input value={state.title} onChange={(event) => state.setTitle(event.target.value)} placeholder="Yeni sprint gorevi adi" />
              </label>

              <button type="button" className="ns-button ns-button--primary ns-button--dispatch" onClick={state.handleCreateMission} disabled={state.busy}>
                {state.busy ? 'Dispatching...' : 'Synthesize'}
              </button>
            </div>

            <div className="ns-constructor-footer">
              <label className="ns-field ns-field--prompt">
                <span>Prompt</span>
                <textarea value={state.prompt} onChange={(event) => state.setPrompt(event.target.value)} rows={3} />
              </label>
              <div className="ns-chip-row">
                {capabilityChips.map((capability) => (
                  <span key={capability} className="ns-chip">
                    {capability}
                  </span>
                ))}
              </div>
            </div>
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
