import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { inlineParts, messageBlocks } from './messageBlocks'
import { MessageText } from './MessageText'

const digest = [
  '### Plans for North · 2027-01 – 2031-12',
  '**Period:** from 2027-01 for 5 years (to 2031-12).',
  '**Ready:** 3 of 4',
  '',
  '**Best options**',
  '1. **Full inputs**: profit **$9,000** (+$7,000 vs your plan)',
  '   - Costs $1,000 · irrigation 300 mm · 2 risks',
  '   - Crops: Maize → Soybean',
  '2. **Control**: profit **$5,000**',
  '',
  '**Unavailable / In progress**',
  '- Still calculating: Cash grab',
].join('\n')

describe('assistant message blocks', () => {
  it('reads a heading, line-broken paragraphs and a numbered list with sub-items', () => {
    const blocks = messageBlocks(digest)
    expect(blocks.map(block => block.kind)).toEqual(['heading', 'paragraph', 'paragraph', 'list', 'paragraph', 'list'])
    expect(blocks[0]).toEqual({ kind: 'heading', text: 'Plans for North · 2027-01 – 2031-12' })
    expect(blocks[1]).toEqual({ kind: 'paragraph', lines: ['**Period:** from 2027-01 for 5 years (to 2031-12).', '**Ready:** 3 of 4'] })
    expect(blocks[3]).toEqual({ kind: 'list', ordered: true, items: [
      { text: '**Full inputs**: profit **$9,000** (+$7,000 vs your plan)', children: ['Costs $1,000 · irrigation 300 mm · 2 risks', 'Crops: Maize → Soybean'] },
      { text: '**Control**: profit **$5,000**', children: [] },
    ] })
    expect(blocks[5]).toEqual({ kind: 'list', ordered: false, items: [{ text: 'Still calculating: Cash grab', children: [] }] })
  })

  it('keeps plain text from a model as one paragraph per block', () => {
    expect(messageBlocks('Crop rotation is growing different crops in turn.\n\nIt keeps soil healthy.'))
      .toEqual([{ kind: 'paragraph', lines: ['Crop rotation is growing different crops in turn.'] }, { kind: 'paragraph', lines: ['It keeps soil healthy.'] }])
  })

  it('splits bold and italic runs and leaves stray markers as text', () => {
    expect(inlineParts('I’d choose **Control**: *estimated*')).toEqual([{ text: 'I’d choose ' }, { text: 'Control', bold: true }, { text: ': ' }, { text: 'estimated', italic: true }])
    expect(inlineParts('2 * 3 = 6, a*b')).toEqual([{ text: '2 * 3 = 6, a*b' }])
  })

  it('renders elements, never raw HTML from the text', () => {
    const html = renderToStaticMarkup(<MessageText text={`${digest}\n<script>alert(1)</script>`} />)
    expect(html).toContain('<h4 class="assistant-text-heading">')
    expect(html).toContain('<ol><li><strong>Full inputs</strong>')
    expect(html).toContain('<ul><li><span>Costs $1,000')
    expect(html).toContain('<br/>')
    expect(html).not.toContain('<script>')
    expect(html).toContain('&lt;script&gt;')
  })
})
