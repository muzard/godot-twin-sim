"""W1, preprocessing side: layers are written with row 0 = southernmost row.

GeoTIFF rows run north to south, so the preprocessing flips them. Run: ./dev prep python3 -m pytest prep/tests
"""
import numpy as np

from simprep.layers import write_layer


def test_w1_written_float32_layer_has_the_southernmost_row_first(tmp_path):
    # As read from a GeoTIFF: row 0 is the northernmost row. Asymmetric, so any flip or transpose shows.
    north_up = np.array([[1, 2, 3],
                         [4, 5, 6]], dtype=np.float32)
    path = tmp_path / "h_maa.bin"

    write_layer(path, north_up)

    written = np.fromfile(path, dtype="<f4").reshape(2, 3)
    assert written[0].tolist() == [4, 5, 6]
    assert written[1].tolist() == [1, 2, 3]


def test_w1_written_uint8_layer_is_one_byte_per_cell_south_row_first(tmp_path):
    north_up = np.array([[5, 5, 3],
                         [1, 0, 0]], dtype=np.uint8)
    path = tmp_path / "maastoluokka.bin"

    write_layer(path, north_up)

    assert path.read_bytes() == bytes([1, 0, 0, 5, 5, 3])


def test_float32_layer_is_little_endian(tmp_path):
    path = tmp_path / "h_maa.bin"

    write_layer(path, np.array([[1.0]], dtype=np.float32))

    assert path.read_bytes() == bytes([0x00, 0x00, 0x80, 0x3F])
