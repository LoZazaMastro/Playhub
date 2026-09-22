"""Opt-in RGB backend. Importing this package never accesses hardware."""

from .backend import RgbBackend

__all__ = ["RgbBackend"]
