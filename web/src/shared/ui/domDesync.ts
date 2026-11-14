/**
 * True for the errors React throws when something else (Google Translate, a browser extension) replaced or moved
 * DOM nodes it still owns: "NotFoundError: Failed to execute 'insertBefore' / 'removeChild' on 'Node'".
 */
export function isDomDesyncError(error: unknown): boolean {
  if (!(error instanceof Error)) return false
  return error.name === 'NotFoundError' || /\b(insertBefore|removeChild)\b.*\bnot a child\b/i.test(error.message)
    || /The node (before which the new node is to be inserted|to be removed) is not a child of this node/i.test(error.message)
}
