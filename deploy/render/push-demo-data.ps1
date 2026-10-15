<#
.SYNOPSIS
  Демо-история (14 дней) — локально, затем в базу Render одним pg_restore.

.DESCRIPTION
  На бесплатном Render генератор истории шёл бы десятки минут (0,1 CPU), поэтому история строится на этой машине
  в локальном стеке (docker compose) и переносится дампом. Нужен только Docker.
  ВНИМАНИЕ: локальная база демо-стека (том mise_pgdata) пересоздаётся, а в базе Render все таблицы заменяются дампом.

.PARAMETER DatabaseUrl
  Render → mise-db → Connect → External Database URL (postgresql://mise:...@dpg-....frankfurt-postgres.render.com/mise).

.EXAMPLE
  ./deploy/render/push-demo-data.ps1 -DatabaseUrl "postgresql://mise:PASS@dpg-xxxx.frankfurt-postgres.render.com/mise"
#>
param(
    [Parameter(Mandatory = $true)][string]$DatabaseUrl,
    [int]$Days = 14
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..\..')

Write-Host '1/4 Чистая локальная база...'
docker compose down
docker volume rm mise_pgdata 2>$null
docker compose up -d postgres redis
Start-Sleep -Seconds 8

Write-Host "2/4 История за $Days дн. (пара минут)..."
docker compose run --rm --no-deps api --seed-demo-history --days $Days
if ($LASTEXITCODE -ne 0) { throw 'Генератор истории завершился с ошибкой' }

Write-Host '3/4 Дамп...'
docker compose exec -T postgres pg_dump -U mise -d mise -Fc --no-owner --no-privileges -f /tmp/mise.dump
docker compose cp postgres:/tmp/mise.dump ./mise.dump

Write-Host '4/4 Заливка в Render...'
$url = if ($DatabaseUrl -match '\?') { "$DatabaseUrl&sslmode=require" } else { "$DatabaseUrl`?sslmode=require" }
docker run --rm -v "${PWD}:/dump" postgres:16-alpine pg_restore --clean --if-exists --no-owner --no-privileges -d $url /dump/mise.dump
Remove-Item ./mise.dump

Write-Host 'Готово. Перезапустите mise-api в Render (Manual Deploy → Restart service), чтобы он подхватил данные.'
Write-Host 'Локальный стек снова поднимается: docker compose up -d'
