import type { ApexConsoleState } from '../app/useApexConsole'
import { activeDiscourseTitle, buildExecutionFeed, formatClock } from '../app/view-models'

export function ExecutionPage({ state }: { state: ApexConsoleState }) {
  const feed = buildExecutionFeed(state.dashboard, state.deferredProgress)

  return (
    <div className="ns-page ns-execution-page">
      <section className="ns-execution-banner ns-card">
        <div className="ns-execution-banner__left">
          <div className="ns-inline-status">
            <span className="tone-dot tone-cyan" />
            <strong>Active Deployment: {state.selectedRepository?.name ?? 'Substrate-Node-88'}</strong>
          </div>
          <span className="ns-compute-badge">
            <span className="material-symbols-outlined">bolt</span>
            COMPUTE: {(84.2 + state.dashboard.physicalWorkerCount).toFixed(1)} TFLOPS
          </span>
        </div>
        <div className="ns-execution-banner__actions">
          <button type="button" className="ns-button ns-button--danger">Force Intervene</button>
          <button type="button" className="ns-icon-button">
            <span className="material-symbols-outlined">more_vert</span>
          </button>
        </div>
      </section>

      <div className="ns-execution-layout">
        <section className="ns-terminal-panel">
          <div className="ns-terminal-panel__chrome">
            <div className="ns-terminal-card__lights">
              <i />
              <i />
              <i />
            </div>
            <div>
              <span>kernel_logs.sh</span>
              <button type="button" className="ns-icon-button ns-icon-button--small">
                <span className="material-symbols-outlined">content_copy</span>
              </button>
            </div>
          </div>

          <div className="ns-terminal-panel__body">
            {feed.map((row) => (
              <div key={row.id} className={`ns-terminal-line tone-${row.tone}`}>
                <span>{String(row.line).padStart(3, '0')}</span>
                <strong>[{row.tag}]</strong>
                <p>{row.content}</p>
              </div>
            ))}
            <div className="ns-terminal-line tone-cyan is-cursor">
              <span>{String(feed.length + 1).padStart(3, '0')}</span>
              <strong>[LIVE]</strong>
              <p>_</p>
            </div>
          </div>

          <footer className="ns-terminal-footer">
            <span>MEM: 12.4 / 40.0 GB</span>
            <span>NODE: {state.selectedRepository?.defaultBranch?.toUpperCase() ?? 'US-EAST-SUB-01'}</span>
            <span>{state.connected ? 'SYNCED' : 'POLLING'}</span>
          </footer>
        </section>

        <aside className="ns-discourse-panel">
          <div className="ns-discourse-panel__head">
            <div>
              <p className="ns-eyebrow">Agent Discourse</p>
              <h2>{activeDiscourseTitle(state.selectedThread?.title)}</h2>
            </div>
            <span className="ns-pill">{state.messages.length} mesaj</span>
          </div>

          <div className="ns-discourse-panel__controls">
            <label className="ns-field ns-field--compact">
              <span>Thread</span>
              <select value={state.selectedThreadId ?? ''} onChange={(event) => state.setSelectedThreadId(event.target.value || null)}>
                <option value="">Thread sec</option>
                {state.threads.map((thread) => (
                  <option key={thread.id} value={thread.id}>{thread.title}</option>
                ))}
              </select>
            </label>
            <label className="ns-field ns-field--compact">
              <span>Model</span>
              <select value={state.selectedModel} onChange={(event) => state.setSelectedModel(event.target.value)}>
                {state.chatModels.map((model) => (
                  <option key={model.name} value={model.name}>{model.name}</option>
                ))}
              </select>
            </label>
            <button type="button" className="ns-button ns-button--ghost" onClick={state.handleNewThread}>New Thread</button>
          </div>

          <div className="ns-discourse-stream">
            {state.messages.length === 0 ? (
              <div className="ns-empty-chat">
                <strong>Intervention hazir</strong>
                <p>Secili local model ile bu panelden canli konusma baslatabilirsin.</p>
              </div>
            ) : (
              state.messages.map((message) => (
                <article key={message.id} className={`ns-message-card ${message.role}`}>
                  <div className="ns-message-card__head">
                    <strong>{message.role === 'assistant' ? state.selectedModel || 'Assistant' : 'Operator'}</strong>
                    <span>{formatClock(message.createdAt)}</span>
                  </div>
                  <p>{message.content}</p>
                </article>
              ))
            )}
          </div>

          <div className="ns-discourse-composer">
            <textarea value={state.chatInput} onChange={(event) => state.setChatInput(event.target.value)} rows={4} placeholder="Direct intervention input..." />
            <div className="ns-discourse-composer__footer">
              <div className="ns-discourse-links">
                <button type="button">Attach Schema</button>
                <button type="button">Agent Context</button>
              </div>
              <button type="button" className="ns-button ns-button--primary" onClick={state.handleSendMessage} disabled={state.chatBusy}>
                {state.chatBusy ? 'Sending...' : 'Send'}
              </button>
            </div>
          </div>
        </aside>
      </div>
    </div>
  )
}
