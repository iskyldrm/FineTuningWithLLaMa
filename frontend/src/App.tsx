import { Navigate, Route, Routes } from 'react-router-dom'
import { useApexConsole } from './app/useApexConsole'
import { AppShell } from './components/AppShell'
import { AgentsPage } from './pages/AgentsPage'
import { DashboardPage } from './pages/DashboardPage'
import { ExecutionPage } from './pages/ExecutionPage'
import { WorkflowsPage } from './pages/WorkflowsPage'

export default function App() {
  const state = useApexConsole()

  return (
    <AppShell state={state}>
      {state.error ? <div className="ns-global-banner">{state.error}</div> : null}
      <Routes>
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="/dashboard" element={<DashboardPage state={state} />} />
        <Route path="/agents" element={<AgentsPage state={state} />} />
        <Route path="/workflows" element={<WorkflowsPage state={state} />} />
        <Route path="/execution" element={<ExecutionPage state={state} />} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </AppShell>
  )
}
