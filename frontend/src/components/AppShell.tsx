import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import type { AppRoute } from '../types'
import type { ApexConsoleState } from '../app/useApexConsole'

const navItems: Array<{ route: AppRoute; label: string; icon: string }> = [
  { route: 'overview', label: 'Overview', icon: 'home' },
  { route: 'tasks', label: 'Tasks', icon: 'add_task' },
  { route: 'swarms', label: 'Swarms', icon: 'hub' },
  { route: 'agents', label: 'Agents', icon: 'smart_toy' },
  { route: 'permissions', label: 'Permissions', icon: 'shield' },
  { route: 'tools', label: 'Tools', icon: 'build' },
  { route: 'chats', label: 'Chats', icon: 'chat_bubble' },
]

const routeMeta: Record<AppRoute, { title: string; description: string }> = {
  overview: { title: 'Overview', description: 'Active run summary, recent runs, and system state.' },
  tasks: { title: 'Tasks', description: 'Create direct tasks, pick a repo, or dispatch a board item.' },
  swarms: { title: 'Swarms', description: 'Live run detail, delegation flow, and patch review.' },
  agents: { title: 'Agents', description: 'Registry, availability, and current role capabilities.' },
  permissions: { title: 'Permissions', description: 'Role execution policy and delegation boundaries.' },
  tools: { title: 'Tools', description: 'Tool registry and usable-role visibility.' },
  chats: { title: 'Chats', description: 'Dedicated assistant conversations outside run execution.' },
}

type AppShellProps = {
  state: ApexConsoleState
  children: ReactNode
}

function normalizeRoute(pathname: string): AppRoute {
  const route = pathname.replace(/^\/+/, '').split('/')[0]
  if (route === 'dashboard' || route === 'home' || route === 'monitoring') {
    return 'overview'
  }

  if (route === 'workflows' || route === 'execution') {
    return 'swarms'
  }

  return (routeMeta[route as AppRoute] ? route : 'overview') as AppRoute
}

export function AppShell({ state, children }: AppShellProps) {
  const location = useLocation()
  const currentRoute = normalizeRoute(location.pathname)
  const meta = routeMeta[currentRoute]
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [mobileNavOpen, setMobileNavOpen] = useState(false)

  useEffect(() => {
    setMobileNavOpen(false)
  }, [location.pathname])

  return (
    <div className={`shell${sidebarCollapsed ? ' is-collapsed' : ''}${mobileNavOpen ? ' is-mobile-open' : ''}`}>
      <button type="button" className="shell-backdrop" aria-label="Close navigation" onClick={() => setMobileNavOpen(false)} />

      <aside className="shell-sidebar">
        <div className="shell-brand">
          <div className="shell-brand__mark">A</div>
          <div className="shell-brand__copy">
            <strong>Apex Operator</strong>
            <span>Swarm control</span>
          </div>
        </div>

        <nav className="shell-nav" aria-label="Primary">
          {navItems.map((item) => (
            <NavLink key={item.route} to={`/${item.route}`} className={({ isActive }) => `shell-nav__item${isActive ? ' is-active' : ''}`}>
              <span className="material-symbols-outlined">{item.icon}</span>
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="shell-sidebar__footer">
          <div className="shell-context">
            <span>Selected repo</span>
            <strong>{state.selectedRepository?.fullName ?? state.currentRun.selectedRepository?.fullName ?? 'No repository'}</strong>
          </div>
          <div className="shell-inline-stats">
            <span>{state.connected ? 'Live' : 'Polling'}</span>
            <span>Queue {state.overview.system.logicalQueueDepth}</span>
          </div>
        </div>
      </aside>

      <section className="shell-main">
        <header className="shell-topbar">
          <div className="shell-topbar__left">
            <button type="button" className="shell-icon-button shell-mobile-toggle" aria-label="Open navigation" onClick={() => setMobileNavOpen(true)}>
              <span className="material-symbols-outlined">menu</span>
            </button>
            <button
              type="button"
              className="shell-icon-button shell-desktop-toggle"
              aria-label={sidebarCollapsed ? 'Expand navigation' : 'Collapse navigation'}
              onClick={() => setSidebarCollapsed((current) => !current)}
            >
              <span className="material-symbols-outlined">{sidebarCollapsed ? 'left_panel_open' : 'left_panel_close'}</span>
            </button>
            <div className="shell-heading">
              <h1>{meta.title}</h1>
              <p>{meta.description}</p>
            </div>
          </div>

          <div className="shell-topbar__right">
            <div className="shell-pill">{state.connected ? 'Connected' : 'Offline polling'}</div>
            <div className="shell-pill">{state.overview.agents.length} agents</div>
            <div className="shell-pill">{state.currentRun.swarmTemplate}</div>
          </div>
        </header>

        <main className="shell-content">
          {state.error ? <div className="global-banner">{state.error}</div> : null}
          {children}
        </main>
      </section>
    </div>
  )
}
