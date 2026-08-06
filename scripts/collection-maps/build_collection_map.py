import argparse
import io
import struct
import urllib.parse
import urllib.request
import zlib
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


@dataclass(frozen=True)
class PackAsset:
    pack_path: Path
    offset: int
    size: int


@dataclass(frozen=True)
class MapTile:
    asset_name: str
    x: float
    z: float
    width: float
    height: float


def read_pack_index(pack_path: Path) -> dict[str, PackAsset]:
    assets: dict[str, PackAsset] = {}

    with pack_path.open("rb") as stream:
        next_chunk_offset = 0
        while True:
            stream.seek(next_chunk_offset)
            next_chunk_offset, asset_count = struct.unpack(">II", stream.read(8))

            for _ in range(asset_count):
                (name_length,) = struct.unpack(">I", stream.read(4))
                name = stream.read(name_length).decode("utf-8")
                offset, size, _crc = struct.unpack(">III", stream.read(12))
                assets[name] = PackAsset(pack_path, offset, size)

            if next_chunk_offset == 0:
                return assets


def read_asset(asset: PackAsset) -> bytes:
    with asset.pack_path.open("rb") as stream:
        stream.seek(asset.offset)
        data = stream.read(asset.size)

    if len(data) != asset.size:
        raise ValueError(f"Asset in {asset.pack_path} is truncated")

    return data


def read_remote_asset(
    asset_name: str,
    expected_crc: int | None,
    asset_server: str | None,
    asset_cache: Path | None,
) -> bytes | None:
    cache_path = asset_cache / asset_name if asset_cache is not None else None
    if cache_path is not None and cache_path.exists():
        cached = cache_path.read_bytes()
        if expected_crc is None or zlib.crc32(cached) == expected_crc:
            return cached

        cache_path.unlink()

    if asset_server is None:
        return None

    url = (
        asset_server.rstrip("/") + "/" +
        urllib.parse.quote(asset_name + ".z"))
    request = urllib.request.Request(url, headers={"User-Agent": "OSFR map builder"})
    with urllib.request.urlopen(request, timeout=30) as response:
        compressed = response.read()

    if len(compressed) < 8 or compressed[:4] != b"\xa1\xb2\xc3\xd4":
        raise ValueError(f"Unexpected asset envelope for {asset_name}")

    (expected_size,) = struct.unpack(">I", compressed[4:8])
    data = zlib.decompress(compressed[8:])
    if len(data) != expected_size:
        raise ValueError(
            f"Downloaded {asset_name} is {len(data)} bytes; expected {expected_size}")

    if expected_crc is None:
        raise ValueError(f"No manifest CRC is available for {asset_name}")

    actual_crc = zlib.crc32(data)
    if actual_crc != expected_crc:
        raise ValueError(
            f"Downloaded {asset_name} has CRC {actual_crc}; expected {expected_crc}")

    if cache_path is not None:
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        cache_path.write_bytes(data)

    return data


def read_asset_manifest(manifest_path: Path) -> dict[str, int]:
    checksums: dict[str, int] = {}
    if not manifest_path.exists():
        return checksums

    for line in manifest_path.read_text(encoding="utf-8-sig").splitlines():
        fields = line.split(",")
        if len(fields) != 3 or not fields[0].endswith(".z"):
            continue

        try:
            checksums[fields[0][:-2]] = int(fields[1])
        except ValueError:
            continue

    return checksums


def parse_tile_info(data: bytes) -> list[MapTile]:
    tiles: list[MapTile] = []

    for line in data.decode("utf-8-sig").splitlines():
        fields = line.split()
        if len(fields) != 6:
            continue

        asset_name, x, _y, z, width, height = fields
        tiles.append(MapTile(
            asset_name,
            float(x),
            float(z),
            float(width),
            float(height)))

    return tiles


