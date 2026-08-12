#!/usr/bin/env python3
"""Evaluate a sherpa-onnx NeMo CTC model against the project STT dataset."""

import argparse
import json
import re
import statistics
import time
import wave
from pathlib import Path

import sherpa_onnx


def normalize(text: str) -> str:
    text = text.lower().replace("ё", "е")
    replacements = (
        (r"\b(?:би\s*пи|бп(?:[иэ]шки)?|бонус[- ](?:п(?:о|а)ин(?:ты|иты)|пониты))\b", "bp"),
        (r"\b(?:ди\s*пи|дп(?:[иэ]шки)?|донат[- ]поинты|донатная\s+валюта)\b", "dp"),
        (r"\bм[еэа]р(?:р)?[иэ]?[увв]?[еэ]з?[еэ]р\b", "мерривезер"),
        (r"\bа[эи]ро?дроп", "аирдроп"),
        (r"\bр[ое]днекс", "реднекс"),
        (r"\bкар\s*мит", "кар мит"),
        (r"\b[иэ]пс(?:и)?[ои]н", "эпсилон"),
    )
    for pattern, value in replacements:
        text = re.sub(pattern, value, text)
    text = re.sub(r"\bрепутаций\b", "репутации", text)
    return " ".join(re.sub(r"[^a-zа-я0-9]+", " ", text).split())


def edit_distance(left: list[str], right: list[str]) -> int:
    previous = list(range(len(right) + 1))
    for i, a in enumerate(left, 1):
        current = [i]
        for j, b in enumerate(right, 1):
            current.append(min(current[-1] + 1, previous[j] + 1,
                               previous[j - 1] + (a != b)))
        previous = current
    return previous[-1]


def percentile95(values: list[float]) -> float:
    ordered = sorted(values)
    return ordered[max(0, min(len(ordered) - 1, int(len(ordered) * .95 + .999999) - 1))]


def read_wav(path: Path) -> tuple[list[float], int]:
    with wave.open(str(path), "rb") as wav:
        if wav.getnchannels() != 1 or wav.getsampwidth() != 2:
            raise ValueError(f"Expected mono PCM16 WAV: {path}")
        rate = wav.getframerate()
        samples = wav.readframes(wav.getnframes())
    import array
    pcm = array.array("h", samples)
    return [sample / 32768.0 for sample in pcm], rate


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", required=True, type=Path)
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--tokens", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    dataset = json.loads(args.dataset.read_text(encoding="utf-8-sig"))
    recognizer = sherpa_onnx.OfflineRecognizer.from_nemo_ctc(
        model=str(args.model), tokens=str(args.tokens), num_threads=4,
        sample_rate=16000, feature_dim=64)

    cases = []
    latencies = []
    total_edits = total_words = recalled = term_count = failures = 0
    dataset_root = args.dataset.parent.resolve()
    for item in dataset["cases"]:
        audio_path = (dataset_root / item["audioFile"]).resolve()
        if dataset_root not in audio_path.parents:
            raise ValueError(f"Audio path escapes dataset root: {audio_path}")
        started = time.perf_counter()
        try:
            samples, rate = read_wav(audio_path)
            if rate != 16000:
                raise ValueError(f"Expected 16 kHz WAV, got {rate}: {audio_path}")
            stream = recognizer.create_stream()
            stream.accept_waveform(rate, samples)
            recognizer.decode_stream(stream)
            transcript = stream.result.text
            error = None
        except Exception as exc:
            transcript, error = "", str(exc)
            failures += 1
        latency = (time.perf_counter() - started) * 1000
        latencies.append(latency)
        reference = normalize(item["reference"])
        hypothesis = normalize(transcript)
        ref_words, hyp_words = reference.split(), hypothesis.split()
        edits = edit_distance(ref_words, hyp_words)
        total_edits += edits
        total_words += len(ref_words)
        required = [normalize(term) for term in item.get("requiredTerms", [])]
        hits = sum(term in hypothesis for term in required)
        recalled += hits
        term_count += len(required)
        cases.append({"id": item["id"], "reference": item["reference"],
                      "transcript": transcript, "normalizedTranscript": hypothesis,
                      "wordErrors": edits, "referenceWords": len(ref_words),
                      "requiredTerms": required, "recalledTerms": hits,
                      "latencyMs": round(latency, 2), "error": error})

    wer = total_edits / total_words if total_words else 1.0
    recall = recalled / term_count if term_count else 1.0
    p95 = percentile95(latencies)
    gate = dataset["gate"]
    passed = (len(cases) >= gate["minimumCases"] and
              wer <= gate["maximumAverageWordErrorRate"] and
              recall >= gate["minimumTermRecall"] and
              p95 <= gate["maximumP95LatencyMs"] and failures == 0)
    report = {"schemaVersion": 1, "engine": "sherpa-onnx", "modelId": args.model.parent.name,
              "datasetId": dataset["id"], "caseCount": len(cases),
              "metrics": {"wordErrorRate": round(wer, 6), "termRecall": round(recall, 6),
                          "p95LatencyMs": round(p95, 2), "meanLatencyMs": round(statistics.mean(latencies), 2),
                          "failureCount": failures},
              "gate": {"passed": passed, "thresholds": gate}, "cases": cases}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"sherpa-onnx: {'PASS' if passed else 'FAIL'}; WER {wer:.1%}; terms {recall:.1%}; "
          f"p95 {p95:.0f} ms; failures {failures}")
    return 0 if passed else 2


if __name__ == "__main__":
    raise SystemExit(main())
