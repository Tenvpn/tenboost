"""Собирает движок zapret2 в папку полезной нагрузки установщика.

Бинарники берутся из релизного архива zapret2 и сверяются по SHA256 из bin/manifest.json.
Lua, фейки и фильтры берутся из сабмодуля engine/ и обязаны побайтно совпадать
с тем же архивом: winws2.exe и Lua должны быть из одного релиза, а zapret-lib.lua
в теге, master и zapret-win-bundle сейчас три разные версии.
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import io
import json
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

REPO_ROOT = Path(__file__).resolve().parent.parent


class FetchError(Exception):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def submodule_commit(engine_dir: Path) -> str:
    result = subprocess.run(
        ['git', '-C', str(engine_dir), 'rev-parse', 'HEAD'],
        capture_output=True,
        text=True,
        check=True,
    )
    return result.stdout.strip()


def load_archive(url: str, expected_sha256: str, cache: Path | None) -> bytes:
    if cache is not None and cache.is_file():
        data = cache.read_bytes()
    else:
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read()
        if cache is not None:
            cache.parent.mkdir(parents=True, exist_ok=True)
            cache.write_bytes(data)
    actual = sha256(data)
    if actual != expected_sha256:
        raise FetchError(f'архив {url}: sha256 {actual}, ожидался {expected_sha256}')
    return data


def write(out: Path, dst: str, data: bytes) -> None:
    target = out / PurePosixPath(dst)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)


def submodule_files(engine_dir: Path, src: str, pattern: str | None) -> list[tuple[str, Path]]:
    """Возвращает пары (путь относительно src, файл); для одиночного файла путь пустой."""
    source = engine_dir / PurePosixPath(src)
    if source.is_file():
        return [('', source)]
    if not source.is_dir():
        raise FetchError(f'в сабмодуле нет {src}')
    files = sorted(p for p in source.iterdir() if p.is_file())
    if pattern is not None:
        files = [p for p in files if fnmatch.fnmatch(p.name, pattern)]
    if not files:
        raise FetchError(f'в сабмодуле {src} нет файлов под шаблон {pattern}')
    return [(p.name, p) for p in files]


def build(manifest_path: Path, out: Path, cache: Path | None) -> list[str]:
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    engine = manifest['engine']
    engine_dir = REPO_ROOT / 'engine'

    commit = submodule_commit(engine_dir)
    if commit != engine['commit']:
        raise FetchError(f'сабмодуль engine/ на {commit}, манифест требует {engine["commit"]} ({engine["tag"]})')

    archive = engine['archive']
    data = load_archive(archive['url'], archive['sha256'], cache)
    root = PurePosixPath(archive['root'])
    written: list[str] = []

    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)

    with zipfile.ZipFile(io.BytesIO(data)) as zf:
        names = set(zf.namelist())

        def from_archive(rel: str) -> bytes:
            name = str(root / rel)
            if name not in names:
                raise FetchError(f'в архиве нет {name}')
            return zf.read(name)

        for entry in manifest['files']:
            blob = from_archive(entry['src'])
            actual = sha256(blob)
            if actual != entry['sha256']:
                raise FetchError(f'{entry["src"]}: sha256 {actual}, ожидался {entry["sha256"]}')
            write(out, entry['dst'], blob)
            written.append(entry['dst'])

        for entry in manifest['from_submodule']:
            for rel, path in submodule_files(engine_dir, entry['src'], entry.get('pattern')):
                src_rel = str(PurePosixPath(entry['src']) / rel) if rel else entry['src']
                dst_rel = str(PurePosixPath(entry['dst']) / rel) if rel else entry['dst']
                blob = path.read_bytes()
                if blob != from_archive(src_rel):
                    raise FetchError(f'{src_rel}: сабмодуль и релизный архив расходятся')
                write(out, dst_rel, blob)
                written.append(dst_rel)

    write(out, 'engine/ENGINE_VERSION', f'zapret2 {engine["tag"]} ({engine["commit"][:12]})\n'.encode())
    written.append('engine/ENGINE_VERSION')
    return written


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--manifest', type=Path, default=REPO_ROOT / 'bin' / 'manifest.json')
    parser.add_argument('--out', type=Path, required=True, help='папка полезной нагрузки, пересоздаётся')
    parser.add_argument('--cache', type=Path, help='локальная копия архива; скачивается, если её нет')
    args = parser.parse_args()
    try:
        written = build(args.manifest, args.out, args.cache)
    except FetchError as error:
        print(f'fetch_engine: {error}', file=sys.stderr)
        return 1
    for path in written:
        print(path)
    print(f'fetch_engine: {len(written)} файлов в {args.out}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
