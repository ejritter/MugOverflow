# MugOverflow Project Directives & Build Rules

## Platform & Target Framework
- **Framework**: `.NET 11` (`net11.0-windows10.0.19041.0`, `net11.0-android`, `net11.0-ios`, `net11.0-maccatalyst`; headless test suite on `net11.0`), pinned via `global.json` (`11.0.100`, `rollForward: latestFeature`).
- **Default Theme**: `"Espresso Crema Dark"` (`Resources/Styles/Themes/CoffeeDarkTheme.xaml`), paired with 5 pure Coffeehouse Drink Flavor pairs (10 light/dark palettes).

## Build & Deployment Standard
- **Active Agent Iterations & Debugging**: During active development, testing, and intermediate verification cycles, agents use standard **Debug** builds without worrying about creating a release.
- **Final Verification & Deployment Build**: Once work, features, or bug fixes are finalized and verified, the **final build** must always be a **Release** build published directly to the Desktop application directory:
  `"$([Environment]::GetFolderPath('Desktop'))\MugOverflowApp"` *(or `"$env:USERPROFILE\Desktop\MugOverflowApp"`)*
- **Taskbar Shortcut Binding**: The pinned taskbar shortcut located at:
  `"%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\MugOverflow.lnk"`
  targets:
  `"$([Environment]::GetFolderPath('Desktop'))\MugOverflowApp\MugOverflowProject.exe"`
- **Final Publish Command**:
  ```powershell
  dotnet publish MugOverflowProject.csproj -f net11.0-windows10.0.19041.0 -c Release -o "$([Environment]::GetFolderPath('Desktop'))\MugOverflowApp"
  ```
  *(Note: Always target `MugOverflowProject.csproj` directly with `-o` rather than the solution file to avoid solution-level output conflicts).*