def intersects(tile: MapTile, bounds: tuple[float, float, float, float]) -> bool:
    min_x, max_x, min_z, max_z = bounds
    return (
        tile.x < max_x and tile.x + tile.width > min_x and
        tile.z < max_z and tile.z + tile.height > min_z
    )


def build_map(
    assets: dict[str, PackAsset],
    tiles: list[MapTile],
    bounds: tuple[float, float, float, float],
    output_path: Path,
    asset_checksums: dict[str, int],
    asset_server: str | None,
    asset_cache: Path | None,
) -> None:
    min_x, max_x, min_z, max_z = bounds
    selected_tiles = [tile for tile in tiles if intersects(tile, bounds)]
    if not selected_tiles:
        raise ValueError("No minimap tiles intersect the requested bounds")

    pixels_per_unit = 2
    canvas = Image.new(
        "RGB",
        (round((max_x - min_x) * pixels_per_unit),
         round((max_z - min_z) * pixels_per_unit)),
        "#101a1d")
    missing: list[str] = []
    recovered = 0

    for tile in selected_tiles:
        asset = assets.get(tile.asset_name)
        data = read_asset(asset) if asset is not None else None
        is_recovered = False
        if data is None:
            try:
                data = read_remote_asset(
                    tile.asset_name,
                    asset_checksums.get(tile.asset_name),
                    asset_server,
                    asset_cache)
            except Exception as error:
                print(f"Could not recover {tile.asset_name}: {error}")

            if data is None:
                missing.append(tile.asset_name)
                continue

            recovered += 1
            is_recovered = True

        with Image.open(io.BytesIO(data)) as image:
            # Client minimap tiles are stored 90 degrees clockwise from world X/Z.
            tile_image = image.convert("RGB").transpose(Image.Transpose.ROTATE_270)
            expected_size = (
                round(tile.width * pixels_per_unit),
                round(tile.height * pixels_per_unit))
            if tile_image.size != expected_size:
                if not is_recovered:
                    raise ValueError(
                        f"{tile.asset_name} is {tile_image.size}; expected {expected_size}")

                tile_image = tile_image.resize(
                    expected_size,
                    Image.Resampling.LANCZOS)

            canvas.paste(
                tile_image,
                (round((tile.x - min_x) * pixels_per_unit),
                 round((tile.z - min_z) * pixels_per_unit)))

    if missing:
        print(
            f"Filled {len(missing)} absent client tiles with the map background; "
            f"first missing tile: {missing[0]}")

    if recovered:
        print(f"Recovered {recovered} absent client tiles from the asset source/cache")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output_path, "WEBP", quality=86, method=6)
    print(
        f"Built {output_path} from {len(selected_tiles)} tiles "
        f"at {canvas.width}x{canvas.height}")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Build a web map from Free Realms minimap tiles.")
    parser.add_argument("--client-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--min-x", type=float, required=True)
    parser.add_argument("--max-x", type=float, required=True)
    parser.add_argument("--min-z", type=float, required=True)
    parser.add_argument("--max-z", type=float, required=True)
    parser.add_argument("--asset-server")
    parser.add_argument("--asset-cache", type=Path)
    args = parser.parse_args()

    pack_paths = sorted(args.client_root.rglob("*.pack"))
    if not pack_paths:
        raise FileNotFoundError(f"No pack files found in {args.client_root}")

    assets: dict[str, PackAsset] = {}
    for pack_path in pack_paths:
        assets.update(read_pack_index(pack_path))

    tile_info_asset = assets.get("FabledRealms_TileInfo.txt")
    if tile_info_asset is None:
        raise FileNotFoundError("FabledRealms_TileInfo.txt is not installed")

    build_map(
        assets,
        parse_tile_info(read_asset(tile_info_asset)),
        (args.min_x, args.max_x, args.min_z, args.max_z),
        args.output,
        read_asset_manifest(args.client_root / "Assets_manifest.txt"),
        args.asset_server,
        args.asset_cache)


if __name__ == "__main__":
    main()
