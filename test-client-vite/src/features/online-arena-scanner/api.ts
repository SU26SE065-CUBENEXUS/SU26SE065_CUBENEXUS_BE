export type OnlineArenaScannerFace = {
  faceIndex: number;
  faceCode: string;
  expectedCenterColor: string;
  observedCenterColor?: string | null;
  grid3x3?: string[][] | null;
  acceptedAt: string;
};

export type OnlineArenaScannerValidation = {
  status: string;
  matched: boolean;
  matchedStickerCount: number;
  mismatchedStickerCount: number;
  playerStatus: string;
  mismatches: Array<{
    face: string;
    row: number;
    column: number;
    expected: string;
    observed: string;
  }>;
};

export type OnlineArenaScannerSessionResponse = {
  message: string;
  matchId: string;
  playerId: string;
  validationType: string;
  scanSessionId: string;
  aiSessionId: string;
  scanGeneration: number;
  scanStatus: string;
  scannerState: string;
  matchStatus: string;
  requestedFaceIndex: number;
  requestedFaceCode: string;
  requestedFaceLabel: string;
  requestedCenterColor: string;
  capturedFaceCount: number;
  requestId?: string | null;
  stableObservationCount: number;
  requiredStableObservations: number;
  detectedStickers: number;
  confidence: number;
  inferMs: number;
  decodeMs: number;
  preprocessMs: number;
  postprocessMs: number;
  totalMs: number;
  reason?: string | null;
  observedCenterColor?: string | null;
  grid3x3?: string[][] | null;
  faces: OnlineArenaScannerFace[];
  validation?: OnlineArenaScannerValidation | null;
};

export type OnlineArenaMatchDetail = {
  id: string;
  statusCode: string;
  roomToken: string;
  qrSessionCode?: string | null;
  player1Id: string;
  player2Id: string;
  player1Ready: boolean;
  player2Ready: boolean;
  player1ScrambleCheckStatus: string;
  player2ScrambleCheckStatus: string;
  player1FinishCheckStatus: string;
  player2FinishCheckStatus: string;
  player1ResultStatus: string;
  player2ResultStatus: string;
  playerScrambleSequence?: string | null;
  scrambleSequence?: string | null;
  startedAt?: string | null;
  scrambleRevealedAt?: string | null;
  endedAt?: string | null;
  reviewReasonJson?: string | null;
  timeLimitMs: number;
};

function buildAuthHeaders(token: string) {
  if (!token.trim()) {
    throw new Error('JWT token is required.');
  }

  return {
    Authorization: `Bearer ${token.trim()}`,
  };
}

async function readJsonOrThrow(response: Response) {
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `HTTP ${response.status}`);
  }

  return response.json();
}

export async function getOnlineMatchDetail(args: {
  backendUrl: string;
  token: string;
  matchId: string;
}): Promise<OnlineArenaMatchDetail> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}`,
    {
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}

export async function startOnlineArenaScannerSession(args: {
  backendUrl: string;
  token: string;
  matchId: string;
  validationType: 'scramble' | 'finish';
}): Promise<OnlineArenaScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/scanner/${args.validationType}/start`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}

export async function getOnlineArenaScannerSession(args: {
  backendUrl: string;
  token: string;
  matchId: string;
  validationType: 'scramble' | 'finish';
}): Promise<OnlineArenaScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/scanner/${args.validationType}`,
    {
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}

export async function observeOnlineArenaScannerFrame(args: {
  backendUrl: string;
  token: string;
  matchId: string;
  validationType: 'scramble' | 'finish';
  scanSessionId: string;
  scanGeneration: number;
  requestId: string;
  targetFaceIndex: number;
  snapshot: Blob;
  signal?: AbortSignal;
}): Promise<OnlineArenaScannerSessionResponse> {
  const form = new FormData();
  form.append('snapshot', args.snapshot, 'frame.jpg');
  form.append('scanSessionId', args.scanSessionId);
  form.append('scanGeneration', String(args.scanGeneration));
  form.append('requestId', args.requestId);
  form.append('targetFaceIndex', String(args.targetFaceIndex));

  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/scanner/${args.validationType}/observe`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
      body: form,
      signal: args.signal,
    },
  );
  return readJsonOrThrow(response);
}

export async function retryOnlineArenaScannerFace(args: {
  backendUrl: string;
  token: string;
  matchId: string;
  validationType: 'scramble' | 'finish';
}): Promise<OnlineArenaScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/scanner/${args.validationType}/retry-face`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}

export async function resetOnlineArenaScannerSession(args: {
  backendUrl: string;
  token: string;
  matchId: string;
  validationType: 'scramble' | 'finish';
}): Promise<OnlineArenaScannerSessionResponse> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/scanner/${args.validationType}/reset`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}

export async function reconcileOnlineMatchStatus(args: {
  backendUrl: string;
  token: string;
  matchId: string;
}): Promise<OnlineArenaMatchDetail> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/reconcile-status`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}

export async function mockOnlineMatchFinishPass(args: {
  backendUrl: string;
  token: string;
  matchId: string;
}): Promise<OnlineArenaMatchDetail> {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/online/matches/${args.matchId}/dev/mock-finish-pass`,
    {
      method: 'POST',
      headers: buildAuthHeaders(args.token),
    },
  );
  return readJsonOrThrow(response);
}
