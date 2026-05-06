param(
    [ValidateSet("workers", "integrations", "all")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"

function Assert-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Repository not found '$Label': $Path"
    }
}

function Assert-File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found '$Label': $Path"
    }
}

function Invoke-DockerBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Context,
        [Parameter(Mandatory = $true)]
        [string]$Dockerfile,
        [Parameter(Mandatory = $true)]
        [string]$Tag,
        [string[]]$BuildArgs = @()
    )

    Write-Host "==> build: $Name -> $Tag"
    $commandArgs = @("build", "-f", $Dockerfile, "-t", $Tag)
    foreach ($buildArg in $BuildArgs) {
        $commandArgs += @("--build-arg", $buildArg)
    }
    $commandArgs += $Context

    & docker @commandArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker build failed for '$Name' (tag=$Tag)."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workspaceRoot = (Resolve-Path (Join-Path $repoRoot "..")).Path

$workers = @(
    @{
        Name = "DDCRM worker-api"
        Repo = $repoRoot
        Dockerfile = Join-Path $repoRoot "docker/api.Dockerfile"
        Tag = "ddcrm/worker-api:local"
        BuildArgs = @("SERVICE_PROJECT=src/DDCRM.Worker.Api/DDCRM.Worker.Api.csproj")
    },
    @{
        Name = "DDCRM FunPay worker"
        Repo = (Join-Path $workspaceRoot "DDCRM-FunPay")
        Dockerfile = (Join-Path $workspaceRoot "DDCRM-FunPay/Dockerfile")
        Tag = "ddcrm/funpay-worker:local"
        BuildArgs = @()
    },
    @{
        Name = "DDCRM Playerok worker"
        Repo = (Join-Path $workspaceRoot "DDCRM-Playerok")
        Dockerfile = (Join-Path $workspaceRoot "DDCRM-Playerok/Dockerfile")
        Tag = "ddcrm/playerok-worker:local"
        BuildArgs = @()
    }
)

$integrations = @(
    @{
        Name = "DigitalDealsStats service"
        Repo = (Join-Path $workspaceRoot "DigitalDealsStats")
        Dockerfile = (Join-Path $workspaceRoot "DigitalDealsStats/Dockerfile")
        Tag = "ddcrm/marketstat:local"
        BuildArgs = @()
    },
    @{
        Name = "DDCRM-Steam worker runtime"
        Repo = (Join-Path $workspaceRoot "DDCRM-Steam")
        Dockerfile = (Join-Path $workspaceRoot "DDCRM-Steam/Dockerfile")
        Tag = "ddcrm/steam-worker:local"
        BuildArgs = @()
    }
)

function Build-Group {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GroupName,
        [Parameter(Mandatory = $true)]
        [object[]]$Definitions
    )

    Write-Host "==> group: $GroupName"
    foreach ($definition in $Definitions) {
        Assert-Directory -Path $definition.Repo -Label $definition.Name
        Assert-File -Path $definition.Dockerfile -Label "$($definition.Name) Dockerfile"
        Invoke-DockerBuild `
            -Name $definition.Name `
            -Context $definition.Repo `
            -Dockerfile $definition.Dockerfile `
            -Tag $definition.Tag `
            -BuildArgs $definition.BuildArgs
    }
}

switch ($Target) {
    "workers" {
        Build-Group -GroupName "workers" -Definitions $workers
    }
    "integrations" {
        Build-Group -GroupName "integrations" -Definitions $integrations
    }
    "all" {
        Build-Group -GroupName "workers" -Definitions $workers
        Build-Group -GroupName "integrations" -Definitions $integrations
    }
    default {
        throw "Unsupported target: $Target"
    }
}

Write-Host "[build-local-images] completed (target=$Target)."
