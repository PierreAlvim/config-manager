import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from 'next-themes'
import { Toaster } from '@/components/ui/sonner'
import { TooltipProvider } from '@/components/ui/tooltip'
import { AuthGate } from '@/auth/AuthGate'
import { ApiError } from '@/api/client'
import { AppLayout } from '@/components/AppLayout'
import NamespacesPage from '@/pages/NamespacesPage'
import NamespacePage from '@/pages/NamespacePage'
import FlagPage from '@/pages/FlagPage'
import ChangeRequestsPage from '@/pages/ChangeRequestsPage'
import ChangeRequestPage from '@/pages/ChangeRequestPage'
import HistoryPage from '@/pages/HistoryPage'
import SettingsPage from '@/pages/SettingsPage'
import AuditPage from '@/pages/AuditPage'
import NotFoundPage from '@/pages/NotFoundPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (count, err) => !(err instanceof ApiError && err.status >= 400 && err.status < 500) && count < 2,
      refetchOnWindowFocus: false,
      staleTime: 10_000,
    },
  },
})

export default function App() {
  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
      <TooltipProvider>
        <AuthGate>
          <QueryClientProvider client={queryClient}>
            <BrowserRouter>
              <Routes>
                <Route element={<AppLayout />}>
                  <Route index element={<NamespacesPage />} />
                  <Route path="namespaces/:ns" element={<NamespacePage />} />
                  <Route path="namespaces/:ns/flags/:key" element={<FlagPage />} />
                  <Route path="namespaces/:ns/history" element={<HistoryPage />} />
                  <Route path="namespaces/:ns/settings" element={<SettingsPage />} />
                  <Route path="change-requests" element={<ChangeRequestsPage />} />
                  <Route path="change-requests/:id" element={<ChangeRequestPage />} />
                  <Route path="audit" element={<AuditPage />} />
                  <Route path="*" element={<NotFoundPage />} />
                </Route>
              </Routes>
            </BrowserRouter>
          </QueryClientProvider>
        </AuthGate>
        <Toaster richColors closeButton />
      </TooltipProvider>
    </ThemeProvider>
  )
}
