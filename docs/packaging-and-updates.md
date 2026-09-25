# Windows packaging and updates

MeowField_AutoPiano is published as a self-contained win-x64 application. Users do not need to install .NET Runtime separately.

The release installer is an Inno Setup EXE package. It installs the complete application directory under Program Files, creates Start Menu / desktop shortcuts, appears in Windows Apps and Features, and supports in-place upgrades. The installer requests administrator privileges and closes a running instance before upgrading.

The Settings page checks the latest GitHub Release for an asset whose name ends with `Setup.exe`. The installer is downloaded to a temporary update directory, launched after the app exits, and removed whether installation succeeds or fails.

Build the current installer:

```powershell
.\scripts\publish-win-x64.ps1
```

The script reads the version from `src\MeowField.App\MeowField.App.csproj`, publishes self-contained binaries to `artifacts\publish\win-x64`, compiles `installer\MeowField_AutoPiano.iss` with Inno Setup 6, and keeps only the current versioned `MeowField_AutoPiano-<version>-win-x64-Setup.exe` under `artifacts\installer`. Old installer outputs are removed automatically.

Releasing: commit the version bump, push, then create a GitHub tag `v<version>` with a release that attaches the Setup EXE.
