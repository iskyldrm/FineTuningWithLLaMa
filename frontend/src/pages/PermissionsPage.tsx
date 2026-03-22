import type { ApexConsoleState } from '../app/useApexConsole'
import { roleMeta } from '../app/view-models'
import { agentRoles } from '../types'

export function PermissionsPage({ state }: { state: ApexConsoleState }) {
  return (
    <div className="page-stack">
      <section className="content-grid">
        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Policy</p>
              <h2>Role runtime policy</h2>
            </div>
            <button type="button" className="button" onClick={state.handleSavePolicy} disabled={state.runtimeBusy}>
              {state.runtimeBusy ? 'Saving...' : 'Save policy'}
            </button>
          </div>

          <div className="form-grid">
            <label className="field">
              <span>Role</span>
              <select value={state.selectedPolicyRole} onChange={(event) => state.setSelectedPolicyRole(event.target.value as (typeof agentRoles)[number])}>
                {agentRoles.map((role) => (
                  <option key={role} value={role}>
                    {roleMeta[role].title}
                  </option>
                ))}
              </select>
            </label>

            <label className="field">
              <span>Execution mode</span>
              <select value={state.policyDraft.executionMode} onChange={(event) => state.setPolicyDraft((current) => ({ ...current, executionMode: event.target.value as 'StructuredPrompt' | 'ToolLoop' }))}>
                <option value="StructuredPrompt">StructuredPrompt</option>
                <option value="ToolLoop">ToolLoop</option>
              </select>
            </label>

            <label className="field">
              <span>Max steps</span>
              <input type="number" min={1} max={24} value={state.policyDraft.maxSteps} onChange={(event) => state.setPolicyDraft((current) => ({ ...current, maxSteps: Number(event.target.value) || 1 }))} />
            </label>
          </div>

          <label className="field">
            <span>Writable roots</span>
            <textarea value={state.policyDraft.writableRoots} onChange={(event) => state.setPolicyDraft((current) => ({ ...current, writableRoots: event.target.value }))} rows={5} />
          </label>
        </article>

        <article className="panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">Delegation</p>
              <h2>Allowed agents</h2>
            </div>
          </div>

          <div className="toggle-grid">
            {agentRoles.filter((role) => role !== state.selectedPolicyRole).map((role) => (
              <button
                key={role}
                type="button"
                className={`toggle-card${state.policyDraft.allowedDelegates.includes(role) ? ' is-selected' : ''}`}
                onClick={() => state.togglePolicyDelegate(role)}
              >
                <strong>{roleMeta[role].title}</strong>
                <p>{roleMeta[role].subtitle}</p>
                <span>{state.policyDraft.allowedDelegates.includes(role) ? 'Allowed' : 'Blocked'}</span>
              </button>
            ))}
          </div>
        </article>
      </section>

      <section className="panel">
        <div className="section-head">
          <div>
            <p className="eyebrow">Tool access</p>
            <h2>Allowed tools</h2>
          </div>
        </div>

        <div className="toggle-grid">
          {state.runtimeCatalog.tools.map((tool) => (
            <label key={tool.name} className={`toggle-card${state.policyDraft.allowedTools.includes(tool.name) ? ' is-selected' : ''}`}>
              <input type="checkbox" checked={state.policyDraft.allowedTools.includes(tool.name)} onChange={() => state.togglePolicyTool(tool.name)} />
              <strong>{tool.displayName}</strong>
              <p>{tool.description}</p>
              <span>{tool.type}</span>
            </label>
          ))}
        </div>
      </section>
    </div>
  )
}
