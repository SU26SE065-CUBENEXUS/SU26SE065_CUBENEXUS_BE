import { ort } from '../onnx-runtime/createOnnxSession';

export type PreprocessResult = {
  tensor: ort.Tensor;
  scale: number;
  padX: number;
  padY: number;
  inputSize: number;
};

export class VideoFramePreprocessor {
  private readonly canvas: HTMLCanvasElement;
  private readonly context: CanvasRenderingContext2D;

  constructor(private readonly inputSize = 640) {
    this.canvas = document.createElement('canvas');
    this.canvas.width = inputSize;
    this.canvas.height = inputSize;

    const context = this.canvas.getContext('2d', { willReadFrequently: true });
    if (!context) {
      throw new Error('Canvas 2D context is not available.');
    }

    this.context = context;
  }

  preprocess(video: HTMLVideoElement): PreprocessResult {
    const sourceWidth = video.videoWidth;
    const sourceHeight = video.videoHeight;
    const scale = Math.min(this.inputSize / sourceWidth, this.inputSize / sourceHeight);
    const width = Math.round(sourceWidth * scale);
    const height = Math.round(sourceHeight * scale);
    const padX = Math.floor((this.inputSize - width) / 2);
    const padY = Math.floor((this.inputSize - height) / 2);

    this.context.fillStyle = 'rgb(114,114,114)';
    this.context.fillRect(0, 0, this.inputSize, this.inputSize);
    this.context.drawImage(video, 0, 0, sourceWidth, sourceHeight, padX, padY, width, height);

    const image = this.context.getImageData(0, 0, this.inputSize, this.inputSize).data;
    const planeSize = this.inputSize * this.inputSize;
    const data = new Float32Array(3 * planeSize);
    for (let index = 0; index < planeSize; index += 1) {
      data[index] = image[index * 4] / 255;
      data[planeSize + index] = image[index * 4 + 1] / 255;
      data[2 * planeSize + index] = image[index * 4 + 2] / 255;
    }

    return {
      tensor: new ort.Tensor('float32', data, [1, 3, this.inputSize, this.inputSize]),
      scale,
      padX,
      padY,
      inputSize: this.inputSize,
    };
  }
}
