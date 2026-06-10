"""LLM backends behind one interface. GroqLLM (OpenAI-compatible JSON mode) for dev,
FakeLLM for offline tests, AzureOpenAILLM for production (plan §6.2)."""
from __future__ import annotations

import json
from typing import Callable, Protocol

import httpx


class LLM(Protocol):
    def complete_json(self, system: str, user: str) -> str:
        """Return the model's raw JSON string response."""
        ...


class FakeLLM:
    """Deterministic LLM for tests. Either returns a fixed payload, or calls a function
    of (system, user) so a test can simulate a first-bad-then-good retry."""

    def __init__(self, payload: dict | str | None = None,
                 responder: Callable[[str, str], str] | None = None):
        self._responder = responder
        if payload is None:
            payload = _MINIMAL_VALID_MOM
        self._fixed = payload if isinstance(payload, str) else json.dumps(payload, ensure_ascii=False)

    def complete_json(self, system: str, user: str) -> str:
        if self._responder is not None:
            return self._responder(system, user)
        return self._fixed


class GroqLLM:
    def __init__(self, *, api_key: str, base_url: str, model: str, timeout: float = 60.0):
        if not api_key:
            raise ValueError("GROQ_API_KEY is required for the groq LLM")
        self._key = api_key
        self._url = f"{base_url.rstrip('/')}/chat/completions"
        self._model = model
        self._client = httpx.Client(timeout=timeout)

    def complete_json(self, system: str, user: str) -> str:
        resp = self._client.post(
            self._url,
            headers={"Authorization": f"Bearer {self._key}"},
            json={
                "model": self._model,
                "temperature": 0.2,
                "response_format": {"type": "json_object"},
                "messages": [
                    {"role": "system", "content": system},
                    {"role": "user", "content": user},
                ],
            },
        )
        resp.raise_for_status()
        return resp.json()["choices"][0]["message"]["content"]

    def close(self):
        self._client.close()


class AzureOpenAILLM:
    """Production backend (plan §6.2). Wired on deployment with Azure creds."""

    def __init__(self, *args, **kwargs):
        raise NotImplementedError(
            "AzureOpenAILLM is wired in production; use LLM_PROVIDER=groq for dev.")

    def complete_json(self, system: str, user: str) -> str:  # pragma: no cover
        raise NotImplementedError


def build_llm(settings) -> LLM:  # noqa: ANN001
    kind = settings.llm_provider.lower()
    if kind == "fake":
        return FakeLLM()
    if kind == "groq":
        return GroqLLM(api_key=settings.groq_api_key, base_url=settings.groq_base_url,
                       model=settings.groq_model)
    if kind == "azure-openai":
        return AzureOpenAILLM()
    raise ValueError(f"unknown llm provider: {settings.llm_provider}")


_MINIMAL_VALID_MOM = {
    "summary_ar": "ملخص الاجتماع.",
    "decisions_ar": [],
    "action_items": [],
    "proposed_meetings": [],
    "language_note": None,
}
