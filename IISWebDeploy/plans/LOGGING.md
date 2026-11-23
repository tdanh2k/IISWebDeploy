# Persistent file logging

The application uses the built-in [`FileLoggerProvider`](Services/FileLoggerProvider.cs:7); no logging package is added. Entries are JSON lines with UTC timestamp, level, category, event ID, rendered message, and exception details. Existing [`ILogger`](Services/FileLoggerProvider.cs:60) calls flow to the file provider at Information and higher. File writes are serialized within the process. Rotation retains numbered files when the active file exceeds its configured size.

## Deployed log location

The default deployed log is `logs/application.log`, relative to the application content root. Configure `Logging:File` in deployed `appsettings.json`; the configured path must remain relative and cannot traverse above the application directory. Rotated files are `application.log.1` through the configured retention count.

Grant the IIS application-pool identity Modify permission on the logs directory only, keep it outside public static content, and restrict read access to operators. Example from an elevated Windows Command Prompt:

```bat
icacls "D:\Sites\IISWebDeploy\logs" /grant "IIS AppPool\IISWebDeploy:(OI)(CI)M"
```

## Correlating a deployment attempt

Deployment lifecycle entries include `site`, `job`, and `operation` fields in the rendered structured message. Filter one attempt by its numeric job ID and operation (`deploy` or `restore`), then follow phase entries such as upload, validation/extraction, quiescence, snapshot, live-file replacement, health gate, rollback, and recovery. File I/O failures include the exact operation, full source and destination paths, exception details, and exception `HResult`.

The file logger records the failing operation and paths; it does not identify the process or handle that owns a lock. On Windows, investigate the owner externally with Process Monitor (filter `Path` and `Result = SHARING VIOLATION`), Resource Monitor's CPU > Associated Handles search, or Sysinternals Handle/Process Explorer. Run those tools with appropriate operator privileges and protect their output because paths and process details can be sensitive.

## IIS discovery and deployment permissions

The application logs expected IIS discovery permission/configuration/platform failures and remains available when discovery cannot read IIS configuration. Grant the application-pool identity read access to the required IIS configuration files, including `%SystemRoot%\\System32\\inetsrv\\config\\redirection.config`, and referenced configuration sources. Do not grant write access unless site-management operations require it.

For deployment, keep `stdoutLogEnabled="false"` in the deployed `web.config`. Logging failures are swallowed so they cannot fail startup or requests. Startup failures before the host and provider are constructed cannot be captured by this provider; use temporary IIS stdout diagnostics for those incidents, then disable stdout again. The provider coordinates writes only within one process; do not point multiple worker processes or applications at the same file.

## HTTP-only deployment warning

HTTP sends login passwords and authenticated session cookies without encryption. Restrict HTTP-only access to a trusted private network, block public exposure at the firewall, and prefer HTTPS whenever available.

## Self-check

Run from the project directory:

```sh
dotnet run -- --self-check
dotnet build
```

The self-check covers deployment safety ordering, IIS failure classification, authentication rendering, and concurrent file logger writes. Windows/IIS live deployment and external lock-owner identification are not testable in this Linux environment.
