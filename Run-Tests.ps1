if ($null -like $(gmo pester)) { Install-Module -Name Pester -Force }
Import-Module -Name Pester
Invoke-Pester -CI