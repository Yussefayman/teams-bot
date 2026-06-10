import wave
import io

from app.transcriber import FakeTranscriber, pcm_to_wav_bytes, build_transcriber


def test_fake_transcriber_is_deterministic():
    t = FakeTranscriber()
    a = t.transcribe(b"\x01\x02" * 100)
    b = t.transcribe(b"\x01\x02" * 100)
    assert a.text == b.text
    assert a.lang_detected == "ar"


def test_pcm_to_wav_is_16k_mono_16bit():
    wav = pcm_to_wav_bytes(b"\x00\x00" * 1600, sample_rate=16000)
    with wave.open(io.BytesIO(wav)) as w:
        assert w.getnchannels() == 1
        assert w.getsampwidth() == 2
        assert w.getframerate() == 16000


def test_build_transcriber_selects_backend():
    class S:
        transcriber = "fake"
    t = build_transcriber(S())
    assert isinstance(t, FakeTranscriber)
