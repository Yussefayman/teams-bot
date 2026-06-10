import sys
from pathlib import Path

# make the service package importable as `app`
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
