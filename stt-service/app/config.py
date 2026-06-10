"""STT service configuration. Env-driven via pydantic-settings; no hardcoded values."""
from __future__ import annotations

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    # transcriber backend: "groq" (hosted whisper-large-v3), "fake" (offline tests),
    # or "faster-whisper" (local GPU — production, plan §5).
    transcriber: str = "groq"

    # Groq hosted Whisper
    groq_api_key: str = ""
    groq_base_url: str = "https://api.groq.com/openai/v1"
    groq_model: str = "whisper-large-v3"

    # faster-whisper (only used when transcriber=faster-whisper)
    whisper_model: str = "large-v3"
    whisper_compute_type: str = "float16"

    # transcription language: "auto" (detect) or "ar" (force). Benchmark both (plan §9).
    whisper_language: str = "auto"
    initial_prompt_path: str = "app/initial_prompt_ar.txt"

    # VAD segmentation
    vad_silence_ms: int = 600
    vad_max_segment_ms: int = 25000      # hard cut so Whisper requests stay bounded
    vad_min_segment_ms: int = 400        # drop blips shorter than this
    vad_energy_threshold: int = 500      # int16 RMS below this = silence

    # audio format (must match the media bot — AUDIO-FORMAT.md)
    sample_rate: int = 16000

    data_dir: str = "data/transcripts"
    host: str = "127.0.0.1"
    port: int = 8799

    @property
    def language(self) -> str | None:
        return None if self.whisper_language.lower() == "auto" else self.whisper_language

    def initial_prompt(self) -> str | None:
        from pathlib import Path
        p = Path(self.initial_prompt_path)
        return p.read_text(encoding="utf-8").strip() if p.exists() else None


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()
    return _settings
