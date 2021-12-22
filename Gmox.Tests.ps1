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

        $ProtoPath =
          switch ($true) {
            $IsWindows { ".\org\books\list\svc_list.proto" }
            default { "./org/books/list/svc_list.proto" }
          }

        $out = gmox serve --validate-only --root . --proto $ProtoPath
        $? | Should -BeTrue -Because "Error: $out"
      }
    }

    AfterEach {
      Pop-Location
    }

    AfterAll { Uninstall-Gmox -Global }
  }
}
