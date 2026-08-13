# Ветки и релизы
## Назначение веток

- `main` — стабильная история проекта;
- `dev` — интеграционная ветка для следующего набора изменений;
- `release/vMAJOR.MINOR.PATCH` — короткоживущая ветка конкретного релиза.

Wildcard не является именем Git-ветки, поэтому пустая `release/v*.*.*` не создаётся. Для каждого релиза нужна ветка с точной версией, например `release/v1.4.0`.

## Подготовка релиза

1. Обновите локальные `main` и `dev`, завершите проверки в `dev`.
2. Создайте ветку конкретной версии от проверенного коммита:

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

4. Отправьте release-ветку, затем создайте тег строго на её вершине:

   ```powershell
   git push -u origin release/v1.4.0
   git tag -a v1.4.0 -m "Release v1.4.0"
   git push origin v1.4.0
   ```

Workflow принимает только тег вида `vMAJOR.MINOR.PATCH` без суффиксов и ведущих нулей. Он загружает `release/vMAJOR.MINOR.PATCH` из `origin` и требует, чтобы тег и вершина ветки указывали на один commit. Проверка выполняется непосредственно в GitHub Actions, поэтому ручной fallback не требуется.

После успешной сборки GitHub Actions создаёт черновик GitHub Release, загружает архив, `release-manifest.json` и `SHA256SUMS.txt`, а затем автоматически публикует релиз. Если загрузка хотя бы одного файла не удалась, релиз остаётся черновиком. Уже опубликованный релиз workflow не перезаписывает.

`GITHUB_TOKEN` выдаётся workflow автоматически; отдельный пользовательский токен не нужен.
