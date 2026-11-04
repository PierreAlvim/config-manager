export const config = {
  oidcAuthority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:8080/realms/config-manager',
  oidcClientId: import.meta.env.VITE_OIDC_CLIENT_ID ?? 'config-manager-web',
  apiBase: import.meta.env.VITE_API_BASE ?? '',
}
