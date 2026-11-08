"""Raw layer files: little-endian, row-major, row 0 = southernmost row (scenarios/README.md)."""
import hashlib
from pathlib import Path

import numpy as np


def write_layer(path, north_up: np.ndarray) -> str:
    """Writes a 2-D array read in GeoTIFF order (row 0 = north) with the southernmost row first.

    float32 layers are written little-endian, uint8 layers one byte per cell. Returns the file's sha256 (hex).
    """
    if north_up.ndim != 2:
        raise ValueError(f"expected a 2-D array, got {north_up.ndim} dimensions")
    if north_up.dtype == np.float32:
        dtype = "<f4"
    elif north_up.dtype == np.uint8:
        dtype = "u1"
    else:
        raise ValueError(f"unsupported layer type {north_up.dtype}; use float32 or uint8")

    data = np.ascontiguousarray(np.flipud(north_up), dtype=dtype).tobytes()
    Path(path).write_bytes(data)
    return hashlib.sha256(data).hexdigest()
