import type { ApexConsoleState } from '../app/useApexConsole'
import { buildDashboardMetrics, computeReliability, formatClock, roleMeta, stepProgressValue, topAgents } from '../app/view-models'

export function DashboardPage({ state }: { state: ApexConsoleState }) {
  const metrics = buildDashboardMetrics(state.dashboard, state.mission)
  const featuredAgents = topAgents(state.dashboard.agents.length > 0 ? state.dashboard.agents : state.fallback.agents)

  return (
    <div className="ns-page ns-dashboard-page">
      <section className="ns-hero">
        <div>
          <p className="ns-eyebrow">Dashboard / Neural Workspace</p>
          <h1 className="ns-hero__title">Neural Workspace</h1>
          <p className="ns-hero__subtitle">
            <span>{featuredAgents.length} aktif intelligence node</span> APEX control mesh icinde orkestre ediliyor.
          </p>
        </div>
        <div className="ns-hero__metric">
          <span>Core Load</span>
          <strong>{metrics[1].value}</strong>
          <div className="ns-mini-bars" aria-hidden="true">
            <i />
            <i />
            <i />
            <i />
            <i />
          </div>
        </div>
      </section>

      <div className="ns-dashboard-grid">
        <section className="ns-column ns-column--main">
          <div className="ns-agent-grid">
            {featuredAgents.map((agent) => {
              const meta = roleMeta[agent.role]
              return (
                <article key={agent.role} className={`ns-card ns-agent-overview tone-${meta.tone}`}>
                  <div className="ns-agent-overview__head">
                    <div className="ns-agent-overview__identity">
                      <div className="ns-agent-overview__icon">
                        <span className="material-symbols-outlined">{meta.icon}</span>
                      </div>
                      <div>
                        <h3>{meta.title}</h3>
                        <p>{meta.registryId}</p>
                      </div>
                    </div>
                    <div className="ns-agent-overview__stat">
                      <span>Reliability</span>
                      <strong>{computeReliability(agent)}%</strong>
                    </div>
                  </div>
                  <div className="ns-inline-status">
                    <span className={`tone-dot tone-${meta.tone}`} />
                    <span>{agent.detail ?? agent.status}</span>
                  </div>
                  <div className="ns-progress-meta">
                    <span>{meta.subtitle}</span>
                    <strong>{Math.max(6, 100 - agent.queueDepth * 9)}%</strong>
                  </div>
                  <div className="ns-progress-bar">
                    <div style={{ width: `${Math.max(6, 100 - agent.queueDepth * 9)}%` }} />
                  </div>
                </article>
              )
            })}
          </div>

          <article className="ns-card ns-task-stream">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Active Task Stream</p>
                <h2>Canli sprint akisi</h2>
              </div>
              <button type="button" className="ns-link-button">Tum surecleri goster</button>
            </div>

            <div className="ns-task-stream__list">
              {state.mission.steps.map((step, index) => (
                <div key={step.id} className="ns-task-stream__row">
                  <div className="ns-task-stream__index">{String(index + 1).padStart(2, '0')}</div>
                  <div className="ns-task-stream__body">
                    <div className="ns-task-stream__topline">
                      <strong>{step.title}</strong>
                      <span>{stepProgressValue(step, index, state.mission.steps.length)}%</span>
                    </div>
                    <div className="ns-progress-bar ns-progress-bar--tight">
                      <div style={{ width: `${stepProgressValue(step, index, state.mission.steps.length)}%` }} />
                    </div>
                    <p>
                      Atanan: <span>{roleMeta[step.owner].short}</span> | {step.summary}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </article>
        </section>

        <aside className="ns-column ns-column--side">
          <article className="ns-card ns-resource-panel">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Resource Allocation</p>
                <h2>Compute snapshot</h2>
              </div>
            </div>
            <div className="ns-resource-panel__metrics">
              {metrics.map((metric) => (
                <div key={metric.label} className={`ns-resource-line tone-${metric.tone}`}>
                  <div>
                    <span>{metric.label}</span>
                    <small>{metric.helper}</small>
                  </div>
                  <strong>{metric.value}</strong>
                </div>
              ))}
            </div>
          </article>

          <article className="ns-card ns-terminal-card">
            <div className="ns-terminal-card__chrome">
              <div className="ns-terminal-card__lights">
                <i />
                <i />
                <i />
              </div>
              <span>substrate_feed.log</span>
            </div>
            <div className="ns-terminal-card__body">
              {state.deferredActivities.map((event) => (
                <div key={event.id} className="ns-log-row">
                  <time>{formatClock(event.createdAt)}</time>
                  <span>{event.agentRole ? roleMeta[event.agentRole].short : 'SYS'}</span>
                  <p>{event.summary}</p>
                </div>
              ))}
            </div>
          </article>

          <article className="ns-card ns-quick-actions">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Quick Commands</p>
                <h2>{state.connected ? 'Live stream active' : 'Polling mode active'}</h2>
              </div>
            </div>
            <div className="ns-quick-actions__grid">
              <button type="button">Scale fleet</button>
              <button type="button">Flush cache</button>
              <button type="button">Audit logs</button>
              <button type="button">Pause ops</button>
            </div>
          </article>
        </aside>
      </div>
    </div>
  )
}
