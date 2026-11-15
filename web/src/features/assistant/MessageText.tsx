import { Fragment } from 'react'
import { inlineParts, messageBlocks } from './messageBlocks'

/** One line with its **bold** and *italic* runs; each run in its own element, so a page translator can't strand React's text. */
function Inline({ text }: { text: string }) {
  return <>{inlineParts(text).map((part, index) => part.bold ? <strong key={index}>{part.text}</strong>
    : part.italic ? <em key={index}>{part.text}</em> : <span key={index}>{part.text}</span>)}</>
}

/**
 * An assistant message drawn from its Markdown: a heading, paragraphs with their line breaks, and lists with their
 * sub-lists, instead of one run of text. Rendered as React elements only, never as HTML.
 */
export function MessageText({ text }: { text: string }) {
  return <div className="assistant-text">{messageBlocks(text).map((block, index) => {
    if (block.kind === 'heading') return <h4 key={index} className="assistant-text-heading"><Inline text={block.text} /></h4>
    if (block.kind === 'paragraph') return <p key={index}>{block.lines.map((line, row) =>
      <Fragment key={row}>{row > 0 && <br />}<Inline text={line} /></Fragment>)}</p>
    const List = block.ordered ? 'ol' : 'ul'
    return <List key={index}>{block.items.map((item, row) => <li key={row}><Inline text={item.text} />
      {item.children.length > 0 && <ul>{item.children.map((child, sub) => <li key={sub}><Inline text={child} /></li>)}</ul>}</li>)}</List>
  })}</div>
}
