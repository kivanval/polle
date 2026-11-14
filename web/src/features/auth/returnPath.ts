/** Where to go after signing in: the protected page that sent the user here, else the overview. */
export function returnPath(state: unknown) {
  const from = (state as { from?: unknown } | null)?.from
  // Only same-site paths; never an absolute or protocol-relative URL (browsers read "/\" like "//").
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') && !from.includes('\\') ? from : '/dashboard'
}
