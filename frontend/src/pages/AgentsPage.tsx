import type { ApexConsoleState } from '../app/useApexConsole'
import { formatDateTime, readableAgentStatus, roleMeta, toneByAgentStatus } from '../app/view-models'

export function AgentsPage({ state }: { state: ApexConsoleState }) {
  const roster = state.currentRun.agents.length > 0 ? state.currentRun.agents : state.overview.agents

  return (
    <div className="page-stack">
      <section className="content-grid">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Registry</p>
              <h2>Agent roster</h2>
            </div>
          </div>

          <div className="agent-grid">
            {roster.map((agent) => (
              <article key={agent.role} className="agent-card">
                <div className="agent-card__head">
                  <div>
                    <strong>{roleMeta[agent.role].title}</strong>
                    <p>{roleMeta[agent.role].subtitle}</p>
                  </div>
                  <span className={`tone-pill is-${toneByAgentStatus(agent.status)}`}>{agent.status}</span>
                </div>
                <p>{readableAgentStatus(agent)}</p>
                <div className="chip-row">
                  <span className="chip">Queue {agent.queueDepth}</span>
                  <span className="chip">{formatDateTime(agent.updatedAt)}</span>
                </div>
              </article>
            ))}
          </div>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Capabilities</p>
              <h2>Role focus</h2>
            </div>
          </div>

          <div className="list-stack">
            {Object.entries(roleMeta).map(([role, meta]) => (
              <div key={role} className="list-card is-static">
                <div>
                  <strong>{meta.title}</strong>
                  <p>{meta.subtitle}</p>
                </div>
                <div className="chip-row">
                  {meta.capabilities.map((capability) => (
                    <span key={`${role}-${capability}`} className="chip">{capability}</span>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </article>
      </section>
    </div>
  )
}
