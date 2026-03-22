import { useMemo } from 'react'
import type { ApexConsoleState } from '../app/useApexConsole'
import { formatDateTime, swarmTemplates, toneByStatus } from '../app/view-models'

export function TasksPage({ state }: { state: ApexConsoleState }) {
  const boardItems = useMemo(() => {
    const items = state.board?.items ?? []
    return state.selectedSprintId ? items.filter((item) => item.sprintId === state.selectedSprintId) : items
  }, [state.board?.items, state.selectedSprintId])

  return (
    <div className="page-stack">
      <section className="content-grid content-grid--tasks">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Direct task</p>
              <h2>Create a run</h2>
            </div>
          </div>

          <div className="form-grid">
            <label className="field">
              <span>Repository</span>
              <select value={state.selectedRepoKey} onChange={(event) => state.setSelectedRepoKey(event.target.value)}>
                <option value="">No repository</option>
                {state.repositories.map((repo) => (
                  <option key={`${repo.owner}/${repo.name}`} value={`${repo.owner}/${repo.name}`}>
                    {repo.fullName}
                  </option>
                ))}
              </select>
            </label>

            <label className="field">
              <span>Swarm template</span>
              <select value={state.selectedSwarmTemplate} onChange={(event) => state.setSelectedSwarmTemplate(event.target.value as typeof state.selectedSwarmTemplate)}>
                {swarmTemplates.map((template) => (
                  <option key={template.value} value={template.value}>
                    {template.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="field">
              <span>Sprint</span>
              <select value={state.selectedSprintId} onChange={(event) => state.setSelectedSprintId(event.target.value)} disabled={state.sprints.length === 0}>
                <option value="">No sprint</option>
                {state.sprints.map((sprint) => (
                  <option key={sprint.id} value={sprint.id}>
                    {sprint.projectTitle ? `${sprint.projectTitle} / ` : ''}{sprint.title}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <label className="field">
            <span>Task title</span>
            <input value={state.title} onChange={(event) => state.setTitle(event.target.value)} placeholder="Stabilize run orchestration" />
          </label>

          <label className="field">
            <span>Objective</span>
            <textarea value={state.objective} onChange={(event) => state.setObjective(event.target.value)} rows={7} />
          </label>

          <div className="button-row">
            <button type="button" className="button" onClick={state.handleCreateRun} disabled={state.busy}>
              {state.busy ? 'Creating...' : 'Create run'}
            </button>
            <div className="inline-note">
              <strong>{state.repoStatus}</strong>
              <span>Board import is optional. Direct entry is the primary path.</span>
            </div>
          </div>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Templates</p>
              <h2>Swarm modes</h2>
            </div>
          </div>

          <div className="list-stack">
            {swarmTemplates.map((template) => (
              <button
                key={template.value}
                type="button"
                className={`list-card${state.selectedSwarmTemplate === template.value ? ' is-active' : ''}`}
                onClick={() => state.setSelectedSwarmTemplate(template.value)}
              >
                <div>
                  <strong>{template.label}</strong>
                  <p>{template.description}</p>
                </div>
                <span className="tone-pill is-neutral">{template.value}</span>
              </button>
            ))}
          </div>
        </article>
      </section>

      <section className="panel">
        <div className="section-head">
          <div>
            <p className="eyebrow">Optional import</p>
            <h2>Repository board items</h2>
          </div>
        </div>

        {boardItems.length > 0 ? (
          <div className="board-grid">
            {boardItems.map((item) => (
              <article key={item.id} className={`board-card${state.selectedWorkItemId === item.id ? ' is-active' : ''}`}>
                <div className="board-card__head">
                  <strong>{item.title}</strong>
                  <span>{item.number ? `#${item.number}` : item.contentType}</span>
                </div>
                <p>{item.description || 'No description provided.'}</p>
                <div className="chip-row">
                  <span className="chip">{item.status}</span>
                  <span className="chip">{item.sprintTitle}</span>
                </div>
                <div className="button-row">
                  <button type="button" className="button button--secondary" onClick={() => state.setSelectedWorkItemId(item.id)}>
                    Select
                  </button>
                  <button type="button" className="button" onClick={() => state.handleDispatchWorkItem(item)} disabled={state.busy}>
                    Dispatch
                  </button>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-state">
            <strong>No board items in view</strong>
            <p>Select a repository to load optional GitHub board work. Direct tasks can still be created immediately.</p>
          </div>
        )}
      </section>

      <section className="panel">
        <div className="section-head">
          <div>
            <p className="eyebrow">Queue</p>
            <h2>Recent runs</h2>
          </div>
        </div>

        {state.runs.length > 0 ? (
          <div className="list-stack">
            {state.runs.slice(0, 8).map((run) => (
              <button key={run.id} type="button" className="list-card" onClick={() => state.handleSelectRun(run.id)}>
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
        ) : null}
      </section>
    </div>
  )
}
