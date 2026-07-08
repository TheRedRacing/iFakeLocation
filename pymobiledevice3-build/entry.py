"""PyInstaller entry point for the frozen pymobiledevice3 CLI (see build.sh/build.ps1)."""
import sys

from pymobiledevice3.__main__ import main

if __name__ == "__main__":
    sys.argv[0] = sys.argv[0].removesuffix(".exe")
    sys.exit(main())
