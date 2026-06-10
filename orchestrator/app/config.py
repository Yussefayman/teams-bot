"""Orchestrator configuration. Env-driven via pydantic-settings; no hardcoded values."""
from __future__ import annotations

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    # LLM backend for MoM generation: "groq" (dev), "azure-openai" (prod), "fake" (tests).
    llm_provider: str = "groq"

    # Groq (OpenAI-compatible)
    groq_api_key: str = ""
    groq_base_url: str = "https://api.groq.com/openai/v1"
    groq_model: str = "llama-3.3-70b-versatile"

    # Azure OpenAI (prod — plan §6.2)
    azure_openai_endpoint: str = ""
    azure_openai_deployment: str = ""
    azure_openai_api_version: str = "2024-08-01-preview"

    # Microsoft Graph (app-only)
    tenant_id: str = ""
    client_id: str = ""
    client_secret: str = ""
    graph_enabled: bool = False        # when False, use the FakeGraph (dev/Mac)
    card_out_dir: str = ""             # FakeGraph persists posted cards/events here (demo)

    # behaviour
    stt_base_url: str = "http://127.0.0.1:8799"
    default_timezone: str = "Asia/Riyadh"
    send_mom_email: bool = False
    auto_create_confidence_threshold: str = "high"
    transcript_poll_timeout_s: int = 60

    host: str = "127.0.0.1"
    port: int = 8798


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()
    return _settings
