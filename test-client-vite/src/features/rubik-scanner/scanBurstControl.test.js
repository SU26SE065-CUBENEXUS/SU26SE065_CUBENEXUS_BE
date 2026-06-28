import test from 'node:test'
import assert from 'node:assert/strict'

import { runScannerBurst, shouldStopBurst } from './scanBurstControl.js'

test('stops burst after accepted observation and keeps one request in flight', async () => {
  let concurrent = 0
  let maxConcurrent = 0
  let captures = 0
  const states = ['SCANNING', 'STABLE', 'ACCEPTED']
  let index = 0
  let nowValue = 0

  const result = await runScannerBurst({
    capture: async () => {
      captures += 1
      concurrent += 1
      maxConcurrent = Math.max(maxConcurrent, concurrent)
      return `frame-${captures}`
    },
    observe: async () => {
      const state = states[index++]
      concurrent -= 1
      nowValue += 350
      return { scannerState: state }
    },
    delay: async (ms) => {
      nowValue += ms
    },
    shouldAbort: () => false,
    maxBurstMs: 5000,
    sampleIntervalMs: 350,
    now: () => nowValue,
  })

  assert.equal(result.reason, 'terminal')
  assert.equal(result.observation.scannerState, 'ACCEPTED')
  assert.equal(captures, 3)
  assert.equal(maxConcurrent, 1)
})

test('times out when no terminal observation is reached', async () => {
  let nowValue = 0
  let captures = 0

  const result = await runScannerBurst({
    capture: async () => {
      captures += 1
      return captures
    },
    observe: async () => {
      nowValue += 350
      return { scannerState: 'SCANNING' }
    },
    delay: async (ms) => {
      nowValue += ms
    },
    shouldAbort: () => false,
    maxBurstMs: 1000,
    sampleIntervalMs: 350,
    now: () => nowValue,
  })

  assert.equal(result.reason, 'timeout')
  assert.ok(captures >= 1)
})

test('aborts cleanly without queueing stale frames', async () => {
  let shouldAbort = false
  let captures = 0
  let nowValue = 0

  const resultPromise = runScannerBurst({
    capture: async () => {
      captures += 1
      return captures
    },
    observe: async () => {
      shouldAbort = true
      nowValue += 350
      return { scannerState: 'SCANNING' }
    },
    delay: async (ms) => {
      nowValue += ms
    },
    shouldAbort: () => shouldAbort,
    maxBurstMs: 5000,
    sampleIntervalMs: 350,
    now: () => nowValue,
  })

  const result = await resultPromise
  assert.equal(result.reason, 'aborted')
  assert.equal(captures, 1)
})

test('recognizes terminal scanner states', () => {
  assert.equal(shouldStopBurst('ACCEPTED'), true)
  assert.equal(shouldStopBurst('RETRY'), true)
  assert.equal(shouldStopBurst('SCANNING'), false)
})
