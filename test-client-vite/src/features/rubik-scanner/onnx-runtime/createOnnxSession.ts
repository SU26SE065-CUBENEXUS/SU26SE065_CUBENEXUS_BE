import * as ort from 'onnxruntime-web';

export type OnnxRuntimeSession = {
  session: ort.InferenceSession;
  executionProvider: string;
};

const PROVIDERS = ['webgpu', 'webgl', 'wasm'] as const;

export async function createOnnxSession(modelUrl: string): Promise<OnnxRuntimeSession> {
  ort.env.wasm.numThreads = Math.max(1, Math.min(4, navigator.hardwareConcurrency || 1));
  ort.env.wasm.proxy = true; // Use Web Worker to prevent UI blocking

  for (const provider of PROVIDERS) {
    try {
      const session = await ort.InferenceSession.create(modelUrl, {
        executionProviders: [provider],
        graphOptimizationLevel: 'all',
      });
      return { session, executionProvider: provider };
    } catch {
      continue;
    }
  }

  throw new Error('No ONNX Runtime Web execution provider is available.');
}

export { ort };
