import type {
  AiRubikCheckResponse,
  AiRubikHealthResponse,
  AiRubikScannerPreviewResponse,
  AiRubikScannerSessionResponse,
} from '../types';

type HealthArgs = {
  backendUrl: string;
  token: string;
};

type UploadAiSnapshotArgs = {
  backendUrl: string;
  token: string;
  matchId: string;
  snapshot: Blob;
  checkType: 'pre-check' | 'scramble-check' | 'finish-check';
  scrambleSequence?: string;
};

export async function fetchAiHealth(args: HealthArgs): Promise<AiRubikHealthResponse> {
  const response = await fetch(`${args.backendUrl.replace(/\/$/, '')}/api/online/ai/health`, {
    headers: buildAuthHeaders(args.token),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `AI health check failed with HTTP ${response.status}.`);
  }

  return response.json();
}

export async function uploadAiSnapshot(args: UploadAiSnapshotArgs): Promise<AiRubikCheckResponse> {
  const form = new FormData();
  form.append('snapshot', args.snapshot, `${args.checkType}.jpg`);
  if (args.scrambleSequence?.trim()) {
    form.append('scrambleSequence', args.scrambleSequence.trim());
  }

  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/${toEndpoint(args.checkType)}`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
      body: form,
    },
  );

  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `AI snapshot upload failed with HTTP ${response.status}.`);
  }

  return response.json();
}

function toEndpoint(checkType: UploadAiSnapshotArgs['checkType']) {
  switch (checkType) {
    case 'pre-check':
      return 'ai/pre-check';
    case 'scramble-check':
      return 'ai/scramble-check';
    case 'finish-check':
      return 'ai/finish-check';
    default:
      throw new Error(`Unsupported AI check type: ${checkType satisfies never}`);
  }
}

function buildAuthHeaders(token: string): HeadersInit {
  return token.trim()
    ? { Authorization: `Bearer ${token.trim()}` }
    : {};
}

export async function fetchScannerTestHealth(backendUrl: string): Promise<AiRubikHealthResponse> {
  const response = await fetch(`${backendUrl.replace(/\/$/, '')}/api/dev/ai/scanner-test/health`);
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Scanner test health failed with HTTP ${response.status}.`);
  }
  return response.json();
}

export async function startScannerTestSession(backendUrl: string): Promise<AiRubikScannerSessionResponse> {
  const response = await fetch(`${backendUrl.replace(/\/$/, '')}/api/dev/ai/scanner-test/sessions`, {
    method: 'POST',
  });
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Scanner test session start failed with HTTP ${response.status}.`);
  }
  return response.json();
}

export async function getScannerTestSession(args: {
  backendUrl: string;
  sessionId: string;
}): Promise<AiRubikScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/dev/ai/scanner-test/sessions/${args.sessionId}`,
  );
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Scanner test session fetch failed with HTTP ${response.status}.`);
  }
  return response.json();
}

export async function observeScannerTestFrame(args: {
  backendUrl: string;
  sessionId: string;
  snapshot: Blob;
  scanSessionId: string;
  scanGeneration: number;
  requestId: string;
  targetFaceIndex: number;
  signal?: AbortSignal;
}): Promise<AiRubikScannerPreviewResponse> {
  const form = new FormData();
  form.append('snapshot', args.snapshot, 'preview.jpg');
  form.append('scanSessionId', args.scanSessionId);
  form.append('scanGeneration', String(args.scanGeneration));
  form.append('requestId', args.requestId);
  form.append('targetFaceIndex', String(args.targetFaceIndex));
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/dev/ai/scanner-test/sessions/${args.sessionId}/observe`,
    {
      method: 'POST',
      body: form,
      signal: args.signal,
    },
  );
  if (!response.ok && response.status !== 429) {
    const body = await response.text();
    throw new Error(body || `Scanner observe failed with HTTP ${response.status}.`);
  }
  return response.json();
}

export async function retryScannerTestFace(args: {
  backendUrl: string;
  sessionId: string;
}): Promise<AiRubikScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/dev/ai/scanner-test/sessions/${args.sessionId}/retry-face`,
    {
      method: 'POST',
    },
  );
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Scanner retry failed with HTTP ${response.status}.`);
  }
  return response.json();
}

export async function resetScannerTestSession(args: {
  backendUrl: string;
  sessionId: string;
}): Promise<AiRubikScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/dev/ai/scanner-test/sessions/${args.sessionId}/reset`,
    {
      method: 'POST',
    },
  );
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `Scanner reset failed with HTTP ${response.status}.`);
  }
  return response.json();
}
