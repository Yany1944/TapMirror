# Security policy

[Русская версия ниже](#политика-безопасности)

## Supported versions

Only the latest release gets fixes. TapMirror is young and changes quickly, so please update before reporting.

## Reporting a problem

Please don't open a public issue for security problems. Use [private vulnerability reporting](https://github.com/Yany1944/TapMirror/security/advisories/new) on GitHub instead. Describe what an attacker could do, what they need for it (for example "on the same Wi-Fi"), and how to reproduce it. The maintainer will answer there as soon as possible. Fixes come out in a new release, and you'll be credited unless you'd rather not be.

## Known design limits

These are known and described in [docs/protocol.md](docs/protocol.md#security-notes), so they aren't vulnerabilities by themselves:

- The UDP messages between the phone and the PC aren't encrypted or authenticated. Someone on the same network can see them and send their own. The worst known result is that your PC opens a scrcpy window connected to *their* device.
- While a session runs, adb on the phone is reachable from the local network. adb still requires the PC's key to be allowed on the phone.

If you find a way around these limits, or anything that gives access to the phone or the PC beyond them, please report it.

---

# Политика безопасности

## Поддерживаемые версии

Исправления выходят только для последнего выпуска. TapMirror молодой и быстро меняется, поэтому перед отчётом обновитесь.

## Как сообщить о проблеме

Пожалуйста, не создавайте открытый issue о проблемах безопасности. Используйте [приватное сообщение об уязвимости](https://github.com/Yany1944/TapMirror/security/advisories/new) на GitHub. Опишите, что может сделать атакующий, что ему для этого нужно (например, «быть в той же сети Wi-Fi») и как это повторить. Сопровождающий ответит там как можно скорее. Исправление выйдет в новом выпуске, и вас упомянут, если вы не против.

## Известные ограничения конструкции

Они известны и описаны в [docs/protocol.md](docs/protocol.md#security-notes), поэтому сами по себе уязвимостями не считаются:

- UDP-сообщения между телефоном и компьютером не шифруются и не подписываются. Кто-то в той же сети может их видеть и отправлять свои. Худший известный результат: ваш компьютер откроет окно scrcpy, подключённое к *его* устройству.
- Пока идёт сеанс, adb на телефоне доступен из локальной сети. При этом adb всё равно требует, чтобы ключ компьютера был разрешён на телефоне.

Если вы нашли способ обойти эти ограничения или получить доступ к телефону или компьютеру сверх них, сообщите об этом.
