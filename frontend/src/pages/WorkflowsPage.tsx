import { motion } from 'framer-motion'
import type { ApexConsoleState } from '../app/useApexConsole'
import { buildWorkflowGraph, roleMeta } from '../app/view-models'

const templateSections = [
  {
    label: 'Ingestion & Processing',
    items: [
      { title: 'SQL Transformer', meta: 'Schema mapping & cleaning', icon: 'database', tone: 'cyan' },
      { title: 'PDF Parser', meta: 'OCR & entity extraction', icon: 'description', tone: 'cyan' },
    ],
  },
  {
    label: 'Intelligence Nodes',
    items: [
      { title: 'Semantic Router', meta: 'Intent-based branching', icon: 'psychology', tone: 'violet' },
      { title: 'Linguist Core', meta: 'Polyglot translation engine', icon: 'translate', tone: 'violet' },
    ],
  },
  {
    label: 'Distribution',
    items: [
      { title: 'Email Dispatch', meta: 'SMTP / SendGrid hook', icon: 'mail', tone: 'lime' },
      { title: 'Support Relay', meta: 'Customer handoff queue', icon: 'support_agent', tone: 'rose' },
    ],
  },
]

export function WorkflowsPage({ state }: { state: ApexConsoleState }) {
  const graph = buildWorkflowGraph(state.mission, state.deferredProgress)
  const nodeMap = new Map(graph.nodes.map((node) => [node.id, node]))

  return (
    <div className="ns-page ns-workflows-page">
      <section className="ns-workflow-layout">
        <div className="ns-workflow-canvas-wrap">
          <div className="ns-workflow-toolbar">
            <div className="ns-glass-chip">
              <strong>{state.selectedRepository?.name ?? 'Project Alpha'}</strong>
              <span>/</span>
              <small>{state.selectedSprint ? `Sprint #${state.selectedSprint.number}` : 'Neural Pipeline v4'}</small>
            </div>
            <div className="ns-glass-actions">
              <button type="button"><span className="material-symbols-outlined">near_me</span></button>
              <button type="button"><span className="material-symbols-outlined">pan_tool</span></button>
              <button type="button"><span className="material-symbols-outlined">add_box</span></button>
            </div>
          </div>

          <div className="ns-workflow-canvas">
            <svg className="ns-workflow-lines" viewBox="0 0 100 100" preserveAspectRatio="none">
              {graph.edges.map((edge) => {
                const from = nodeMap.get(edge.from)
                const to = nodeMap.get(edge.to)
                if (!from || !to) {
                  return null
                }

                const midX = (from.x + to.x) / 2
                return (
                  <g key={edge.id}>
                    <path
                      d={`M ${from.x} ${from.y} C ${midX} ${from.y}, ${midX} ${to.y}, ${to.x} ${to.y}`}
                      className={`ns-workflow-edge ${edge.active ? 'is-active' : ''}`}
                    />
                    <text x={midX} y={(from.y + to.y) / 2 - 2}>{edge.label}</text>
                    {edge.active ? (
                      <motion.circle
                        r="1"
                        className="ns-workflow-pulse"
                        initial={{ cx: from.x, cy: from.y, opacity: 0 }}
                        animate={{ cx: [from.x, to.x], cy: [from.y, to.y], opacity: [0, 1, 1, 0] }}
                        transition={{ duration: 1.6, repeat: Number.POSITIVE_INFINITY, ease: 'linear' }}
                      />
                    ) : null}
                  </g>
                )
              })}
            </svg>

            {graph.nodes.map((node) => {
              const meta = roleMeta[node.owner]
              return (
                <motion.article
                  key={node.id}
                  className={`ns-workflow-node tone-${node.tone} status-${node.status.toLowerCase()}`}
                  style={{ left: `${node.x}%`, top: `${node.y}%` }}
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.28 }}
                >
                  <div className="ns-workflow-node__top">
                    <span className="ns-node-tag">{meta.short}</span>
                    <span className="ns-node-tag ns-node-tag--ghost">{node.status}</span>
                  </div>
                  <h3>{node.title}</h3>
                  <p>{node.summary}</p>
                  <div className="ns-workflow-node__footer">
                    <span>{meta.title}</span>
                    <strong>{meta.subtitle}</strong>
                  </div>
                </motion.article>
              )
            })}

            <div className="ns-zoom-rail">
              <button type="button"><span className="material-symbols-outlined">add</span></button>
              <span>85%</span>
              <button type="button"><span className="material-symbols-outlined">remove</span></button>
            </div>
          </div>
        </div>

        <aside className="ns-workflow-sidebar">
          <div className="ns-card ns-template-panel">
            <div className="ns-section-head">
              <div>
                <p className="ns-eyebrow">Task Templates</p>
                <h2>Read-only orchestration kit</h2>
              </div>
            </div>
            {templateSections.map((section) => (
              <div key={section.label} className="ns-template-group">
                <p className="ns-eyebrow">{section.label}</p>
                <div className="ns-template-group__stack">
                  {section.items.map((item) => (
                    <div key={item.title} className={`ns-template-card tone-${item.tone}`}>
                      <div className="ns-template-card__icon">
                        <span className="material-symbols-outlined">{item.icon}</span>
                      </div>
                      <div>
                        <strong>{item.title}</strong>
                        <p>{item.meta}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </aside>
      </section>
    </div>
  )
}

