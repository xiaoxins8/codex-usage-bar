import { clamp, integer } from './format.js'

// The free strip of the Codex composer row, in DIPs at 100 % scaling: from the left buttons
// (+, auto-approve) to the context ring next to the model button. Capsule height is 30.
export const STRIP = { left: 157, right: 663 }
export const STRIP_WIDTH = STRIP.right - STRIP.left
export const OFFSET_X = { min: -240, max: 240 }
export const OFFSET_Y = { min: -32, max: 24 }

// Anchor for each stored alignment (0 right, 1 center, 2 left), as BarSettings.Place in src/Settings.cs.
export function anchorFor(alignment, width) {
  const left = STRIP.left, right = Math.max(left, STRIP.right - width)
  return alignment === 1 ? left + (right - left) / 2 : alignment === 2 ? left : right
}

export function place(alignment, offsetX, width) {
  const left = STRIP.left, right = Math.max(left, STRIP.right - width)
  const anchor = anchorFor(alignment, width)
  return { x: clamp(anchor + integer(offsetX), left, right), anchor, left, right }
}

// Offset that puts the capsule's left edge at x, limited to what the overlay can actually reach.
export function offsetFor(alignment, x, width) {
  const { anchor, left, right } = place(alignment, 0, width)
  const low = Math.max(OFFSET_X.min, Math.ceil(left - anchor)), high = Math.min(OFFSET_X.max, Math.floor(right - anchor))
  return clamp(Math.round(x - anchor), low, high)
}
