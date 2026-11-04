import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from 'react-oidc-context'
import { useTheme } from 'next-themes'
import { Flag, FolderKanban, GitPullRequest, LogOut, Moon, ScrollText, Sun } from 'lucide-react'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
} from '@/components/ui/sidebar'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Separator } from '@/components/ui/separator'
import { useMe } from '@/api/me'
import { useNamespaces } from '@/api/namespaces'
import { usePendingCount } from '@/api/changeRequests'
import { RoleBadge } from '@/components/common/badges'

function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme()
  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label="Toggle theme"
      onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}
    >
      <Sun className="hidden size-4 dark:block" />
      <Moon className="size-4 dark:hidden" />
    </Button>
  )
}

function UserMenu() {
  const auth = useAuth()
  const { data: me } = useMe()
  const name = me?.name || me?.username || auth.user?.profile.name || auth.user?.profile.preferred_username || 'User'
  const initials = name
    .split(/\s+/)
    .map((p) => p[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="ghost" className="h-9 gap-2 px-2" />}>
        <Avatar className="size-6">
          <AvatarFallback className="text-[10px]">{initials}</AvatarFallback>
        </Avatar>
        <span className="hidden max-w-32 truncate text-sm sm:inline">{name}</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        <DropdownMenuLabel className="space-y-0.5">
          <div className="text-sm font-medium text-foreground">{name}</div>
          {me?.email && <div className="text-xs font-normal text-muted-foreground">{me.email}</div>}
          {me?.isAdmin && <div className="text-xs font-normal text-emerald-600">Global administrator</div>}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={() => void auth.signoutRedirect()}>
          <LogOut /> Log out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function AppSidebar() {
  const { pathname } = useLocation()
  const { data: namespaces } = useNamespaces()
  const { data: pending } = usePendingCount()
  const nav = [
    { to: '/', label: 'Namespaces', icon: FolderKanban, match: (p: string) => p === '/' },
    { to: '/change-requests', label: 'Change requests', icon: GitPullRequest, match: (p: string) => p.startsWith('/change-requests'), badge: pending },
    { to: '/audit', label: 'Audit log', icon: ScrollText, match: (p: string) => p.startsWith('/audit') },
  ]
  return (
    <Sidebar>
      <SidebarHeader>
        <Link to="/" className="flex items-center gap-2 px-2 py-1.5">
          <div className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <Flag className="size-4" />
          </div>
          <div className="leading-tight">
            <div className="text-sm font-semibold">Config Manager</div>
            <div className="text-xs text-muted-foreground">Flags &amp; config</div>
          </div>
        </Link>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>Workspace</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {nav.map((n) => (
                <SidebarMenuItem key={n.to}>
                  <SidebarMenuButton isActive={n.match(pathname)} render={<NavLink to={n.to} />}>
                    <n.icon />
                    <span>{n.label}</span>
                  </SidebarMenuButton>
                  {n.badge ? <SidebarMenuBadge data-testid="pending-count">{n.badge}</SidebarMenuBadge> : null}
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
        <SidebarGroup>
          <SidebarGroupLabel>Namespaces</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {(namespaces ?? []).map((ns) => (
                <SidebarMenuItem key={ns.key}>
                  <SidebarMenuButton
                    isActive={pathname.startsWith(`/namespaces/${ns.key}`)}
                    render={<NavLink to={`/namespaces/${ns.key}`} />}
                  >
                    <Flag />
                    <span>{ns.name || ns.key}</span>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter className="px-4 pb-3 text-xs text-muted-foreground">OpenFeature compatible</SidebarFooter>
    </Sidebar>
  )
}

export function AppLayout() {
  const { data: me } = useMe()
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="sticky top-0 z-20 flex h-14 items-center gap-2 border-b bg-background/80 px-4 backdrop-blur">
          <SidebarTrigger />
          <Separator orientation="vertical" className="mx-1 h-5" />
          <div className="flex-1" />
          {me?.isAdmin && <RoleBadge role="owner" />}
          <ThemeToggle />
          <UserMenu />
        </header>
        <main className="mx-auto w-full max-w-7xl flex-1 p-4 md:p-6">
          <Outlet />
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
