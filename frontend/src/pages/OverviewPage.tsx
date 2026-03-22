import type { ApexConsoleState } from '../app/useApexConsole'
import { activeAgents, formatDateTime, roleMeta, toneByStatus } from '../app/view-models'

export function OverviewPage({ state }: { state: ApexConsoleState }) {
  const active = activeAgents(state.overview.agents)
  const recentRuns = state.overview.recentRuns.length > 0 ? state.overview.recentRuns : state.runs.filter((run) => run.id !== state.overview.activeRun?.id).slice(0, 8)
  const recentActivity = state.deferredActivities.slice(0, 6)
  const pendingReview = state.currentRun.patchProposals.filter((proposal) => proposal.status === 'PendingReview').length

  return (
    <div className="page-stack">
      <section className="stat-grid">
        <article className="panel stat-card">
          <span>Active run</span>
          <strong>{state.overview.activeRun?.status ?? 'Idle'}</strong>
          <p>{state.overview.activeRun?.currentPhase ?? 'No run is active.'}</p>
        </article>
        <article className="panel stat-card">
          <span>Queue depth</span>
          <strong>{state.overview.system.logicalQueueDepth}</strong>
          <p>{state.connected ? 'Realtime activity stream connected.' : 'Polling the API for refresh.'}</p>
        </article>
        <article className="panel stat-card">
          <span>Active agents</span>
          <strong>{active.length}</strong>
          <p>{state.overview.agents.length} configured roles available.</p>
        </article>
        <article className="panel stat-card">
          <span>Pending review</span>
          <strong>{pendingReview}</strong>
          <p>Patch proposals waiting for operator action.</p>
        </article>
      </section>

      <section className="content-grid content-grid--overview">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Current</p>
              <h2>{state.currentRun.title}</h2>
            </div>
            <span className={`tone-pill is-${toneByStatus(state.currentRun.status)}`}>{state.currentRun.status}</span>
          </div>

          <div className="detail-grid">
            <div>
              <span>Template</span>
              <strong>{state.currentRun.swarmTemplate}</strong>
            </div>
            <div>
              <span>Repository</span>
              <strong>{state.currentRun.selectedRepository?.fullName ?? 'No repository'}</strong>
            </div>
            <div>
              <span>Sprint</span>
              <strong>{state.currentRun.selectedSprint?.title ?? 'No sprint'}</strong>
            </div>
            <div>
              <span>Selected task</span>
              <strong>{state.currentRun.selectedWorkItem?.title ?? 'Direct task'}</strong>
            </div>
          </div>

          <div className="summary-box">
            <p>{state.currentRun.objective || 'No objective defined yet.'}</p>
          </div>

          <div className="button-row">
            <button type="button" className="button" onClick={() => state.handleSelectRun(state.currentRun.id)} disabled={state.currentRun.id === 'idle-run'}>
              Open in Swarms
            </button>
            <button type="button" className="button button--secondary" onClick={() => state.handleCancelRun(state.currentRun.id)} disabled={state.currentRun.id === 'idle-run' || state.busy}>
              Cancel run
            </button>
            <button type="button" className="button button--secondary" onClick={() => state.handleArchiveRun(state.currentRun.id)} disabled={state.currentRun.id === 'idle-run' || state.busy}>
              Archive
            </button>
          </div>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Recent</p>
              <h2>Run history</h2>
            </div>
          </div>

          {recentRuns.length > 0 ? (
            <div className="list-stack">
              {recentRuns.map((run) => (
                <button key={run.id} type="button" className={`list-card${state.selectedRunId === run.id ? ' is-active' : ''}`} onClick={() => state.handleSelectRun(run.id)}>
                  <div>
                    <strong>{run.title}</strong>
                    <p>{run.selectedRepository?.fullName ?? 'No repository'} · {run.swarmTemplate}</p>
                  </div>
                  <div className="list-card__meta">
                    <span className={`tone-pill is-${toneByStatus(run.status)}`}>{run.status}</span>
                    <small>{formatDateTime(run.updatedAt)}</small>
                  </div>
                </button>
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <strong>No recent runs</strong>
              <p>Run history appears here after the first task is created.</p>
            </div>
          )}
        </article>
      </section>

      <section className="content-grid">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">System</p>
              <h2>Role status</h2>
            </div>
          </div>

          <div className="compact-grid">
            {state.overview.agents.map((agent) => (
              <div key={agent.role} className="compact-card">
                <div className="compact-card__head">
                  <strong>{roleMeta[agent.role].title}</strong>
                  <span className={`tone-pill is-${toneByStatus(agent.status)}`}>{agent.status}</span>
                </div>
                <p>{agent.detail ?? roleMeta[agent.role].subtitle}</p>
              </div>
            ))}
          </div>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Activity</p>
              <h2>Latest events</h2>
            </div>
          </div>

          {recentActivity.length > 0 ? (
            <div className="timeline">
              {recentActivity.map((event) => (
                <div key={event.id} className="timeline-row">
                  <div>
                    <strong>{event.summary}</strong>
                    <p>{event.details}</p>
                  </div>
                  <span>{formatDateTime(event.createdAt)}</span>
                </div>
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <strong>No activity yet</strong>
              <p>The live feed will populate when a run starts.</p>
            </div>
          )}
        </article>
      </section>
    </div>
  )
}
