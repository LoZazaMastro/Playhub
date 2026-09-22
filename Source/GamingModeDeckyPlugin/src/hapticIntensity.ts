export function modernHapticIntensity(value: number, scale = 1) {
  const intensity = Math.max(5, Math.min(100, Number.isFinite(value) ? value : 55));
  const position = (intensity - 5) / 95;
  const level = position < 1 / 3 ? 0 : position < 2 / 3 ? 1 : 2;
  const shapeGain = 20 * Math.log10(Math.max(0.03, Math.min(1, scale)));
  const gain = Math.max(-12, Math.min(16, Math.round(-12 + 28 * position + shapeGain)));
  // Above the midpoint, blend in a second short click instead of clipping an
  // ever-higher gain. At 100 both clicks carry the full calibrated command.
  const repeatGain = intensity > 50
    ? Math.max(-12, Math.round(gain + 20 * Math.log10((intensity - 50) / 50)))
    : null;
  return { level, gain, repeatGain };
}
