#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Assert-LastCommand([string] $Message) {
    if ($LASTEXITCODE -ne 0) { throw $Message }
}

Push-Location $projectRoot
try {
    foreach ($commandName in @('dotnet', 'docker')) {
        if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
            throw "Missing $commandName. Install prerequisites from README.md."
        }
    }
    dotnet --version
    Assert-LastCommand 'Install the SDK specified in global.json.'
    docker info --format '{{.OSType}}' | Out-Null
    Assert-LastCommand 'Start Docker Desktop using Linux containers, then retry.'

    $envPath = Join-Path $projectRoot '.env'
    if (-not (Test-Path -LiteralPath $envPath)) {
        Copy-Item -LiteralPath (Join-Path $projectRoot '.env.example') -Destination $envPath
    }

    $settings = @{}
    foreach ($line in Get-Content -LiteralPath $envPath) {
        if ($line -match '^\s*(#|$)') { continue }
        if ($line -notmatch '^([A-Z_]+)=(.*)$') {
            throw 'Use simple KEY=value lines in .env, without quotes or inline comments.'
        }
        $settings[$Matches[1]] = $Matches[2]
    }
    foreach ($key in @('COMPOSE_PROJECT_NAME', 'SQL_PORT', 'SQL_DATABASE', 'SQL_PASSWORD')) {
        if (-not $settings.ContainsKey($key)) { throw "Missing $key in .env." }
        if (Test-Path "Env:$key") {
            throw "Remove the shell variable $key first; it would override .env in Docker Compose."
        }
    }
    if ($settings.SQL_PASSWORD -eq 'CHANGE_ME') {
        $settings.SQL_PASSWORD = 'Lv1!' + [Guid]::NewGuid().ToString('N')
        $envText = [System.IO.File]::ReadAllText($envPath)
        $envText = $envText.Replace('SQL_PASSWORD=CHANGE_ME', 'SQL_PASSWORD=' + $settings.SQL_PASSWORD)
        [System.IO.File]::WriteAllText($envPath, $envText, [System.Text.UTF8Encoding]::new($false))
        Write-Host 'Generated a local development password in .env (ignored by Git).'
    }

    if ($settings.COMPOSE_PROJECT_NAME -notmatch '^[a-z0-9][a-z0-9_-]*$') {
        throw 'COMPOSE_PROJECT_NAME must contain lowercase letters, digits, underscores or hyphens.'
    }
    if ($settings.SQL_DATABASE -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') {
        throw 'SQL_DATABASE must start with a letter and contain only letters, digits or underscores.'
    }
    $sqlPort = 0
    if (-not [int]::TryParse($settings.SQL_PORT, [ref]$sqlPort) -or $sqlPort -lt 1024 -or $sqlPort -gt 65535) {
        throw 'SQL_PORT must be a number between 1024 and 65535.'
    }
    $password = $settings.SQL_PASSWORD
    if ($password -notmatch '^[A-Za-z0-9!@#%_+=.\-]{8,128}$') {
        throw 'SQL_PASSWORD: use 8-128 characters from letters/digits/!@#%_+=.- (no quotes or spaces).'
    }
    $categories = 0
    foreach ($pattern in @('[A-Z]', '[a-z]', '[0-9]', '[^A-Za-z0-9]')) {
        if ($password -cmatch $pattern) { $categories++ }
    }
    if ($categories -lt 3) { throw 'SQL_PASSWORD must use at least 3 character categories.' }

    dotnet restore LegacyVault.sln
    Assert-LastCommand 'NuGet restore failed. Check network access and SDK.'
    dotnet tool restore
    Assert-LastCommand 'dotnet-ef restore failed.'

    docker compose --env-file .env config --quiet
    Assert-LastCommand 'Invalid Docker Compose configuration.'
    docker compose --env-file .env up -d --wait --wait-timeout 180 sqlserver
    Assert-LastCommand 'SQL startup failed. Check Docker, free port, memory and the password of an existing volume. No data was deleted.'

    # Password stays in container environment, never interpolated into shell code or command arguments.
    $databaseName = $settings.SQL_DATABASE
    $createDatabaseSql = "IF DB_ID(N'$databaseName') IS NULL EXEC(N'CREATE DATABASE [$databaseName]');"
    $createDatabaseSql | docker compose --env-file .env exec -T sqlserver sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 10'
    Assert-LastCommand 'Could not create the empty development database.'

    $connectionString = "Server=127.0.0.1,$sqlPort;Database=$databaseName;User Id=sa;Password=$password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
    # Import JSON via stdin, so dotnet does not print the connection string on success.
    @{ 'ConnectionStrings:Default' = $connectionString } | ConvertTo-Json -Compress |
        dotnet user-secrets set --project src/LegacyVault.Api | Out-Null
    Assert-LastCommand 'Could not save local user-secrets.'

    dotnet build LegacyVault.sln --no-restore
    Assert-LastCommand 'Build failed.'

    Write-Host ''
    Write-Host "Ready. SQL Server: 127.0.0.1,$sqlPort; Database: $databaseName"
    Write-Host 'SQL username: sa; password: see your local .env file.'
    Write-Host 'Run: dotnet run --project src/LegacyVault.Api --launch-profile http'
    Write-Host 'Swagger: http://localhost:5000/swagger'
}
finally {
    Pop-Location
}
