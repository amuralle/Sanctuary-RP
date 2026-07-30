#!/usr/bin/env python3

import argparse
import pathlib
import subprocess
import xml.etree.ElementTree as ElementTree


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=pathlib.Path)
    parser.add_argument("--template", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    return parser.parse_args()


def iter_manifest_files(folder, parts=()):
    folder_name = folder.get("name")
    if folder_name:
        parts = (*parts, folder_name)

    for file in folder.findall("File"):
        yield pathlib.Path(*parts, file.get("name")), file

    for child in folder.findall("Folder"):
        yield from iter_manifest_files(child, parts)


def hash_file(path):
    result = subprocess.run(
        ["xxhsum", "-H1", str(path)],
        check=True,
        capture_output=True,
        text=True,
    )
    return int(result.stdout.split()[0], 16)


def main():
    args = parse_args()
    root = args.root.resolve()
    tree = ElementTree.parse(args.template)
    manifest_root = tree.getroot().find("Folder")
    updated = 0

    for relative_path, file in iter_manifest_files(manifest_root):
        path = (root / relative_path).resolve()
        path.relative_to(root)

        if not path.is_file():
            raise FileNotFoundError(f"Manifest file is missing: {relative_path}")

        file.set("size", str(path.stat().st_size))
        file.set("hash", str(hash_file(path)))
        updated += 1

    ElementTree.indent(tree, space="  ")
    tree.write(args.output, encoding="utf-8", xml_declaration=True)
    print(f"Generated manifest for {updated} client files.")


if __name__ == "__main__":
    main()
