# Lets tests in prep/tests import the preprocessing package (prep/simprep) without installing it.
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
