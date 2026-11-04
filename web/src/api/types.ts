export type Role = 'viewer' | 'editor' | 'approver' | 'owner'
export const ROLES: Role[] = ['viewer', 'editor', 'approver', 'owner']
export type FlagType = 'boolean' | 'string' | 'integer' | 'double' | 'object'
export const FLAG_TYPES: FlagType[] = ['boolean', 'string', 'integer', 'double', 'object']

export type ConditionOperator =
  | 'equals'
  | 'notEquals'
  | 'in'
  | 'notIn'
  | 'contains'
  | 'startsWith'
  | 'endsWith'
  | 'greaterThan'
  | 'greaterThanOrEquals'
  | 'lessThan'
  | 'lessThanOrEquals'

export const OPERATORS: ConditionOperator[] = [
  'equals',
  'notEquals',
  'in',
  'notIn',
  'contains',
  'startsWith',
  'endsWith',
  'greaterThan',
  'greaterThanOrEquals',
  'lessThan',
  'lessThanOrEquals',
]

export type JsonValue = string | number | boolean | null | JsonValue[] | { [k: string]: JsonValue }

export interface Condition {
  attribute: string
  operator: ConditionOperator
  values: string[]
}
export interface WeightedVariant {
  variant: string
  weight: number
}
export interface TargetingRule {
  name?: string | null
  conditions: Condition[]
  variant?: string | null
  rollout?: WeightedVariant[] | null
}
export interface FlagDefinition {
  type: FlagType
  enabled: boolean
  variants: Record<string, JsonValue>
  defaultVariant: string
  rules: TargetingRule[]
  metadata?: Record<string, string>
}
export interface ConfigSnapshot {
  namespace: string
  environment: string
  version: number
  publishedAt: string
  flags: Record<string, FlagDefinition>
}

export interface Me {
  username: string
  name?: string | null
  email?: string | null
  isAdmin: boolean
  groups: string[]
  namespaceRoles: Record<string, Role>
}
export interface Environment {
  key: string
  name: string
  sortOrder: number
}
export interface Namespace {
  key: string
  name: string
  description?: string | null
  myRole?: Role | null
}
export interface Policy {
  environment: string
  requireChangeRequest: boolean
  requiredApprovals: number
}
export interface Member {
  id: string
  subjectType: 'user' | 'group'
  subject: string
  role: Role
}
export interface ServiceAccess {
  id: string
  clientId: string
  environment: string
}
export interface FlagEnvSummary {
  enabled: boolean
  defaultVariant: string
  rulesCount: number
}
export interface FlagSummary {
  key: string
  name: string
  description?: string | null
  type: FlagType
  environments: Record<string, FlagEnvSummary | null>
}
export interface FlagDetail {
  key: string
  name: string
  description?: string | null
  type: FlagType
  configs: Record<string, FlagDefinition | null>
}

export type Operation =
  | { type: 'upsert'; flagKey: string; flag: FlagDefinition }
  | { type: 'delete'; flagKey: string }

export interface SubmitChangeBody {
  title: string
  description?: string
  forceChangeRequest?: boolean
  operations: Operation[]
}

export type CrStatus = 'pending' | 'applied' | 'rejected' | 'cancelled' | 'conflict'
export interface Review {
  reviewer: string
  decision: 'approved' | 'rejected'
  comment?: string | null
  at: string
}
export interface DiffEntry {
  flagKey: string
  type: 'upsert' | 'delete'
  before: FlagDefinition | null
  after: FlagDefinition | null
}
export interface ChangeRequestSummary {
  id: string
  namespace: string
  environment: string
  title: string
  description?: string | null
  status: CrStatus
  author: string
  createdAt: string
  baseVersion?: number | null
  appliedVersion?: number | null
  requiredApprovals: number
  operations?: Operation[]
  reviews?: Review[]
}
export interface ChangeRequestDetail extends ChangeRequestSummary {
  operations: Operation[]
  reviews: Review[]
  diff: DiffEntry[]
  canApprove: boolean
  canCancel: boolean
}

export type ChangeOutcome =
  | { outcome: 'applied'; version: number }
  | { outcome: 'changeRequestCreated'; changeRequest: ChangeRequestSummary }

export interface VersionInfo {
  version: number
  publishedAt: string
  publishedBy: string
  message?: string | null
  changeRequestId?: string | null
}
export interface VersionDetail extends VersionInfo {
  snapshot: ConfigSnapshot
}
export interface AuditEntry {
  at: string
  actor: string
  action: string
  namespace?: string | null
  environment?: string | null
  details?: string | null
}

export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: unknown
}
