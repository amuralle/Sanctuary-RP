import argparse
import json
import os
import tempfile
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Link existing volunteer submissions to persistent hardpoint IDs.")
    parser.add_argument("--placements", type=Path, required=True)
    parser.add_argument("--mapping", type=Path, required=True)
    args = parser.parse_args()

    placements = json.loads(args.placements.read_text(encoding="utf-8"))
    mappings = json.loads(args.mapping.read_text(encoding="utf-8"))
    mapping_by_submission = {
        (entry["SubmissionId"], entry["UserId"]): entry["HardPointId"]
        for entry in mappings
    }

    linked = 0
    for placement in placements:
        hardpoint_id = mapping_by_submission.get(
            (placement["Id"], placement["SubmittedByUserId"]))
        if hardpoint_id is None:
            continue
        if placement.get("HardPointId") not in (None, hardpoint_id):
            raise ValueError(
                f"Submission {placement['Id']} is linked to a different hardpoint")
        placement["HardPointId"] = hardpoint_id
        linked += 1

    if linked != len(mappings):
        raise ValueError(f"Linked {linked} of {len(mappings)} mapped submissions")

    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f"{args.placements.name}.",
        suffix=".tmp",
        dir=args.placements.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(placements, stream, indent=2)
            stream.write("\n")
        os.chmod(temporary_name, 0o640)
        os.replace(temporary_name, args.placements)
    finally:
        if os.path.exists(temporary_name):
            os.unlink(temporary_name)

    print(f"Linked {linked} volunteer submissions to live hardpoints.")


if __name__ == "__main__":
    main()
