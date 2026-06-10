# Media Bot Audio Format (M1 contract)

The media bot receives mixed-call audio from the Graph Communications SDK as PCM
frames and, when `DUMP_AUDIO=true`, writes them to a `.wav` file. This document pins
the **exact** format so the dump can be verified independently of Windows/the SDK.

## Canonical format

| Property | Value |
|---|---|
| Container | RIFF / WAVE |
| Encoding | Linear PCM (`AudioFormat = 1`) |
| Sample rate | **16000 Hz** |
| Bits per sample | **16** (signed little-endian) |
| Channels | **1** (mono — the mixed audio socket) |
| Byte rate | 32000 (= 16000 × 1 × 2) |
| Block align | 2 (= 1 × 2) |

This is exactly what the SDK's mixed audio socket delivers (`AudioFormat.Pcm16K`),
so the WavWriter copies frame bytes through unchanged — no resampling.

## RIFF byte layout (44-byte header, then samples)

```
offset  size  field            value
0       4     ChunkID          "RIFF"
4       4     ChunkSize        36 + dataSize      (little-endian uint32)
8       4     Format           "WAVE"
12      4     Subchunk1ID      "fmt "
16      4     Subchunk1Size    16                 (PCM)
20      2     AudioFormat      1                  (PCM)
22      2     NumChannels      1
24      4     SampleRate       16000
28      4     ByteRate         32000
32      2     BlockAlign       2
34      2     BitsPerSample    16
36      4     Subchunk2ID      "data"
40      4     Subchunk2Size    dataSize           (= numSamples * 2)
44      ...   PCM samples      int16 little-endian, interleaved (mono => 1 ch)
```

The header is written with placeholder sizes on open and **patched on close** once
the total sample count is known (the SDK streams an unknown length).

## Verifying a dumped file

`tools/check_wav.py` is the oracle. It parses the RIFF header and asserts every field
above. Run it on the WAV the bot produces on the Windows VM:

```bash
python3 tools/check_wav.py /path/to/dump_<meetingId>.wav
```

Exit code 0 + `CONFORMS` means the M1 audio pipeline is byte-correct. A non-zero exit
points at the first mismatched field. Then play the file (`afplay` on mac, any player
on Windows) to confirm the audio is intelligible — that is the M1 exit criterion.
