import type { ApexConsoleState } from '../app/useApexConsole'
import { formatDateTime } from '../app/view-models'

export function ChatsPage({ state }: { state: ApexConsoleState }) {
  return (
    <div className="page-stack">
      <section className="chat-layout">
        <aside className="panel chat-sidebar">
          <div className="section-head">
            <div>
              <p className="eyebrow">Threads</p>
              <h2>Chats</h2>
            </div>
            <button type="button" className="button button--secondary" onClick={state.handleNewThread}>
              New chat
            </button>
          </div>

          <div className="list-stack">
            {state.threads.length > 0 ? (
              state.threads.map((thread) => (
                <button key={thread.id} type="button" className={`list-card${state.selectedThreadId === thread.id ? ' is-active' : ''}`} onClick={() => state.setSelectedThreadId(thread.id)}>
                  <div>
                    <strong>{thread.title}</strong>
                    <p>{thread.model}</p>
                  </div>
                  <small>{formatDateTime(thread.updatedAt)}</small>
                </button>
              ))
            ) : (
              <div className="empty-state">
                <strong>No chats yet</strong>
                <p>Create a thread to start a separate assistant conversation.</p>
              </div>
            )}
          </div>
        </aside>

        <section className="panel chat-panel">
          <div className="chat-panel__header">
            <div>
              <p className="eyebrow">Conversation</p>
              <h2>{state.selectedThread?.title ?? 'New chat'}</h2>
            </div>

            <label className="field field--compact">
              <span>Model</span>
              <select value={state.selectedModel} onChange={(event) => state.setSelectedModel(event.target.value)}>
                {state.chatModels.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="chat-stream">
            {state.messages.length > 0 ? (
              state.messages.map((message) => (
                <article key={message.id} className={`message-card is-${message.role}`}>
                  <div className="message-card__head">
                    <strong>{message.role === 'assistant' ? message.model || state.selectedModel || 'Assistant' : 'You'}</strong>
                    <span>{formatDateTime(message.createdAt)}</span>
                  </div>
                  <p>{message.content}</p>
                </article>
              ))
            ) : (
              <div className="empty-state empty-state--large">
                <strong>Chat page is separate by design</strong>
                <p>Thread history, model selection, and conversation stay isolated from live run execution.</p>
              </div>
            )}
          </div>

          <div className="chat-composer">
            <textarea value={state.chatInput} onChange={(event) => state.setChatInput(event.target.value)} rows={5} placeholder="Write a message" />
            <div className="chat-composer__footer">
              <span>{state.selectedModel || 'No model selected'}</span>
              <button type="button" className="button" onClick={state.handleSendMessage} disabled={state.chatBusy}>
                {state.chatBusy ? 'Sending...' : 'Send'}
              </button>
            </div>
          </div>
        </section>
      </section>
    </div>
  )
}
