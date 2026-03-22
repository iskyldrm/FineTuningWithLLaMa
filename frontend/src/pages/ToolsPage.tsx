import type { ApexConsoleState } from '../app/useApexConsole'
import type { AgentToolType } from '../types'
import { roleMeta } from '../app/view-models'

const toolTypeOptions: AgentToolType[] = ['ListFiles', 'ReadFile', 'WriteFile', 'SearchCode', 'RunTerminal', 'GitStatus', 'GitDiff', 'GitCommit', 'GitPush', 'CustomCommand']

export function ToolsPage({ state }: { state: ApexConsoleState }) {
  const usableBy = new Map(
    state.runtimeCatalog.tools.map((tool) => [
      tool.name,
      state.runtimeCatalog.policies.filter((policy) => policy.allowedTools.includes(tool.name)).map((policy) => policy.role),
    ]),
  )

  return (
    <div className="page-stack">
      <section className="content-grid">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Registry</p>
              <h2>Available tools</h2>
            </div>
          </div>

          <div className="tool-list">
            {state.runtimeCatalog.tools.map((tool) => (
              <article key={tool.name} className="tool-card">
                <div className="tool-card__head">
                  <div>
                    <strong>{tool.displayName}</strong>
                    <p>{tool.name}</p>
                  </div>
                  <button
                    type="button"
                    className="button button--secondary"
                    onClick={() => state.setToolForm({
                      name: tool.name,
                      displayName: tool.displayName,
                      description: tool.description,
                      type: tool.type,
                      enabled: tool.enabled,
                      destructive: tool.destructive,
                      commandTemplate: tool.commandTemplate ?? '',
                    })}
                  >
                    Edit
                  </button>
                </div>
                <p>{tool.description || 'No description provided.'}</p>
                <div className="chip-row">
                  <span className="chip">{tool.type}</span>
                  <span className="chip">{tool.enabled ? 'Enabled' : 'Disabled'}</span>
                  {tool.destructive ? <span className="chip">Destructive</span> : null}
                </div>
                <div className="usable-by">
                  <span>Usable by</span>
                  <div className="chip-row">
                    {(usableBy.get(tool.name) ?? []).length > 0 ? (
                      (usableBy.get(tool.name) ?? []).map((role) => (
                        <span key={`${tool.name}-${role}`} className="chip">
                          {roleMeta[role].title}
                        </span>
                      ))
                    ) : (
                      <span className="muted-text">No role has access yet.</span>
                    )}
                  </div>
                </div>
              </article>
            ))}
          </div>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Editor</p>
              <h2>Create or update tool</h2>
            </div>
          </div>

          <div className="form-grid">
            <label className="field">
              <span>Name</span>
              <input value={state.toolForm.name} onChange={(event) => state.setToolForm((current) => ({ ...current, name: event.target.value }))} />
            </label>

            <label className="field">
              <span>Display name</span>
              <input value={state.toolForm.displayName} onChange={(event) => state.setToolForm((current) => ({ ...current, displayName: event.target.value }))} />
            </label>

            <label className="field">
              <span>Type</span>
              <select value={state.toolForm.type} onChange={(event) => state.setToolForm((current) => ({ ...current, type: event.target.value as AgentToolType }))}>
                {toolTypeOptions.map((type) => (
                  <option key={type} value={type}>
                    {type}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <label className="field">
            <span>Description</span>
            <textarea value={state.toolForm.description} onChange={(event) => state.setToolForm((current) => ({ ...current, description: event.target.value }))} rows={4} />
          </label>

          <label className="field">
            <span>Command template</span>
            <textarea value={state.toolForm.commandTemplate} onChange={(event) => state.setToolForm((current) => ({ ...current, commandTemplate: event.target.value }))} rows={4} />
          </label>

          <div className="inline-switches">
            <label className="checkbox">
              <input type="checkbox" checked={state.toolForm.enabled} onChange={(event) => state.setToolForm((current) => ({ ...current, enabled: event.target.checked }))} />
              <span>Enabled</span>
            </label>
            <label className="checkbox">
              <input type="checkbox" checked={state.toolForm.destructive} onChange={(event) => state.setToolForm((current) => ({ ...current, destructive: event.target.checked }))} />
              <span>Destructive</span>
            </label>
          </div>

          <div className="button-row">
            <button type="button" className="button" onClick={state.handleSaveTool} disabled={state.runtimeBusy}>
              {state.runtimeBusy ? 'Saving...' : 'Save tool'}
            </button>
          </div>
        </article>
      </section>
    </div>
  )
}
