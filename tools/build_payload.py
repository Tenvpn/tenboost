"""Собирает папку, которую упаковывает установщик: движок, наши exe, каталог, списки, лицензии."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import fetch_engine  # noqa: E402

REPO_ROOT = fetch_engine.REPO_ROOT
PROJECTS = ['app/TenBoost.App', 'service/TenBoost.Service']
STATIC = {
    'strategies/catalog.json': 'strategies/catalog.json',
    'lists/youtube.txt': 'lists/youtube.txt',
    'lists/discord.txt': 'lists/discord.txt',
    'LICENSE': 'licenses/TenBoost-LICENSE.txt',
    'THIRD_PARTY.md': 'licenses/THIRD_PARTY.md',
    'licenses/WinDivert-LICENSE.txt': 'licenses/WinDivert-LICENSE.txt',
    'licenses/LGPL-3.0.txt': 'licenses/LGPL-3.0.txt',
    'licenses/GPL-3.0.txt': 'licenses/GPL-3.0.txt',
}


def publish(dotnet: str, out: Path, version: str) -> None:
    for project in PROJECTS:
        subprocess.run(
            [
                dotnet, 'publish', str(REPO_ROOT / project),
                '-c', 'Release',
                '-r', 'win-x64',
                '--self-contained',
                '-p:DebugType=none',
                f'-p:Version={version}',
                '-o', str(out),
            ],
            check=True,
            env={**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1'},
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=REPO_ROOT / 'build' / 'payload')
    parser.add_argument('--version', default='0.1.0')
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--cache', type=Path, help='локальная копия релизного архива zapret2')
    args = parser.parse_args()

    try:
        fetch_engine.build(REPO_ROOT / 'bin' / 'manifest.json', args.out, args.cache)
    except fetch_engine.FetchError as error:
        print(f'build_payload: {error}', file=sys.stderr)
        return 1

    for src, dst in STATIC.items():
        target = args.out / dst
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(REPO_ROOT / src, target)

    publish(args.dotnet, args.out, args.version)

    for required in ('TenBoost.exe', 'TenBoost.Service.exe', 'engine/winws2.exe', 'licenses/zapret2-LICENSE.txt'):
        if not (args.out / required).is_file():
            print(f'build_payload: в сборке нет {required}', file=sys.stderr)
            return 1

    print(f'build_payload: готово, {sum(1 for p in args.out.rglob("*") if p.is_file())} файлов в {args.out}')
    return 0


if __name__ == '__main__':
    # под -I PYTHONUTF8 не действует, а консоль Windows-раннера в cp1252
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    sys.exit(main())
