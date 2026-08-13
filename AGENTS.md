# AGENTS.md — 5thMR Squad Autoseeder

## Ветки и релизы

### Версия приложения

Локальные значения версии задаются в `Autoseeder.Client.csproj` свойствами MSBuild `Version`, `AssemblyVersion`, `FileVersion` и `InformationalVersion`. Release workflow передаёт версию тега без префикса `v` в `Version` и `InformationalVersion`, а для `AssemblyVersion` и `FileVersion` добавляет четвёртый компонент `.0` (например, для `v1.4.0` это `1.4.0.0`).

### Назначение веток

- `main` — стабильная история проекта;
- `dev` — интеграционная ветка для следующего набора изменений;
- `release/vMAJOR.MINOR.PATCH` — короткоживущая ветка конкретного релиза.

Wildcard не является именем Git-ветки, поэтому пустая `release/v*.*.*` не создаётся. Для каждого релиза нужна ветка с точной версией, например `release/v1.4.0`.

### Подготовка релиза

1. Обновите локальные `main` и `dev`, завершите проверки в `dev`.
2. Создайте ветку конкретной версии от проверенного коммита `dev`:

   ```powershell
   git switch dev
   git switch -c release/v1.4.0
   ```

3. Выполните локальные проверки:

   ```powershell
   dotnet restore .\autoseeder.slnx
   dotnet build .\autoseeder.slnx -c Release --no-restore
   .\scripts\New-Release.ps1 -Version 1.4.0
   ```

4. Отправьте release-ветку, затем создайте аннотированный тег строго на её вершине:

   ```powershell
   git push -u origin release/v1.4.0
   git tag -a v1.4.0 -m "Release v1.4.0"
   git push origin v1.4.0
   ```

Push тега строго формата `vMAJOR.MINOR.PATCH` запускает `.github/workflows/release.yml`. Допускается только точное регулярное выражение `^v(?<version>(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*))$`: без суффиксов и ведущих нулей. Workflow загружает `release/vMAJOR.MINOR.PATCH` из `origin` и требует, чтобы тег и вершина ветки указывали на один commit. Проверка выполняется непосредственно в GitHub Actions, поэтому ручной fallback не требуется.

После успешной сборки GitHub Actions создаёт черновик GitHub Release, загружает self-contained single-file архив `win-x64`, `release-manifest.json` и `SHA256SUMS.txt`, а затем автоматически публикует релиз. Если загрузка хотя бы одного файла не удалась, релиз остаётся черновиком. Уже опубликованный релиз workflow не перезаписывает.

`GITHUB_TOKEN` выдаётся workflow автоматически; отдельный пользовательский токен не нужен.
