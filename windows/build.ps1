$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '缺少 .NET Framework 4.x 的 C# 编译器。'
}

$distribution = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'
New-Item -ItemType Directory -Force -Path $distribution | Out-Null
$executable = Join-Path $distribution 'Milofrog.exe'
$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    "/out:$executable",
    "/win32icon:$PSScriptRoot\assets\frog.ico",
    "/resource:$PSScriptRoot\assets\motion30.png,milofrog.png",
    "/reference:$framework\WPF\PresentationCore.dll",
    "/reference:$framework\WPF\PresentationFramework.dll",
    "/reference:$framework\WPF\WindowsBase.dll",
    '/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', "$PSScriptRoot\Program.cs"
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '奶蛙桌宠编译失败。' }
Write-Output $executable
