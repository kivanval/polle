import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useQueries } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, History, Pencil, Plus, Search, Table2 } from 'lucide-react'
import { catalogOptionsQuery, PAGE_SIZE, useCatalogChanges, useCatalogRows, useCatalogTable, useCatalogTables,
  type CatalogOption, type CatalogRow, type CatalogTable, type CatalogTableSummary } from '@/features/catalog/api'
import { ago, cell, changedFields, columnLabel, optionLabels, rowName } from '@/features/catalog/format'
import { RowEditor } from '@/features/catalog/RowEditor'
import { problemMessage } from '@/features/farms/api'
import '@/styles/catalog.css'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'

const GROUP_ORDER = ['Crops', 'Operations', 'Machinery and labour', 'Market']

/** Expert data: the solver's reference tables, editable by any signed-in user, with every change logged. */
export function CatalogPage() {
  const tables = useCatalogTables()
  const [params, setParams] = useSearchParams()
  const selected = params.get('table') ?? tables.data?.[0]?.name ?? ''
  const view = params.get('view') === 'changes' ? 'changes' : 'rows'
  const choose = (name: string, next = view) => setParams(name ? { table: name, ...(next === 'changes' ? { view: next } : {}) } : {}, { replace: false })

  if (tables.isPending) return <div className="atlas-state"><TractorLoader /><p>Loading expert data…</p></div>
  if (tables.error) return <div className="atlas-state"><h1>Expert data didn’t load.</h1><p>{problemMessage(tables.error)}</p>
    <button className="button button-dark" onClick={() => void tables.refetch()}>Try again</button></div>

  const groups = GROUP_ORDER.map(group => ({ group, items: tables.data.filter(table => table.group === group) })).filter(entry => entry.items.length)
  return <div className="catalog">
    <div className="workspace-heading"><h1>Expert data</h1>
      <p>The reference data the planner works from: crops and their growing profiles, operations, machinery, labour and market prices.
        Every change is saved straight away and recorded in the change log under your name or a shortened email.</p></div>
    <div className="catalog-layout">
      <nav className="catalog-nav" aria-label="Tables">
        {groups.map(({ group, items }) => <div key={group} className="catalog-nav-group">
          <h2>{group}</h2>
          {items.map(table => <button key={table.name} type="button" aria-current={table.name === selected ? 'page' : undefined} onClick={() => choose(table.name, 'rows')}>
            <span>{table.title}</span><span className="catalog-count">{table.rows}</span></button>)}
        </div>)}
      </nav>
      {selected && <TablePanel key={selected} name={selected} summaries={tables.data} view={view} onView={next => choose(selected, next)} />}
    </div>
  </div>
}

function TablePanel({ name, summaries, view, onView }: { name: string; summaries: CatalogTableSummary[]; view: 'rows' | 'changes'; onView: (view: 'rows' | 'changes') => void }) {
  const table = useCatalogTable(name)
  const titles = useMemo(() => Object.fromEntries(summaries.map(summary => [summary.name, summary.title])), [summaries])
  const summary = summaries.find(entry => entry.name === name)
  if (!summary) return <section className="catalog-main"><p className="catalog-empty">This table isn’t available.</p></section>
  return <section className="catalog-main" aria-labelledby="catalog-table-title">
    <header className="catalog-head">
      <div><h2 id="catalog-table-title">{summary.title}</h2><p>{summary.description}</p>
        {table.data && <Dependents table={table.data} titles={titles} />}</div>
      <div className="catalog-tabs" role="tablist" aria-label="View">
        <button type="button" role="tab" aria-selected={view === 'rows'} onClick={() => onView('rows')}><Table2 size={15} />Rows</button>
        <button type="button" role="tab" aria-selected={view === 'changes'} onClick={() => onView('changes')}><History size={15} />Change log</button>
      </div>
    </header>
    {table.isPending ? <div className="schedule-state" role="status"><TractorLoader compact />Loading columns…</div>
      : table.error ? <p className="form-error" role="alert">{problemMessage(table.error)}</p>
      : view === 'rows' ? <Rows table={table.data} titles={titles} /> : <Changes table={table.data} />}
  </section>
}

function Dependents({ table, titles }: { table: CatalogTable; titles: Record<string, string> }) {
  const cascade = table.referencedBy.filter(ref => ref.onDelete === 'cascade').map(ref => titles[ref.table] ?? ref.table)
  const blocking = table.referencedBy.filter(ref => ref.onDelete === 'restrict').map(ref => titles[ref.table] ?? ref.table.replace(/_/g, ' '))
  if (!cascade.length && !blocking.length) return null
  return <p className="catalog-dependents">
    {cascade.length > 0 && <>Deleting a row also deletes its linked {[...new Set(cascade)].join(', ').toLowerCase()}. </>}
    {blocking.length > 0 && <>Rows used by {[...new Set(blocking)].join(', ').toLowerCase()} can’t be deleted.</>}
  </p>
}

