# TenBoost

Ускоритель YouTube и Discord для Windows 10/11 x64. Ставится в один клик, работает без сервера.

TenBoost — оболочка вокруг движка [zapret2](https://github.com/bol-van/zapret2) от bol-van. Движок не модифицирован: он подключён сабмодулем `engine/`, закреплённым на релизном теге, а бинарники берутся из официального релиза этого тега с проверкой SHA256.

> Статус: этап 1 из 4 (каркас). Одна встроенная стратегия, без автоподбора.

## Как устроено

| Часть | Что делает |
|---|---|
| `service/` | Служба Windows `TenBoost` (LocalSystem). Единственная, кто запускает `winws2.exe` — только из своей папки и только с аргументами из каталога. Держит процесс в Job Object, перезапускает при сбое, пишет лог в `%ProgramData%\TenBoost\logs`. |
| `app/` | Значок в трее без прав администратора: вкл/выкл и «О программе». Говорит со службой по именованному каналу `TenBoost.Control` (только локальные пользователи, только команды `status`/`enable`/`disable`). |
| `core/` | Общая библиотека: каталог стратегий, подстановка путей, протокол канала, состояние, лог. |
| `strategies/catalog.json` | Стратегии. Пути только токенами `{lua:…}`, `{fake:…}`, `{filter:…}`, `{list:…}` — сослаться на файл вне папки установки нельзя. |
| `lists/` | Хостлисты YouTube и Discord. |
| `bin/manifest.json` | Откуда берутся бинарники движка и их SHA256. Сами бинарники в git не лежат. |
| `installer/` | Inno Setup: установка без единой страницы мастера, проверка конфликтов с GoodbyeDPI/zapret, регистрация службы, чистое удаление. |

## Сборка

Нужны .NET 8 SDK, Python 3.12, git с сабмодулями; для установщика — Windows и Inno Setup 6.

```
git clone --recurse-submodules https://github.com/tenvpn/tenboost
cd tenboost
dotnet test tests/TenBoost.Core.Tests
python -I tools/build_payload.py --out build/payload --version 0.1.0
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" /DAppVersion=0.1.0 /DPayload=%CD%\build\payload installer\TenBoost.iss
```

`build_payload.py` скачивает релизный архив zapret2, сверяет его SHA256 с `bin/manifest.json`, проверяет, что Lua, фейки и фильтры в архиве побайтно совпадают с сабмодулем, и публикует оба exe в одну папку с общим рантаймом .NET. Любое расхождение — ошибка сборки.

Релиз: тег `v*` → GitHub Actions собирает установщик и создаёт черновик релиза с `SHA256SUMS.txt` и ссылкой на прогон сборки.

## Обновление движка

1. `git -C engine fetch --depth 1 origin tag vX.Y.Z && git -C engine checkout vX.Y.Z`
2. В `bin/manifest.json` — новый тег, коммит, URL и SHA256 архива и четырёх бинарников.
3. `python -I tools/fetch_engine.py --out build/payload` должен пройти без ошибок.
4. Проверка у тестеров, потом тег релиза.

## Лицензия

Код TenBoost — MIT ([LICENSE](LICENSE)). Движок zapret2 — MIT © bol-van. WinDivert и Cygwin — LGPL. Подробности — [THIRD_PARTY.md](THIRD_PARTY.md); все тексты лицензий ставятся вместе с программой в папку `licenses`.
