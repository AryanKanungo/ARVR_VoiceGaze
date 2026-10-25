import argparse, csv, json, math, statistics, shutil, re
from collections import Counter
from datetime import datetime
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("csv_path")
parser.add_argument("--automated", action="store_true")
parser.add_argument("--summary", required=True)
parser.add_argument("--copy")
args = parser.parse_args()
path = Path(args.csv_path)
with path.open(newline="", encoding="utf-8") as stream:
    reader = csv.DictReader(stream)
    header = reader.fieldnames
    rows = list(reader)
assert len(header) == 35 and len(header) == len(set(header)), header
assert rows, "CSV must contain records"
for column in ("session_id", "trial_id", "input_source", "command", "recognition_confidence",
               "accepted", "rejection_reason", "recognition_event_to_action_ms",
               "input_event_to_result_ms", "command_processing_ms", "frame_time_ms",
               "measurement_id", "voice_callback_events_dropped"):
    assert column in header, column
sessions = set()
sequences = {}
commands = []
performance = []
def utc(value):
    assert value.endswith("Z"), value
    # .NET round-trip timestamps carry seven fractional digits; Python 3.10 parses six.
    normalized = re.sub(r"(\.\d{6})\d+", r"\1", value[:-1])
    result = datetime.fromisoformat(normalized + "+00:00")
    assert result.utcoffset().total_seconds() == 0
    return result
def number(value):
    result = float(value)
    assert math.isfinite(result) and result >= 0, value
    return result
for row in rows:
    assert None not in row and all(value is not None for value in row.values()), row
    session = row["session_id"]
    assert len(session) == 32 and all(c in "0123456789abcdef" for c in session)
    sessions.add(session)
    sequence = int(row["event_id"])
    assert sequence > sequences.get(session, 0)
    sequences[session] = sequence
    utc(row["result_completed_utc"])
    assert row["automated_test_session"] in ("true", "false")
    assert row["synthetic_input"] in ("true", "false")
    assert int(row["dropped_rows"]) == 0
    assert int(row["voice_callback_events_dropped"] or 0) == 0
    if args.automated:
        assert row["automated_test_session"] == "true"
        assert row["session_label"] == 'automated_smoke, "CSV quote check"', row["session_label"]
        assert row["trial_id"] == "AUTOMATED-001"
    if row["record_type"] == "command":
        commands.append(row)
        assert row["input_source"] in ("voice", "keyboard", "system")
        assert row["command"] in ("select", "place", "release", "reset")
        assert row["accepted"] in ("true", "false")
        received = utc(row["input_received_utc"])
        completed = utc(row["result_completed_utc"])
        assert completed >= received, "Wall clock changed between event receipt and result"
        event = number(row["input_event_to_result_ms"])
        processing = number(row["command_processing_ms"])
        assert event + .001 >= processing
        number(row["frame_time_ms"])
        if row["accepted"] == "false":
            assert row["rejection_reason"]
            assert row["recognition_event_to_action_ms"] == "", "Rejected command has no action"
        else:
            assert row["rejection_reason"] == ""
            if row["command"] in ("select", "place"):
                assert row["target_object"]
            if row["command"] == "place":
                assert row["destination_object"]
        if row["input_source"] == "keyboard":
            assert row["recognition_confidence"] == ""
            assert row["recognition_event_to_action_ms"] == ""
            assert row["phrase_started_utc"] == ""
        if row["input_source"] == "voice" and row["accepted"] == "true":
            assert abs(number(row["recognition_event_to_action_ms"]) - event) < .00001
        if row["input_source"] == "voice" and row["synthetic_input"] == "false":
            assert row["recognition_confidence"] in ("High", "Medium", "Low", "Rejected")
            assert row["phrase_started_utc"]
            utc(row["phrase_started_utc"])
            number(row["phrase_duration_ms"])
    if int(row["sampled_frames"]) > 0:
        n = int(row["sampled_frames"])
        duration = number(row["sampled_duration_s"])
        mean = number(row["avg_frame_time_ms"])
        fps = number(row["avg_fps"])
        assert abs(mean - duration * 1000 / n) < .002
        assert abs(fps - n / duration) < .002
        assert number(row["max_frame_time_ms"]) + .00001 >= mean
        if row["record_type"] == "performance":
            performance.append(row)
real_voice = [r for r in commands if r["input_source"] == "voice" and r["synthetic_input"] == "false"]
accepted_voice = [r for r in real_voice if r["accepted"] == "true"]
latencies = [float(r["recognition_event_to_action_ms"]) for r in accepted_voice]
keyboard_processing = [float(r["command_processing_ms"]) for r in commands if r["input_source"] == "keyboard"]
summary = {
    "csv_path": str(path), "schema_columns": len(header), "rows": len(rows),
    "session_ids": sorted(sessions), "command_rows": len(commands),
    "record_types": dict(Counter(r["record_type"] for r in rows)),
    "input_sources": dict(Counter(r["input_source"] for r in commands)),
    "accepted_commands": sum(r["accepted"] == "true" for r in commands),
    "rejection_reasons": dict(Counter(r["rejection_reason"] for r in commands if r["accepted"] == "false")),
    "synthetic_voice_rows": sum(r["input_source"] == "voice" and r["synthetic_input"] == "true" for r in commands),
    "real_voice_rows": len(real_voice),
    "real_voice_accepted_commands": dict(Counter(r["command"] for r in accepted_voice)),
    "real_voice_event_to_action_ms": None if not latencies else {
        "n": len(latencies), "min": min(latencies), "mean": statistics.mean(latencies),
        "median": statistics.median(latencies), "max": max(latencies)},
    "keyboard_processing_ms": None if not keyboard_processing else {
        "n": len(keyboard_processing), "mean": statistics.mean(keyboard_processing), "max": max(keyboard_processing)},
    "performance_rows": len(performance), "validation": "PASS",
    "interpretation": "Development/automation verification only; not human-study or acoustic speech-latency results."
}
Path(args.summary).write_text(json.dumps(summary, indent=2), encoding="utf-8")
if args.copy:
    shutil.copyfile(path, args.copy)
print(json.dumps(summary, indent=2))

