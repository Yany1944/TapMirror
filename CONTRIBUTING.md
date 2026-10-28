# Contributing to TapMirror

[Русская версия ниже](#как-помочь-проекту)

Thanks for wanting to help. TapMirror is small, so most help is simple: try it on your phone and say what happened.

## Reporting bugs and testing

The most useful thing right now is testing on phones other than the Pixel 7 Pro it was built on. If it works on yours, a short note in [Discussions](https://github.com/Yany1944/TapMirror/discussions) with the phone, Android version and root or no root is already valuable. If it doesn't, open a [bug report](https://github.com/Yany1944/TapMirror/issues/new?template=bug_report.yml) and paste the info the app and the tray menu copy for you.

## Ideas

Open a [feature request](https://github.com/Yany1944/TapMirror/issues/new?template=feature_request.yml) before writing a lot of code, so we can agree on the approach first. Small fixes can go straight to a pull request.

## Building

- PC app: `windows\build.cmd` (uses the C# compiler that ships with Windows).
- Phone app: JDK 17+ and the Android SDK with platform 35, then `cd android` and `gradlew assembleDebug`.
- Both: `build.cmd` in the root folder, output in `dist\`.

To try a debug build next to a release one, uninstall the release first: the two are signed with different keys.

## Code style

- Follow the style of the file you're editing. Comments explain why, not what.
- The phone app is plain Java with no libraries. Please keep it that way unless there's a strong reason.
- The PC app must build with the C# 5 compiler in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, so no string interpolation, `?.`, `nameof` or other C# 6+ features. This is what lets anyone build it without installing anything.
- Every user-facing text in the phone app goes into both `res/values/strings.xml` and `res/values-ru/strings.xml`.
- Changes to the messages between phone and PC need an update to [docs/protocol.md](docs/protocol.md), and both sides must still talk to the previous version where possible.

## Translations

To add a language to the phone app, copy `android/app/src/main/res/values/strings.xml` to `values-<code>/strings.xml` (for example `values-de`), translate the texts and open a pull request. Strings marked `translatable="false"` stay as they are. The READMEs can be translated the same way (`README.<code>.md`).

## Pull requests

Describe what changed and how you tested it; the template asks for the phone and Windows version. Keep one topic per pull request.

---

# Как помочь проекту

Спасибо, что хотите помочь. TapMirror небольшой, так что чаще всего помощь простая: попробовать на своём телефоне и рассказать, что получилось.

## Ошибки и проверка

Сейчас полезнее всего проверка на других телефонах, не на Pixel 7 Pro, на котором его делали. Если у вас работает, короткой заметки в [Discussions](https://github.com/Yany1944/TapMirror/discussions) с моделью, версией Android и режимом (root или без) уже достаточно. Если не работает, создайте [отчёт об ошибке](https://github.com/Yany1944/TapMirror/issues/new?template=bug_report.yml) и вставьте сведения, которые копируют приложение и меню в трее.

## Идеи

Перед тем как писать много кода, создайте [предложение](https://github.com/Yany1944/TapMirror/issues/new?template=feature_request.yml), чтобы сначала договориться о подходе. Небольшие исправления можно сразу присылать pull request'ом.

## Сборка

- Программа для ПК: `windows\build.cmd` (использует компилятор C#, встроенный в Windows).
- Приложение для телефона: JDK 17+ и Android SDK с platform 35, затем `cd android` и `gradlew assembleDebug`.
- Оба сразу: `build.cmd` в корне, результат в `dist\`.

Чтобы поставить отладочную сборку поверх выпуска, сначала удалите выпуск: они подписаны разными ключами.

## Стиль кода

- Пишите так же, как написан файл, который правите. Комментарии объясняют «почему», а не «что».
- Приложение для телефона написано на чистой Java без библиотек. Пожалуйста, сохраняйте это без веской причины.
- Программа для ПК должна собираться компилятором C# 5 из `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, поэтому без интерполяции строк, `?.`, `nameof` и других возможностей C# 6+. Благодаря этому её может собрать кто угодно, ничего не устанавливая.
- Все тексты, которые видит пользователь, добавляются и в `res/values/strings.xml`, и в `res/values-ru/strings.xml`.
- Изменения в сообщениях между телефоном и ПК нужно описать в [docs/protocol.md](docs/protocol.md), и по возможности обе стороны должны работать с предыдущей версией.

## Переводы

Чтобы добавить язык в приложение, скопируйте `android/app/src/main/res/values/strings.xml` в `values-<код>/strings.xml` (например, `values-de`), переведите тексты и пришлите pull request. Строки с `translatable="false"` не переводятся. README переводится так же (`README.<код>.md`).

## Pull request

Опишите, что изменилось и как вы проверяли; шаблон спросит модель телефона и версию Windows. Один pull request, одна тема.
