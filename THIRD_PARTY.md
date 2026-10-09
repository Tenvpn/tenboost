# Сторонние компоненты

TenBoost — оболочка. Движок и драйвер не наши, и их лицензии едут в каждой копии установщика (`licenses\` в папке программы). Тексты GPL/LGPL лежат в `licenses/` этого репозитория, взяты из [spdx/license-list-data](https://github.com/spdx/license-list-data) и [basil00/WinDivert](https://github.com/basil00/WinDivert/blob/master/LICENSE).

| Компонент | Откуда | Лицензия | Файл лицензии в установке |
|---|---|---|---|
| zapret2: `winws2.exe`, Lua-библиотеки, фейки, фильтры WinDivert | [bol-van/zapret2](https://github.com/bol-van/zapret2), релизный архив тега из `bin/manifest.json` | MIT, © 2016-2026 bol-van | `licenses\zapret2-LICENSE.txt` (копия `engine/docs/LICENSE.txt`) |
| WinDivert: `WinDivert.dll`, `WinDivert64.sys` | в составе релиза zapret2; апстрим — [basil00/WinDivert](https://github.com/basil00/WinDivert) | LGPLv3 или GPLv2 на выбор | `licenses\WinDivert-LICENSE.txt` (тексты LGPLv3, GPLv3, GPLv2 внутри) |
| Cygwin: `cygwin1.dll` | в составе релиза zapret2 | LGPLv3 | `licenses\LGPL-3.0.txt` и `licenses\GPL-3.0.txt` (LGPLv3 ссылается на GPLv3) |
| Хостлист YouTube `lists/youtube.txt` | [bol-van/zapret-win-bundle](https://github.com/bol-van/zapret-win-bundle) `6eb463a`, `zapret-winws/files/list-youtube.txt` | — | — |

Движок не модифицируется: сабмодуль `engine/` закреплён на релизном теге, бинарники берутся из релизного архива этого тега с проверкой SHA256.

LGPL требует, чтобы получатель мог достать исходники библиотеки. Исходники WinDivert — [basil00/WinDivert](https://github.com/basil00/WinDivert), Cygwin — [cygwin.com](https://cygwin.com/git.html); версии — те, что собраны в релиз zapret2 указанного тега. Ссылки дублируются в «О программе».
