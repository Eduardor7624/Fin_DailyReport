# FinzatiDailyReport

Aplicación de consola .NET 8 para consultar `AmericaMarketFMPLogs`, generar un reporte HTML diario y enviarlo por SMTP. Está diseñada para ejecutarse desde Windows Task Scheduler.

## Qué incluye

- Resumen general de `AmericaMarketProcessRuns`.
- Desglose por aplicación, modo y estado.
- Errores de `AmericaMarketOperationLogs`, agrupados por ruta y excepción.
- Procesos con estado de error o `ErrorCount > 0`.
- Visitas generales, páginas, empresas, secciones y referrers.
- Clasificación automática: **ESTABLE**, **CON INCIDENCIAS** o **CRÍTICO**.
- Reporte HTML compatible con clientes de correo.
- Copia del reporte en `output` y logs locales en `logs`.
- Ejecución de prueba sin correo mediante `--no-send`.

## 1. Requisitos

- Windows con .NET 8 Runtime o SDK.
- Acceso de red a SQL Server.
- Credenciales SMTP. Para Zoho normalmente se usa `smtp.zoho.com`, puerto `587`, `StartTls`; si la cuenta usa MFA, configure una contraseña de aplicación.

## 2. Configuración

Edite `appsettings.json`:

1. Cambie la conexión `AmericaMarketFMPLogs`.
2. Configure SMTP y destinatarios.
3. Mantenga `DefaultDaysOffset: -1` para que el reporte enviado por la mañana corresponda al día anterior completo. Use `0` si desea enviar el día en curso.
4. Ajuste `CriticalErrorThreshold` según el volumen normal del sistema.

No publique el archivo con contraseñas en GitHub. Limite sus permisos NTFS a la cuenta que ejecutará la tarea.

## 3. Publicación

Ejecute:

```bat
scripts\publish-win-x64.cmd
```

Se creará la carpeta `publish`.

## 4. Prueba segura

Desde la raíz:

```bat
scripts\run-test.cmd
```

Esto consulta SQL y genera el HTML, pero no envía correo. Revise `publish\output`.

También puede solicitar una fecha específica:

```bat
publish\FinzatiDailyReport.exe --date 2026-07-24 --no-send
```

Cuando el resultado sea correcto:

```bat
scripts\run-production.cmd
```

## 5. Crear la tarea de Windows

Abra PowerShell como administrador:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\install-task.ps1 -RunAt "07:30"
```

La tarea usa la fecha anterior por defecto, por lo que a las 7:30 a. m. enviará el día completo previo.

## Observaciones de diseño

- Las consultas usan `@Fecha`; no dependen únicamente de `GETDATE()`. Esto permite reprocesar cualquier fecha.
- Se incluyen rutas de empresas tanto `/en/company/` como `/es/company/`.
- `ProcessException IS NOT NULL` evita contar filas normales sin excepción como errores operativos.
- Los accesos sospechosos (`/wp-login`, `/.env`, etc.) se cuentan aparte para no confundirlos con navegación legítima.
- Si no existen datos, el correo se genera igualmente y lo indica claramente.
