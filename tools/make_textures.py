"""Makes the water's tileable textures (no game assets): ripple normals and foam noise.

Both come from white noise shaped in the frequency domain, which tiles by construction.
Usage: python3 tools/make_textures.py unity/Assets/FlowingWater/Textures
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

SIZE = 512


def shaped_noise(rng, slope, low, high):
    """Tileable noise whose spectrum falls as k^-slope between the low and high cut-offs (cycles per tile)."""
    white = rng.standard_normal((SIZE, SIZE))
    k = np.fft.fftfreq(SIZE) * SIZE
    kx, ky = np.meshgrid(k, k)
    radius = np.hypot(kx, ky)
    radius[0, 0] = 1.0
    gain = radius ** -slope
    gain *= 1.0 / (1.0 + np.exp((low - radius) * 1.5))
    gain *= 1.0 / (1.0 + np.exp((radius - high) * 0.15))
    gain[0, 0] = 0.0
    field = np.real(np.fft.ifft2(np.fft.fft2(white) * gain))
    return (field - field.mean()) / field.std()


def normal_map(height, strength):
    """Tangent-space normals (x right, y up the texture, z out) from a height field, wrapping at the edges."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength
    n = np.stack([-dx, dy, np.ones_like(height)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255.0 + 0.5).clip(0, 255).astype(np.uint8)


def main():
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(20261021)

    # Ripples: mostly small wavelets with a few longer swells.
    ripples = shaped_noise(rng, 1.0, 6.0, 60.0) + 0.6 * shaped_noise(rng, 1.8, 2.0, 14.0)
    Image.fromarray(normal_map(ripples, 2.5), "RGB").save(out / "water_normal.png")

    # Foam: blotchy, sharpened so it breaks into streaks and patches once thresholded.
    foam = shaped_noise(rng, 1.2, 4.0, 120.0)
    foam = 1.0 / (1.0 + np.exp(-foam * 1.6))
    Image.fromarray((foam * 255.0 + 0.5).clip(0, 255).astype(np.uint8), "L").save(out / "water_foam.png")
    print(f"written to {out}")


if __name__ == "__main__":
    main()
