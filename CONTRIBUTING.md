# Contributing to Lavagon

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting bugs

Open an [issue](https://github.com/funwkwk/lavagon/issues/new/choose) and include:

- Lavagon version (window title) and Windows version
- The versions of the components involved (e.g. `php-8.4.x-Win32-vs17-x64`, `httpd-2.4.x`)
- What you did, what you expected and what happened
- The relevant lines of `logs\lavagon.log` and the service log (`logs\apache_error.log`, `logs\mysqld.log`…)

Please remove passwords and private paths before posting logs.

## Development setup

You need Windows and the [.NET SDK](https://dotnet.microsoft.com/download) 6 or newer (no Visual Studio required).

```powershell
git clone https://github.com/funwkwk/lavagon.git
cd lavagon
dotnet build src\Lavagon -c Debug
# run against a throw-away folder so you never touch a real setup
src\Lavagon\bin\Debug\Lavagon.exe --root D:\lavagon-dev
```

`build.ps1` produces the portable folder in `dist\Lavagon`. Maintainers publish releases with `release.ps1` (see the README).

There is no automatic CI: please run `build.ps1` before opening a pull request and mention the result.

## Pull requests

1. Branch from `dev` and target `dev` with your pull request.
2. Keep changes focused; one feature or fix per pull request.
3. Follow the existing style (see `.editorconfig`): 4 spaces, C# `latest` language version, target .NET Framework 4.8.
4. Test with at least one old and one current PHP version when you touch PHP, Apache or Nginx code.
5. Describe how you tested in the pull request.

## Design rules

- **Portable**: everything lives in the Lavagon folder. No installer, no Windows services, no registry state except the optional *Run at startup* and *Add to Path*.
- **`bin\` is read-only**: never modify third-party binaries; generated configuration belongs in `etc\`.
- **Clear errors**: a failure should tell the user what to do (`LavagonException` messages are shown as-is).
- **Never touch what Lavagon didn't start**: stop and shutdown only processes recorded in `tmp\pids`.

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
