import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '@/shared/api/client'

export type ColumnKind = 'integer' | 'decimal' | 'boolean' | 'text' | 'date' | 'timestamp' | 'enum' | 'json' | 'translations' | 'unsupported'
export type CatalogColumn = {
  name: string; kind: ColumnKind; dbType: string; nullable: boolean; hasDefault: boolean; generated: boolean
  maxLength: number | null; options: string[] | null; link: { table: string; column: string } | null; key: boolean
}
export type CatalogTableSummary = { name: string; title: string; group: string; description: string; rows: number }
export type CatalogTable = Omit<CatalogTableSummary, 'rows'> & {
  key: string[]; columns: CatalogColumn[]; uniques: { name: string; columns: string[] }[]; checks: string[]
  referencedBy: { table: string; columns: string[]; onDelete: 'cascade' | 'restrict' | 'set null' | 'set default' }[]
}
export type RowValues = Record<string, unknown>
export type CatalogRow = { values: RowValues; label: string }
export type CatalogOption = { key: string | number; label: string }
export type CatalogChange = {
  id: number; table: string; action: 'insert' | 'update' | 'delete'; key: RowValues; before: RowValues | null; after: RowValues | null
  cascade: Record<string, number> | null; editor: string | null; byYou: boolean; changedAtUtc: string
}

export const PAGE_SIZE = 50
const keys = {
  all: ['catalog'] as const,
  tables: ['catalog', 'tables'] as const,
  table: (name: string) => ['catalog', 'table', name] as const,
  rows: (name: string, search: string, page: number) => ['catalog', 'rows', name, search, page] as const,
  options: (name: string) => ['catalog', 'options', name] as const,
  changes: (name?: string) => ['catalog', 'changes', name ?? 'all'] as const,
}
const base = (table: string) => `/catalog/${encodeURIComponent(table)}`

export const useCatalogTables = () => useQuery({ queryKey: keys.tables, queryFn: () => apiFetch<CatalogTableSummary[]>('/catalog') })
export const useCatalogTable = (name: string) => useQuery({ queryKey: keys.table(name), queryFn: () => apiFetch<CatalogTable>(base(name)), enabled: Boolean(name) })

export function useCatalogRows(name: string, search: string, page: number) {
  const query = new URLSearchParams({ offset: String(page * PAGE_SIZE), limit: String(PAGE_SIZE), ...(search ? { search } : {}) })
  return useQuery({
    queryKey: keys.rows(name, search, page), enabled: Boolean(name), placeholderData: keepPreviousData,
    queryFn: () => apiFetch<{ total: number; rows: CatalogRow[] }>(`${base(name)}/rows?${query}`),
  })
}

/** Key and label of every row of a linked table, for pickers and for showing names instead of ids. */
export const catalogOptionsQuery = (name: string) =>
  ({ queryKey: keys.options(name), queryFn: () => apiFetch<CatalogOption[]>(`${base(name)}/options`), staleTime: 60_000 })

export const useCatalogChanges = (name?: string) => useQuery({
  queryKey: keys.changes(name),
  queryFn: () => apiFetch<CatalogChange[]>(`/catalog/changes?limit=100${name ? `&table=${encodeURIComponent(name)}` : ''}`),
})

export function useSaveCatalogRow(name: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ key, values }: { key?: RowValues; values: RowValues }) =>
      apiFetch<CatalogRow>(`${base(name)}/rows`, { method: key ? 'PUT' : 'POST', body: JSON.stringify(key ? { key, values } : { values }) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  })
}

export function useDeleteCatalogRow(name: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ key, preview }: { key: RowValues; preview: boolean }) =>
      apiFetch<{ deleted: boolean; cascade: Record<string, number> }>(`${base(name)}/rows/delete`, { method: 'POST', body: JSON.stringify({ key, preview }) }),
    // A delete can cascade into other tables, so refresh everything the page shows.
    onSuccess: (result) => { if (result.deleted) void queryClient.invalidateQueries({ queryKey: keys.all }) },
  })
}

export const keyOf = (table: CatalogTable, values: RowValues): RowValues => Object.fromEntries(table.key.map(column => [column, values[column]]))
