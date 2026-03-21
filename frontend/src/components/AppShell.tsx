import { NavLink, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import type { AppRoute } from '../types'
import type { ApexConsoleState } from '../app/useApexConsole'

const navItems: Array<{ route: AppRoute; label: string; description: string; icon: string }> = [
  { route: 'dashboard', label: 'Dashboard', description: 'Neural workspace', icon: 'dashboard' },
  { route: 'agents', label: 'Agents', description: 'Registry ve constructor', icon: 'smart_toy' },
  { route: 'workflows', label: 'Workflows', description: 'Node orchestration', icon: 'account_tree' },
  { route: 'execution', label: 'Execution', description: 'Live pulse terminal', icon: 'terminal' },
]

const routeMeta: Record<AppRoute, { title: string; search: string }> = {
  dashboard: { title: 'Neural Workspace', search: 'Search substrate...' },
  agents: { title: 'Agent Orchestration', search: 'Global node search...' },
  workflows: { title: 'Workflow Orchestration', search: 'Search workflow graph...' },
  execution: { title: 'Execution Pulse', search: 'Search execution history...' },
}

type AppShellProps = {
  state: ApexConsoleState
  children: ReactNode
}

export function AppShell({ state, children }: AppShellProps) {
  const location = useLocation()
  const currentRoute = ((location.pathname.replace('/', '') || 'dashboard') as AppRoute)
  const meta = routeMeta[currentRoute] ?? routeMeta.dashboard

  return (
    <div className="ns-shell">
      <aside className="ns-sidebar">
        <div className="ns-brand">
          <div className="ns-brand__mark">
            <span className="material-symbols-outlined">hub</span>
          </div>
          <div>
            <h1>APEX Neural Substrate</h1>
            <p>Quantum Terminal v3.0</p>
          </div>
        </div>

        <nav className="ns-sidebar__nav">
          {navItems.map((item) => (
            <NavLink
              key={item.route}
              to={`/${item.route}`}
              className={({ isActive }) => `ns-nav ${isActive ? 'is-active' : ''}`}
            >
              <span className="material-symbols-outlined">{item.icon}</span>
              <span className="ns-nav__copy">
                <strong>{item.label}</strong>
                <small>{item.description}</small>
              </span>
            </NavLink>
          ))}
        </nav>

        <div className="ns-sidebar__cta">
          <NavLink to="/agents" className="ns-button ns-button--primary">
            <span className="material-symbols-outlined">rocket_launch</span>
            Deploy Agent
          </NavLink>
        </div>

        <div className="ns-sidebar__footer">
          <button type="button" className="ns-footer-link">
            <span className="material-symbols-outlined">database</span>
            <span>System Logs</span>
          </button>
          <button type="button" className="ns-footer-link">
            <span className="material-symbols-outlined">settings</span>
            <span>Settings</span>
          </button>
        </div>
      </aside>

      <section className="ns-mainframe">
        <header className="ns-topbar">
          <div className="ns-topbar__left">
            <label className="ns-search">
              <span className="material-symbols-outlined">search</span>
              <input type="text" placeholder={meta.search} />
            </label>
            <nav className="ns-health-tabs" aria-label="System tabs">
              <button type="button" className="is-active">Health</button>
              <button type="button">Latency</button>
              <button type="button">Uptime</button>
            </nav>
          </div>

          <div className="ns-topbar__right">
            <div className="ns-topbar__badge">
              <span>Queue</span>
              <strong>{state.dashboard.logicalQueueDepth}</strong>
            </div>
            <button type="button" className="ns-icon-button" aria-label="Notifications">
              <span className="material-symbols-outlined">notifications</span>
            </button>
            <button type="button" className="ns-icon-button" aria-label="Controls">
              <span className="material-symbols-outlined">settings_input_component</span>
            </button>
            <div className="ns-avatar-card">
              <div>
                <strong>{meta.title}</strong>
                <small>{state.selectedRepository?.fullName ?? state.selectedModel ?? 'Local control mesh'}</small>
              </div>
              <div className="ns-avatar">A</div>
            </div>
          </div>
        </header>

        <div className="ns-page-frame">{children}</div>
      </section>
    </div>
  )
}
