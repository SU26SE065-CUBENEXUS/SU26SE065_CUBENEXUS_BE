export const TERMINAL_SCANNER_STATES = new Set([
  'ACCEPTED',
  'DUPLICATE_FACE',
  'RETRY',
  'AI_BUSY',
  'AI_UNAVAILABLE',
  'CAMERA_ERROR',
])

export function shouldStopBurst(scannerState) {
  return TERMINAL_SCANNER_STATES.has(scannerState)
}

export async function runScannerBurst({
  capture,
  observe,
  delay,
  onObservation,
  shouldStop,
  shouldAbort,
  maxBurstMs,
  sampleIntervalMs,
  now,
}) {
  const startedAt = now()
  const stopPredicate = shouldStop ?? ((observation) => shouldStopBurst(observation.scannerState))

  while (!shouldAbort() && (now() - startedAt) < maxBurstMs) {
    const snapshot = await capture()
    const observation = await observe(snapshot)
    onObservation?.(observation)
    if (stopPredicate(observation)) {
      return { reason: 'terminal', observation }
    }

    await delay(sampleIntervalMs)
  }

  return { reason: shouldAbort() ? 'aborted' : 'timeout', observation: null }
}
