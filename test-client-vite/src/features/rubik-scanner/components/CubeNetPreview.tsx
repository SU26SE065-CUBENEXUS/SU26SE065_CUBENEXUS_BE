import { FACE_ORDER, type FaceName, type FaceScanResult, type RubikColor } from '../types';

const COLOR_STYLE: Record<RubikColor | 'unknown', string> = {
  white: '#f8fafc',
  yellow: '#facc15',
  red: '#ef4444',
  orange: '#fb923c',
  blue: '#3b82f6',
  green: '#22c55e',
  unknown: '#475569',
};

type Props = {
  faces: Partial<Record<FaceName, FaceScanResult>>;
};

export function CubeNetPreview({ faces }: Props) {
  return (
    <div className="cube-net-preview">
      {FACE_ORDER.map((face) => {
        const grid = faces[face]?.grid;
        return (
          <div className="cube-face-preview" key={face}>
            <strong>{face}</strong>
            <div className="cube-face-grid">
              {Array.from({ length: 9 }).map((_, index) => {
                const color = grid?.[Math.floor(index / 3)]?.[index % 3] ?? 'unknown';
                return <span key={index} style={{ background: COLOR_STYLE[color] }} />;
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}
