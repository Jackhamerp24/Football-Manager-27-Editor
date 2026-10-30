param([Parameter(Mandatory=$true)][string]$CopyRoot)
$ErrorActionPreference = 'Stop'
$cases = @(
    @{File='src/Fm27Editor.App/Modules.cs'; Old='CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace'; New='CompareOptions.IgnoreCase'; Test='SearchMatchesBothNamesWithoutCaseOrAccents'},
    @{File='src/Fm27Editor.Core/Players/PlayerRecord.cs'; Old='(byte)(display * scale)'; New='(byte)display'; Test='EditWritesOnlyCrossingAndUndoRestoresIt'},
    @{File='src/Fm27Editor.App/Backend.cs'; Old='return players.Reload(player.Address) ?? throw'; New='return player ?? throw'; Test='EditReloadsGameChangedAbility'},
    @{File='src/Fm27Editor.Core/Players/PlayerWriter.cs'; Old='if (!current.AsSpan().SequenceEqual(expected))'; New='if (false)'; Test='StaleEditDoesNotOverwriteGameValue'},
    @{File='src/Fm27Editor.App/Modules.cs'; Old='= [new PlayersModule()];'; New='= [];'; Test='RegistryContainsOnlyAvailablePlayersModuleAndNoActions'}
)
foreach ($case in $cases) {
    $path = Join-Path $CopyRoot $case.File
    $original = [IO.File]::ReadAllText($path)
    if (-not $original.Contains($case.Old)) { throw "Mutation target missing: $($case.Test)" }
    try {
        [IO.File]::WriteAllText($path, $original.Replace($case.Old, $case.New))
        $output = & dotnet test (Join-Path $CopyRoot 'tests/Fm27Editor.App.Tests/Fm27Editor.App.Tests.csproj') -c Release --filter "FullyQualifiedName~$($case.Test)" --no-restore 2>&1
        $code = $LASTEXITCODE
        $output | Select-String -Pattern 'Failed |Error Message|Assert\.' | ForEach-Object { $_.ToString() }
        if ($code -eq 0 -or -not ($output -match 'Failed!')) { throw "Mutation did not produce a test failure: $($case.Test)" }
        Write-Output "Detected mutation: $($case.Test)"
    } finally { [IO.File]::WriteAllText($path, $original) }
}
