/**
 * The small Markdown the assistant writes, read into blocks the chat draws: headings (#, ## or ###), paragraphs whose
 * lines keep their breaks, bulleted lists (-, * or •) and numbered lists (1.), each item with an optional indented
 * sub-list. Inline **bold** and *italic* are split out by `inlineParts`. Anything else stays plain text: nothing is read
 * as HTML.
 */
export type ListItem = { text: string; children: string[] }
export type MessageBlock =
  | { kind: 'heading'; text: string }
  | { kind: 'paragraph'; lines: string[] }
  | { kind: 'list'; ordered: boolean; items: ListItem[] }

const HEADING = /^#{1,3}\s+(.*)$/
const BULLET = /^[-*•]\s+(.*)$/
const NUMBERED = /^\d{1,3}[.)]\s+(.*)$/

export function messageBlocks(text: string): MessageBlock[] {
  const blocks: MessageBlock[] = []
  let paragraph: string[] | null = null
  let list: Extract<MessageBlock, { kind: 'list' }> | null = null
  const close = () => { paragraph = null; list = null }

  for (const raw of text.replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trimEnd()
    if (!line.trim()) { close(); continue }
    const indented = /^\s{2,}/.test(line)
    const body = line.trim()
    const heading = HEADING.exec(body)
    const bullet = BULLET.exec(body), numbered = NUMBERED.exec(body)
    const itemText = (bullet ?? numbered)?.[1]

    if (heading && !indented) { close(); blocks.push({ kind: 'heading', text: heading[1].trim() }); continue }
    if (itemText !== undefined) {
      const current = list as Extract<MessageBlock, { kind: 'list' }> | null
      // An indented item under an item becomes its sub-item.
      if (indented && current?.items.length) { current.items.at(-1)!.children.push(itemText); continue }
      const ordered = Boolean(numbered) && !bullet
      if (!current || current.ordered !== ordered) {
        paragraph = null
        list = { kind: 'list', ordered, items: [] }
        blocks.push(list)
      }
      list!.items.push({ text: itemText, children: [] })
      continue
    }
    // A plain line right under a list item continues that item.
    const current = list as Extract<MessageBlock, { kind: 'list' }> | null
    if (current?.items.length && indented) { current.items.at(-1)!.text += ` ${body}`; continue }
    list = null
    if (!paragraph) { paragraph = []; blocks.push({ kind: 'paragraph', lines: paragraph }) }
    ;(paragraph as string[]).push(body)
  }
  return blocks
}

export type InlinePart = { text: string; bold?: boolean; italic?: boolean }

/** A line split into plain, **bold** and *italic* runs; an unmatched marker stays as text. */
export function inlineParts(text: string): InlinePart[] {
  const parts: InlinePart[] = []
  const pattern = /\*\*(.+?)\*\*|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])/g
  let last = 0
  for (const match of text.matchAll(pattern)) {
    if (match.index > last) parts.push({ text: text.slice(last, match.index) })
    parts.push(match[1] !== undefined ? { text: match[1], bold: true } : { text: match[2], italic: true })
    last = match.index + match[0].length
  }
  if (last < text.length) parts.push({ text: text.slice(last) })
  return parts
}