function Rows({ table, titles }: { table: CatalogTable; titles: Record<string, string> }) {
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const [editing, setEditing] = useState<CatalogRow | 'new' | null>(null)
  const [notice, setNotice] = useState('')
  const rows = useCatalogRows(table.name, search, page)
  const linkTables = [...new Set(table.columns.flatMap(column => (column.link ? [column.link.table] : [])))]
  const optionQueries = useQueries({ queries: linkTables.map(catalogOptionsQuery) })
  const links: Record<string, CatalogOption[]> = Object.fromEntries(linkTables.map((linked, index) => [linked, optionQueries[index].data ?? []]))
  const labels = new Map(linkTables.map(linked => [linked, optionLabels(links[linked])]))

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(input.trim()); setPage(0) }, 300)
    return () => window.clearTimeout(timer)
  }, [input])

  const total = rows.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const visible = table.columns.filter(column => column.kind !== 'json' || table.columns.length < 6)

  return <>
    <div className="catalog-toolbar">
      <label className="catalog-search"><Search size={15} aria-hidden="true" /><span className="visually-hidden">Search {table.title}</span>
        <input type="search" value={input} placeholder={`Search ${table.title.toLowerCase()}`} onChange={event => setInput(event.target.value)} /></label>
      {rows.isFetching && <TractorSpinner size={16} label="Loading" />}
      <button type="button" className="button button-dark" onClick={() => { setNotice(''); setEditing('new') }}><Plus size={16} />Add row</button>
    </div>
    {notice && <p className="catalog-notice" role="status">{notice}</p>}
    {rows.error ? <p className="form-error" role="alert">{problemMessage(rows.error)}</p>
      : rows.data && rows.data.rows.length === 0 ? <p className="catalog-empty">{search ? `Nothing matches “${search}”.` : 'No rows yet. Add the first one.'}</p>
      : <div className="catalog-grid-wrap">
          <table className="catalog-grid">
            <thead><tr>{visible.map(column => { const { label, unit } = columnLabel(column.name)
              return <th key={column.name} scope="col" className={column.kind === 'integer' || column.kind === 'decimal' ? 'is-numeric' : undefined}>
                {label}{unit && <span className="catalog-unit">{unit}</span>}</th> })}<th scope="col"><span className="visually-hidden">Actions</span></th></tr></thead>
            <tbody>{rows.data?.rows.map(row => <tr key={JSON.stringify(table.key.map(column => row.values[column]))} onClick={() => setEditing(row)}>
              {visible.map(column => {
                const shown = cell(column, row.values[column.name], column.link ? labels.get(column.link.table) : undefined)
                return <td key={column.name} className={[shown.numeric && 'is-numeric', shown.muted && 'is-muted'].filter(Boolean).join(' ') || undefined}>
                  {shown.clip ? <span className="catalog-clip" title={shown.text}>{shown.text}</span> : shown.text}{shown.sub &&<span className="catalog-sub">{shown.sub}</span>}</td>
              })}
              <td className="catalog-row-action"><button type="button" className="button button-quiet button-icon" aria-label={`Edit ${row.label || 'row'}`}
                onClick={event => { event.stopPropagation(); setEditing(row) }}><Pencil size={14} /></button></td>
            </tr>)}</tbody>
          </table>
        </div>}
    {total > PAGE_SIZE && <nav className="catalog-pages" aria-label="Pages">
      <button type="button" className="button button-quiet button-icon" aria-label="Previous page" disabled={page === 0} onClick={() => setPage(page - 1)}><ChevronLeft size={15} /></button>
      <span>{page * PAGE_SIZE + 1}–{Math.min(total, (page + 1) * PAGE_SIZE)} of {total}</span>
      <button type="button" className="button button-quiet button-icon" aria-label="Next page" disabled={page + 1 >= pages} onClick={() => setPage(page + 1)}><ChevronRight size={15} /></button>
    </nav>}
    {editing && <RowEditor table={table} row={editing === 'new' ? null : editing} links={links} titles={titles}
      onClose={() => setEditing(null)} onSaved={message => { setEditing(null); setNotice(message) }} />}
  </>
}

const ACTIONS = { insert: 'Added', update: 'Changed', delete: 'Deleted' } as const

function Changes({ table }: { table: CatalogTable }) {
  const changes = useCatalogChanges(table.name)
  if (changes.isPending) return <div className="schedule-state" role="status"><TractorLoader compact />Loading changes…</div>
  if (changes.error) return <p className="form-error" role="alert">{problemMessage(changes.error)}</p>
  if (changes.data.length === 0) return <p className="catalog-empty">No one has changed {table.title.toLowerCase()} here yet.</p>
  return <ol className="catalog-log">
    {changes.data.map(change => {
      const fields = change.action === 'update' ? changedFields(change.before, change.after) : []
      return <li key={change.id} className={`is-${change.action}`}>
        <div className="catalog-log-head">
          <span className="catalog-log-action">{ACTIONS[change.action]}</span>
          <strong>{rowName(change.after ?? change.before, change.key)}</strong>
          <span className="catalog-log-meta">{change.byYou ? 'you' : change.editor ?? 'removed account'} · <time dateTime={change.changedAtUtc} title={new Date(change.changedAtUtc).toLocaleString()}>{ago(change.changedAtUtc)}</time></span>
        </div>
        {fields.length > 0 && <ul className="catalog-log-fields">{fields.map(({ field, from, to }) => <li key={field}>
          <span className="mono">{field}</span> <del>{from}</del> → <ins>{to}</ins></li>)}</ul>}
        {change.cascade && <p className="catalog-log-cascade">Also removed: {Object.entries(change.cascade).map(([other, count]) => `${count} ${other.replace(/_/g, ' ')}`).join(', ')}.</p>}
      </li>
    })}
  </ol>
}
