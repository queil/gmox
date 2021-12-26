if ($null -like $(Get-Module pester)) { Install-Module -Name Pester -Force }
Import-Module -Name Pester
Invoke-Pester -CI -Path ./tests/integration/Gmox.Tests.ps1
