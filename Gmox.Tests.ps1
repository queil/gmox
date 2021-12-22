Describe 'Gmox' {

  BeforeAll { . "$PSScriptRoot/tests/resources/Gmox.Functions.ps1" }

  Context '-- as a global tool' -Tag 'global' {

    BeforeAll { Install-Gmox -Global }

    BeforeEach {

      Copy-Item "$PSScriptRoot/tests/integration/protos/" -Recurse TestDrive:/protos
      Push-Location
      Set-Location TestDrive:/protos
    }

    Describe 'mocking service' {
      It 'should work if pwd is proto root' {

        switch ($true) {
          $IsWindows {
            gmox serve --validate-only --root . --proto .\org\books\list\svc_list.proto | Write-Host
          }
          default {
            gmox serve --validate-only --root . --proto ./org/books/list/svc_list.proto | Write-Host
          }
        }
      }
    }

    AfterEach {
      Pop-Location
    }

    AfterAll { Uninstall-Gmox -Global }
  }
}
