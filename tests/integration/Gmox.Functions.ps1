$IsCiBuild = $null -ne $env:TF_BUILD
$SrcPath = $IsCiBuild ? $env:SRC_PATH : "src/Queil.Gmox.DotNet.Cli"
$NuGetSource = $IsCiBuild ? $env:BUILD_ARTIFACTSTAGINGDIRECTORY : "$SrcPath/nupkg/"
$PkgVer = $IsCiBuild ? $env:PKGVER : "0.0.0-dev"
$ManifestDir = $TestDrive
$ManifestFilePath = "$ManifestDir/.config/dotnet-tools.json"

function Install-Gmox {

  param(
    [switch]$Global = $false
  )
  $ErrorActionPreference = "Stop"
  $ToolType = $Global ? '--global' : '--local'

  if (!$IsCiBuild) {
    Write-Host "Running dotnet pack due to a local dev build"
    $NuGetPath = "($env:USERPROFILE)\.nuget\packages\queil.gmox.dotnet.cli\$PkgVer"
    if (Test-Path  $NuGetPath) { Remove-Item $NuGetPath -r -force }
    dotnet pack $SrcPath -c Release
  }

  Write-Host "Installing gmox (version: $PkgVer, $ToolType) from $NuGetSource"
  if ($Global) {
    dotnet tool install --global Queil.Gmox.DotNet.Cli --version $PkgVer --add-source $NuGetSource --configfile "$PSScriptRoot/NuGet.config"
  } 
  else 
  {
    dotnet new tool-manifest --output "$ManifestDir"
    dotnet tool install Queil.Gmox.DotNet.Cli --version $PkgVer --add-source $NuGetSource --tool-manifest $ManifestFilePath --configfile "$PSScriptRoot/NuGet.config"
  }
  
  if ($LASTEXITCODE -ne 0) {
    throw "Cannot install gmox"
  }
}

function Uninstall-Gmox {
  param(
    [switch]$Global = $false
  )
  
  if ($Global) {
    dotnet tool uninstall --global Queil.Gmox.DotNet.Cli
  }
  else {
    dotnet tool uninstall Queil.Gmox.DotNet.Cli --tool-manifest $ManifestFilePath
  }
}
