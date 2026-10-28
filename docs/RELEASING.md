# Making a release

[По-русски ниже](#как-выпустить-версию)

1. Set the new version in two places:
   - `android/app/build.gradle.kts`: increase `versionCode` by one and set `versionName`;
   - `windows/src/AssemblyInfo.cs`: all three version lines.
2. Move the "Unreleased" notes in [CHANGELOG.md](../CHANGELOG.md) under the new version with today's date, in both languages.
3. Make sure `android\keystore.properties` points to the release key (see the comment in `android/app/build.gradle.kts`). Every release must be signed with the same key, or phones can't update.
4. Run `build.cmd`. It writes `dist\TapMirror.apk`, `dist\TapMirror.exe` and `dist\SHA256SUMS.txt`.
5. Install both on a phone and a PC and do one full session.
6. Commit, then tag: `git tag v0.1.0` and `git push origin main --tags`.
7. On GitHub: Releases → Draft a new release → choose the tag. Paste the version's notes from the changelog, and attach the three files from `dist\`. Keep the file names as they are: the README links to `releases/latest/download/TapMirror.apk` and `TapMirror.exe`.
8. For versions below 1.0, tick "Set as a pre-release" only if it's experimental; otherwise leave it as the latest release so the download links work.

---

# Как выпустить версию

1. Укажите новую версию в двух местах:
   - `android/app/build.gradle.kts`: увеличьте `versionCode` на один и задайте `versionName`;
   - `windows/src/AssemblyInfo.cs`: все три строки с версией.
2. Перенесите заметки из «Unreleased» в [CHANGELOG.md](../CHANGELOG.md) под новую версию с сегодняшней датой, на обоих языках.
3. Проверьте, что `android\keystore.properties` указывает на ключ выпуска (см. комментарий в `android/app/build.gradle.kts`). Все выпуски должны быть подписаны одним ключом, иначе телефоны не смогут обновиться.
4. Запустите `build.cmd`. Он создаст `dist\TapMirror.apk`, `dist\TapMirror.exe` и `dist\SHA256SUMS.txt`.
5. Установите оба файла на телефон и компьютер и проверьте один полный сеанс.
6. Сделайте коммит и тег: `git tag v0.1.0` и `git push origin main --tags`.
7. На GitHub: Releases → Draft a new release → выберите тег. Вставьте заметки к версии из changelog и приложите три файла из `dist\`. Не меняйте имена файлов: README ссылается на `releases/latest/download/TapMirror.apk` и `TapMirror.exe`.
8. Для версий до 1.0 отмечайте «Set as a pre-release», только если версия экспериментальная; иначе оставьте её последним выпуском, чтобы ссылки на скачивание работали.
