# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

Изменения по-русски указаны под каждой версией.

## [Unreleased]

## [0.1.2] - 2026-10-29

### Added

- Video toggle: "Mirror phone video to the PC" in the phone app's settings. When turned off, scrcpy runs with `--no-video` for audio-only streaming. / Переключатель «Передавать видео» в настройках приложения. При выключении scrcpy запускается с параметром `--no-video` для трансляции только звука.
- Status notification in the shade indicates "Video transmission disabled" when video streaming is turned off. / В уведомлении в шторке указывается «Передача видео отключена», если трансляция видео выключена.

## [0.1.1] - 2026-10-29

### Added

- Updates. The PC app checks GitHub on start and once a day; when a newer release is out it shows a tray notification and an "Update to …" menu item, then downloads the new exe, checks its SHA-256, swaps it in and restarts. "Check for updates" is in the tray menu, and `updates=0` in the ini turns the check off. / Обновления. Программа на ПК проверяет GitHub при запуске и раз в день; если вышла новая версия, показывает уведомление и пункт «Update to …» в меню, затем скачивает новый exe, проверяет SHA-256, заменяет его и перезапускается. В меню есть «Check for updates», а `updates=0` в ini выключает проверку.
- The phone app checks for a newer release when you open it (at most once an hour) and shows a card with "Update" and "What's new". "Update" downloads the APK, checks its SHA-256 and installs it over the current version through Android's installer. / Приложение на телефоне при открытии (не чаще раза в час) проверяет новый выпуск и показывает карточку с кнопками «Обновить» и «Что нового». «Обновить» скачивает APK, проверяет SHA-256 и ставит его поверх текущей версии через установщик Android.
- `TapMirror.exe --check-updates` checks right away and offers the update. / `TapMirror.exe --check-updates` сразу проверяет обновления и предлагает установить.
- [docs/ai-setup.md](docs/ai-setup.md): a setup guide for AI assistants, and an "Easy setup" section in the README with a message to paste into one. / Инструкция для ИИ-помощников и раздел «Быстрая настройка» в README с сообщением, которое достаточно вставить в чат.

### Fixed

- `SHA256SUMS.txt` uses LF line endings, so `sha256sum -c` works on Linux and macOS. / В `SHA256SUMS.txt` переводы строк LF, поэтому `sha256sum -c` работает в Linux и macOS.
- The AI setup guide starts TapMirror through Explorer. Started from an AI tool's own shell (for example the Claude desktop app), it ran in the tool's sandbox, kept its settings in a private copy and closed with the tool. / Инструкция для ИИ запускает TapMirror через Проводник: запущенный из консоли ИИ-инструмента (например, Claude для компьютера), он работал в его «песочнице», хранил настройки в отдельной копии и закрывался вместе с ним.

## [0.1.0] - 2026-10-28

First public version. Tested on one phone and one PC (see "Tested on" in the README), so expect bugs.

### Added

- Phone app with a "Mirror to PC" quick settings tile. Tap to start, tap again to stop.
- Root mode: adb over TCP on port 5555, turned on only for the session.
- No-root mode: Android's Wireless debugging, switched by the app after a one-time permission grant from a PC.
- Debugging is put back as it was after the session, or always turned off, as you choose.
- The session ends by itself within about 12 seconds if the PC stops answering.
- Setup checklist in the app, with a button for each step.
- scrcpy settings on the phone: resolution, bitrate, frame rate, codec, audio, screen off, stay awake.
- "Tile only" mode with TapMirror's notifications turned off.
- "Enter address" for networks where searching can't find the PC, such as behind a VPN that apps can't bypass.
- "Copy info for a bug report" on the phone and on the PC.
- PC tray app: waits for the phone, asks once to allow a new phone, opens scrcpy in front of other windows, starts with Windows.
- PC tray app: downloads scrcpy from GitHub and checks its SHA-256, adds a firewall rule, and helps grant the no-root permission over USB or with a pairing code.
- English and Russian.

<details>
<summary>По-русски</summary>

Первая публичная версия. Проверена на одном телефоне и одном компьютере (см. «На чём проверено» в README), так что ошибки возможны.

- Приложение для телефона с плиткой «Экран на ПК» в шторке. Нажатие запускает трансляцию, повторное останавливает.
- Режим с root: adb по TCP на порту 5555, включается только на время сеанса.
- Режим без root: «Отладка по Wi-Fi», которую приложение переключает само после того, как компьютер один раз выдаст разрешение.
- После сеанса отладка возвращается как была или всегда выключается, на ваш выбор.
- Если компьютер перестал отвечать, сеанс заканчивается сам примерно через 12 секунд.
- Список шагов настройки в приложении с кнопкой для каждого шага.
- Настройки scrcpy на телефоне: разрешение, битрейт, частота кадров, кодек, звук, гашение экрана, запрет сна.
- Режим «только плитка» с выключенными уведомлениями TapMirror.
- «Ввести адрес» для сетей, где поиск не находит ПК, например при VPN, который приложения не могут обойти.
- «Скопировать сведения для отчёта» на телефоне и на компьютере.
- Программа в трее: ждёт телефон, один раз спрашивает разрешение для нового телефона, открывает scrcpy поверх других окон, запускается с Windows.
- Программа в трее: скачивает scrcpy с GitHub с проверкой SHA-256, добавляет правило брандмауэра и помогает выдать разрешение для режима без root по кабелю или по коду сопряжения.
- Английский и русский языки.

</details>

[Unreleased]: https://github.com/Yany1944/TapMirror/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/Yany1944/TapMirror/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/Yany1944/TapMirror/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Yany1944/TapMirror/releases/tag/v0.1.0
