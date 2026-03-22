import type { ApexConsoleState } from '../app/useApexConsole'
import { formatDateTime, roleMeta, toneByStatus } from '../app/view-models'

export function SwarmsPage({ state }: { state: ApexConsoleState }) {
  const delegationEvents = state.deferredProgress
    .filter((item) => item.stage === 'delegation')
    .slice(-12)
    .reverse()
  const run = state.currentRun

  return (
    <div className="page-stack">
      <section className="content-grid content-grid--swarms">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Live run</p>
              <h2>{run.title}</h2>
            </div>
            <span className={`tone-pill is-${toneByStatus(run.status)}`}>{run.status}</span>
          </div>

          <div className="detail-grid">
            <div>
              <span>Current phase</span>
              <strong>{run.currentPhase ?? 'Idle'}</strong>
            </div>
            <div>
              <span>Template</span>
              <strong>{run.swarmTemplate}</strong>
            </div>
            <div>
              <span>Repository</span>
              <strong>{run.selectedRepository?.fullName ?? 'No repository'}</strong>
            </div>
            <div>
              <span>Workspace</span>
              <strong>{run.workspaceRootPath ?? 'Not initialized'}</strong>
            </div>
          </div>

          <div className="summary-box">
            <p>{run.objective || run.prompt || 'No objective available.'}</p>
          </div>

          <div className="button-row">
            <button type="button" className="button button--secondary" onClick={() => state.handleCancelRun(run.id)} disabled={run.id === 'idle-run' || state.busy}>
              Cancel
            </button>
            <button type="button" className="button button--secondary" onClick={() => state.handleArchiveRun(run.id)} disabled={run.id === 'idle-run' || state.busy}>
              Archive
            </button>
          </div>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Delegation</p>
              <h2>Swarm flow</h2>
            </div>
          </div>

          {delegationEvents.length > 0 ? (
            <div className="list-stack">
              {delegationEvents.map((event) => (
                <div key={event.id} className="list-card is-static">
                  <div>
                    <strong>{event.metadata.fromRole ?? event.role ?? 'Manager'} → {event.metadata.toRole ?? 'Unknown'}</strong>
                    <p>{event.message}</p>
                  </div>
                  <small>{formatDateTime(event.createdAt)}</small>
                </div>
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <strong>No delegation events</strong>
              <p>Delegation edges will appear here while the swarm runs.</p>
            </div>
          )}
        </article>
      </section>

      <section className="content-grid">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Progress</p>
              <h2>Role execution</h2>
            </div>
          </div>

          {run.steps.length > 0 ? (
            <div className="list-stack">
              {run.steps.map((step) => (
                <div key={step.id} className="list-card is-static">
                  <div>
                    <strong>{roleMeta[step.owner].title}</strong>
                    <p>{step.summary}</p>
                  </div>
                  <span className={`tone-pill is-${toneByStatus(step.status)}`}>{step.status}</span>
                </div>
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <strong>No run steps yet</strong>
              <p>Step progress appears after the analyst defines the run path.</p>
            </div>
          )}
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Logs</p>
              <h2>Run feed</h2>
            </div>
          </div>

          {state.deferredProgress.length > 0 ? (
            <div className="log-list">
              {state.deferredProgress.slice(-20).reverse().map((item) => (
                <div key={item.id} className="log-row">
                  <span>{formatDateTime(item.createdAt)}</span>
                  <strong>{item.role ?? item.stage}</strong>
                  <p>{item.message}</p>
                </div>
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <strong>No progress feed</strong>
              <p>The run feed will stream here as agents work.</p>
            </div>
          )}
        </article>
      </section>

      <section className="panel">
        <div className="section-head">
          <div>
            <p className="eyebrow">Patch review</p>
            <h2>Proposals</h2>
          </div>
        </div>

        {run.patchProposals.length > 0 ? (
          <div className="patch-grid">
            {run.patchProposals.map((proposal) => (
              <article key={proposal.id} className="patch-card">
                <div className="patch-card__head">
                  <div>
                    <strong>{proposal.title}</strong>
                    <p>{roleMeta[proposal.authorRole].title}</p>
                  </div>
                  <span className={`tone-pill is-${toneByStatus(proposal.status)}`}>{proposal.status}</span>
                </div>
                <p>{proposal.summary}</p>
                <div className="chip-row">
                  {proposal.targetPaths.map((path) => (
                    <span key={`${proposal.id}-${path}`} className="chip">{path}</span>
                  ))}
                </div>
                <div className="patch-diff">
                  <pre>{proposal.diff}</pre>
                </div>
                <div className="button-row">
                  <button type="button" className="button" onClick={() => state.handlePatchDecision(proposal, 'approve')} disabled={proposal.status !== 'PendingReview'}>
                    Approve
                  </button>
                  <button type="button" className="button button--secondary" onClick={() => state.handlePatchDecision(proposal, 'reject')} disabled={proposal.status !== 'PendingReview'}>
                    Reject
                  </button>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-state">
            <strong>No patch proposals</strong>
            <p>Patch review remains manual and appears here when agents submit diffs.</p>
          </div>
        )}
      </section>
    </div>
  )
}
