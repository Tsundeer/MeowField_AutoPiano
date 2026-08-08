# Windows packaging and updates

MeowField_AutoPiano is published as a self-contained win-x64 application. Users do not need to install .NET Runtime separately.

The release installer is a WiX MSI package. It installs the complete application directory under Program Files, creates a Start Menu shortcut, appears in Windows Apps and Features, and supports major upgrades.

The Settings page checks the latest GitHub Release for a versioned `.msi` asset. The MSI is downloaded to a temporary update directory, launched through `msiexec.exe`, and removed after the installer exits whether installation succeeds or fails.

Build the current installer:

```powershell
.\scripts\publish-win-x64.ps1
```

The script keeps only `artifacts\\publish\\win-x64` and the current versioned MSI under `artifacts\\installer`. Portable ZIP files and old installer outputs are removed automatically.
