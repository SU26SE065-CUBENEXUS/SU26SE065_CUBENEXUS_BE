function buildAuthHeaders(token) {
  if (!token.trim()) {
    throw new Error('JWT token is required.');
  }

  return {
    Authorization: `Bearer ${token.trim()}`,
  };
}

async function readJsonOrThrow(response) {
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `HTTP ${response.status}`);
  }

  return response.json();
}

export async function createMatchRecordingUploadUrl(args) {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/matches/${args.matchId}/recording/upload-url`,
    {
      method: 'POST',
      headers: {
        ...buildAuthHeaders(args.token),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        contentType: args.contentType,
        fileExtension: args.fileExtension,
        durationSeconds: args.durationSeconds,
        recordedAt: args.recordedAt,
      }),
    },
  );

  return readJsonOrThrow(response);
}

export async function markMatchRecordingStarted(args) {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/matches/${args.matchId}/recording/started`,
    {
      method: 'POST',
      headers: {
        ...buildAuthHeaders(args.token),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        recordingStartedAt: args.recordingStartedAt,
        mimeType: args.mimeType,
      }),
    },
  );

  return readJsonOrThrow(response);
}

export async function completeMatchRecordingUpload(args) {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/matches/${args.matchId}/recording/complete`,
    {
      method: 'POST',
      headers: {
        ...buildAuthHeaders(args.token),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        objectKey: args.objectKey,
        durationSeconds: args.durationSeconds,
      }),
    },
  );

  return readJsonOrThrow(response);
}

export async function getMatchRecordingPlaybackUrls(args) {
  const response = await fetch(
    `${args.backendUrl.replace(/\/$/, '')}/api/matches/${args.matchId}/recording/playback-url`,
    {
      headers: buildAuthHeaders(args.token),
    },
  );

  return readJsonOrThrow(response);
}

export function uploadRecordingBlob({ uploadUrl, contentType, blob, onProgress }) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', uploadUrl, true);
    xhr.setRequestHeader('Content-Type', contentType);

    xhr.upload.onprogress = (event) => {
      if (!event.lengthComputable) {
        return;
      }
      onProgress?.(Math.round((event.loaded / event.total) * 100));
    };

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve();
        return;
      }
      reject(new Error(xhr.responseText || `Upload failed with HTTP ${xhr.status}`));
    };

    xhr.onerror = () => reject(new Error('Network error while uploading recording.'));
    xhr.onabort = () => reject(new Error('Recording upload was aborted.'));
    xhr.send(blob);
  });
}
