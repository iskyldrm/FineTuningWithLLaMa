import { Navigate, Route, Routes } from 'react-router-dom'
import { useApexConsole } from './app/useApexConsole'
import { AppShell } from './components/AppShell'
import { AgentsPage } from './pages/AgentsPage'
import { ChatsPage } from './pages/ChatsPage'
import { OverviewPage } from './pages/OverviewPage'
import { PermissionsPage } from './pages/PermissionsPage'
import { SwarmsPage } from './pages/SwarmsPage'
import { TasksPage } from './pages/TasksPage'
import { ToolsPage } from './pages/ToolsPage'

export default function App() {
  const state = useApexConsole()

  return (
    <AppShell state={state}>
      <Routes>
        <Route path="/" element={<Navigate to="/overview" replace />} />
        <Route path="/overview" element={<OverviewPage state={state} />} />
        <Route path="/tasks" element={<TasksPage state={state} />} />
        <Route path="/swarms" element={<SwarmsPage state={state} />} />
        <Route path="/chats" element={<ChatsPage state={state} />} />
        <Route path="/agents" element={<AgentsPage state={state} />} />
        <Route path="/permissions" element={<PermissionsPage state={state} />} />
        <Route path="/tools" element={<ToolsPage state={state} />} />
        <Route path="/home" element={<Navigate to="/overview" replace />} />
        <Route path="/monitoring" element={<Navigate to="/overview" replace />} />
        <Route path="/dashboard" element={<Navigate to="/overview" replace />} />
        <Route path="/workflows" element={<Navigate to="/swarms" replace />} />
        <Route path="/execution" element={<Navigate to="/swarms" replace />} />
        <Route path="*" element={<Navigate to="/overview" replace />} />
      </Routes>
    </AppShell>
  )
}
