import type { InferenceSession } from 'onnxruntime-web';
import type { FrameDetectionResult } from '../types';
import { VideoFramePreprocessor } from './preprocess';
import { postprocessYoloOutput } from './postprocess';
import { buildGridFromDetections } from './gridBuilder';

export class YoloRubikDetector {
  private readonly preprocessor: VideoFramePreprocessor;

  constructor(
    private readonly session: InferenceSession,
    private readonly inputName: string,
    private readonly confidenceThreshold = 0.35,
    private readonly iouThreshold = 0.45,
    inputSize = 640,
  ) {
    this.preprocessor = new VideoFramePreprocessor(inputSize);
  }

  async detect(video: HTMLVideoElement): Promise<FrameDetectionResult> {
    if (video.videoWidth === 0 || video.videoHeight === 0) {
      return { ok: false, averageConfidence: 0, detectedStickers: 0, reason: 'Video is not ready.' };
    }

    const input = this.preprocessor.preprocess(video);
    const outputs = await this.session.run({ [this.inputName]: input.tensor });
    const outputTensor = Object.values(outputs)[0];
    const detections = postprocessYoloOutput({
      output: outputTensor.data as Float32Array,
      dims: outputTensor.dims,
      confidenceThreshold: this.confidenceThreshold,
      iouThreshold: this.iouThreshold,
      scale: input.scale,
      padX: input.padX,
      padY: input.padY,
    });

    return buildGridFromDetections(detections, video.videoWidth, video.videoHeight);
  }
}
