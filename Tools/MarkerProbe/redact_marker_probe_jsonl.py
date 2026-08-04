#!/usr/bin/env python3
"""Create a repository-safe Marker Probe JSONL copy.

The source file is never modified. Unique device/account/network fields and RawPayload
plaintext are removed, repeated sensitive values are scrubbed from free-form strings, and
world-space poses are expressed relative to the first world pose in each run.
"""

from __future__ import annotations

import argparse
import json
import math
import tempfile
from pathlib import Path
from typing import Any, Dict, Iterable, List, MutableMapping, Sequence, Tuple


REDACTION_SCHEMA = "marker-probe-redaction-v1"
FORBIDDEN_NORMALIZED_KEYS = {
    "accountid",
    "bssid",
    "devicename",
    "deviceuniqueidentifier",
    "email",
    "ipaddress",
    "macaddress",
    "networkid",
    "serialnumber",
    "ssid",
    "userid",
}

Vector3 = Tuple[float, float, float]
Quaternion = Tuple[float, float, float, float]


def normalized_key(key: str) -> str:
    return "".join(character.lower() for character in key if character.isalnum())


def read_jsonl(path: Path) -> List[Dict[str, Any]]:
    events: List[Dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as source:
        for line_number, line in enumerate(source, start=1):
            if not line.strip():
                raise ValueError(f"{path}:{line_number}: blank JSONL lines are not allowed")
            value = json.loads(line)
            if not isinstance(value, dict):
                raise ValueError(f"{path}:{line_number}: each JSONL line must be an object")
            events.append(value)
    return events


def collect_sensitive_values(value: Any, parent_key: str = "") -> set[str]:
    sensitive: set[str] = set()
    if isinstance(value, dict):
        for key, child in value.items():
            key_name = normalized_key(key)
            if key_name in FORBIDDEN_NORMALIZED_KEYS or key_name == "plaintext":
                if isinstance(child, str) and child:
                    sensitive.add(child)
            sensitive.update(collect_sensitive_values(child, key))
    elif isinstance(value, list):
        for child in value:
            sensitive.update(collect_sensitive_values(child, parent_key))
    return sensitive


def scrub_string(value: str, sensitive_values: Iterable[str]) -> str:
    result = value
    for sensitive in sorted(sensitive_values, key=len, reverse=True):
        if sensitive:
            result = result.replace(sensitive, "[REDACTED]")
    return result


def redact_value(value: Any, sensitive_values: set[str]) -> Any:
    if isinstance(value, dict):
        sanitized: Dict[str, Any] = {}
        for key, child in value.items():
            key_name = normalized_key(key)
            if key_name in FORBIDDEN_NORMALIZED_KEYS or key_name == "plaintext":
                continue
            if key_name in {"plaintextincluded", "rawpayloadcaptured"}:
                sanitized[key] = False
                continue
            sanitized[key] = redact_value(child, sensitive_values)
        return sanitized
    if isinstance(value, list):
        return [redact_value(child, sensitive_values) for child in value]
    if isinstance(value, str):
        return scrub_string(value, sensitive_values)
    return value


def vector3(value: MutableMapping[str, Any]) -> Vector3:
    return (float(value["x"]), float(value["y"]), float(value["z"]))


def quaternion(value: MutableMapping[str, Any]) -> Quaternion:
    raw = (float(value["x"]), float(value["y"]), float(value["z"]), float(value["w"]))
    magnitude = math.sqrt(sum(component * component for component in raw))
    if magnitude <= 1e-12:
        raise ValueError("cannot normalize a zero quaternion")
    return tuple(component / magnitude for component in raw)  # type: ignore[return-value]


def quaternion_inverse(value: Quaternion) -> Quaternion:
    return (-value[0], -value[1], -value[2], value[3])


def quaternion_multiply(left: Quaternion, right: Quaternion) -> Quaternion:
    lx, ly, lz, lw = left
    rx, ry, rz, rw = right
    return (
        lw * rx + lx * rw + ly * rz - lz * ry,
        lw * ry - lx * rz + ly * rw + lz * rx,
        lw * rz + lx * ry - ly * rx + lz * rw,
        lw * rw - lx * rx - ly * ry - lz * rz,
    )


def rotate_vector(rotation: Quaternion, value: Vector3) -> Vector3:
    vector_quaternion: Quaternion = (value[0], value[1], value[2], 0.0)
    rotated = quaternion_multiply(
        quaternion_multiply(rotation, vector_quaternion), quaternion_inverse(rotation)
    )
    return (rotated[0], rotated[1], rotated[2])


def is_world_pose(value: Any) -> bool:
    coordinate_space = value.get("coordinateSpace") if isinstance(value, dict) else None
    return (
        isinstance(value, dict)
        and isinstance(coordinate_space, str)
        and "world" in coordinate_space.lower()
        and "relative" not in coordinate_space.lower()
        and isinstance(value.get("position"), dict)
        and isinstance(value.get("rotation"), dict)
    )


def iter_world_poses(value: Any) -> Iterable[MutableMapping[str, Any]]:
    if is_world_pose(value):
        yield value
    if isinstance(value, dict):
        for child in value.values():
            yield from iter_world_poses(child)
    elif isinstance(value, list):
        for child in value:
            yield from iter_world_poses(child)


def primary_world_pose(event: Dict[str, Any]) -> MutableMapping[str, Any] | None:
    pose = event.get("pose")
    if isinstance(pose, dict) and is_world_pose(pose.get("unityPose")):
        return pose["unityPose"]
    return next(iter(iter_world_poses(event)), None)


def relative_pose(
    pose: MutableMapping[str, Any],
    reference_position: Vector3,
    reference_rotation: Quaternion,
) -> None:
    position = vector3(pose["position"])
    rotation = quaternion(pose["rotation"])
    reference_inverse = quaternion_inverse(reference_rotation)
    delta = (
        position[0] - reference_position[0],
        position[1] - reference_position[1],
        position[2] - reference_position[2],
    )
    relative_position = rotate_vector(reference_inverse, delta)
    relative_rotation = quaternion_multiply(reference_inverse, rotation)

    pose["position"] = dict(zip(("x", "y", "z"), relative_position))
    pose["rotation"] = dict(zip(("x", "y", "z", "w"), relative_rotation))
    pose["coordinateSpace"] = "run_first_world_pose_relative"


def make_world_poses_relative(events: Sequence[Dict[str, Any]]) -> None:
    references: Dict[str, Tuple[Vector3, Quaternion]] = {}

    for event in events:
        world_poses = list(iter_world_poses(event))
        if not world_poses:
            continue

        run_id = event.get("runId")
        if not isinstance(run_id, str) or not run_id:
            raise ValueError("world-space Pose cannot be sanitized without a non-empty runId")

        if run_id not in references:
            first = primary_world_pose(event)
            if first is None:
                raise ValueError(f"run {run_id}: could not select a first world Pose")
            references[run_id] = (vector3(first["position"]), quaternion(first["rotation"]))

        reference_position, reference_rotation = references[run_id]
        for pose in world_poses:
            relative_pose(pose, reference_position, reference_rotation)

        event["redaction"] = {
            "schemaVersion": REDACTION_SCHEMA,
            "worldPoseReference": "first world Pose in this run",
            "runId": run_id,
        }


def validate_sanitized(events: Sequence[Dict[str, Any]], sensitive_values: set[str]) -> None:
    serialized = "\n".join(json.dumps(event, ensure_ascii=False) for event in events)
    for sensitive in sensitive_values:
        if sensitive and sensitive in serialized:
            raise ValueError("sensitive value remains after redaction")

    for event in events:
        for pose in iter_world_poses(event):
            raise ValueError(f"unsanitized world Pose remains: {pose.get('coordinateSpace')}")

        def check_keys(value: Any) -> None:
            if isinstance(value, dict):
                for key, child in value.items():
                    key_name = normalized_key(key)
                    if key_name in FORBIDDEN_NORMALIZED_KEYS or key_name == "plaintext":
                        raise ValueError(f"forbidden field remains after redaction: {key}")
                    check_keys(child)
            elif isinstance(value, list):
                for child in value:
                    check_keys(child)

        check_keys(event)


def sanitize(events: Sequence[Dict[str, Any]]) -> List[Dict[str, Any]]:
    sensitive_values: set[str] = set()
    for event in events:
        sensitive_values.update(collect_sensitive_values(event))

    sanitized = [redact_value(event, sensitive_values) for event in events]
    make_world_poses_relative(sanitized)
    validate_sanitized(sanitized, sensitive_values)
    return sanitized


def write_jsonl(events: Sequence[Dict[str, Any]], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as destination:
        for event in events:
            destination.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")))
            destination.write("\n")


def self_test() -> None:
    source = [
        {
            "sequence": 1,
            "runId": "run-a",
            "deviceUniqueIdentifier": "device-secret",
            "rawPayload": {
                "utf8ByteLength": 15,
                "sha256": "safe-hash",
                "rawPayloadCaptured": True,
                "plaintext": "business-secret",
            },
            "error": {"message": "saw business-secret on device-secret"},
            "pose": {
                "unityPose": {
                    "coordinateSpace": "unity_world",
                    "position": {"x": 10, "y": 0, "z": 0},
                    "rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
                }
            },
        },
        {
            "sequence": 2,
            "runId": "run-a",
            "pose": {
                "unityPose": {
                    "coordinateSpace": "unity_world",
                    "position": {"x": 11, "y": 0, "z": 0},
                    "rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
                }
            },
        },
    ]
    result = sanitize(source)
    assert result[0]["pose"]["unityPose"]["position"]["x"] == 0
    assert result[1]["pose"]["unityPose"]["position"]["x"] == 1
    assert "deviceUniqueIdentifier" not in result[0]
    assert "plaintext" not in result[0]["rawPayload"]
    assert "[REDACTED]" in result[0]["error"]["message"]

    with tempfile.TemporaryDirectory(prefix="marker-probe-redaction-") as directory:
        output = Path(directory) / "representative.jsonl"
        write_jsonl(result, output)
        assert len(read_jsonl(output)) == 2

    print("Marker Probe redaction self-test passed")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", type=Path, help="source device JSONL")
    parser.add_argument("output", nargs="?", type=Path, help="new sanitized JSONL")
    parser.add_argument("--self-test", action="store_true")
    arguments = parser.parse_args()

    if arguments.self_test:
        self_test()
        return
    if arguments.input is None or arguments.output is None:
        parser.error("input and output are required unless --self-test is used")
    if arguments.input.resolve() == arguments.output.resolve():
        parser.error("output must be a new file; source logs are never overwritten")
    if arguments.output.exists():
        parser.error(f"output already exists: {arguments.output}")

    source_events = read_jsonl(arguments.input)
    sanitized_events = sanitize(source_events)
    write_jsonl(sanitized_events, arguments.output)
    print(f"Wrote {len(sanitized_events)} sanitized events to {arguments.output}")


if __name__ == "__main__":
    main()
